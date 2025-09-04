using System.Runtime.CompilerServices;
using Optx.Core.Events;
using Optx.Core.Interfaces;
using Optx.Core.Types;
using Optx.Core.Utils;
using Optx.Pricing;

namespace Optx.Strategies;

/// <summary>
/// Covered call strategy that sells calls against long underlying positions
/// </summary>
public sealed class CoveredCallStrategy : IStrategy
{
    private readonly CoveredCallConfig _config;
    private readonly Dictionary<string, StrategyState> _positions;
    private readonly Dictionary<string, object> _state;

    public CoveredCallStrategy(CoveredCallConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _positions = new Dictionary<string, StrategyState>();
        _state = new Dictionary<string, object>();
        
        Name = "CoveredCall";
    }

    public string Name { get; }

    /// <summary>
    /// Process market event and generate orders
    /// </summary>
    public IEnumerable<Order> OnEvent(in MarketEvent marketEvent, in PortfolioState portfolioState)
    {
        var orders = new List<Order>();

        if (marketEvent.TryGetMarketTick(out var tick))
        {
            orders.AddRange(ProcessMarketTick(tick, portfolioState));
        }
        else if (marketEvent.TryGetQuoteUpdate(out var quote))
        {
            orders.AddRange(ProcessQuote(quote, portfolioState));
        }

        return orders;
    }

    private IEnumerable<Order> ProcessMarketTick(MarketTick tick, PortfolioState portfolioState)
    {
        var symbol = tick.Symbol.ToString();
        
        // Only process configured symbols
        if (!_config.Symbols.Contains(symbol))
            return Array.Empty<Order>();

        var orders = new List<Order>();

        // Check if we need to establish or roll positions
        if (!_positions.TryGetValue(symbol, out var strategyState))
        {
            strategyState = new StrategyState(symbol);
            _positions[symbol] = strategyState;
        }

        // Check for entry signals
        var currentPosition = portfolioState.GetPosition(symbol.AsSpan());
        var underlyingShares = currentPosition?.Quantity ?? 0;

        // If we have underlying shares but no covered call, sell a call
        if (underlyingShares >= _config.LotSize && !strategyState.HasActiveCalls)
        {
            var callOrder = CreateCoveredCallOrder(symbol, tick.Price, underlyingShares, tick.TimestampNs);
            if (callOrder != null)
            {
                orders.Add(callOrder.Value);
                strategyState.HasActiveCalls = true;
                strategyState.LastAction = StrategyAction.SellCall;
                strategyState.LastActionTime = tick.TimestampNs;
            }
        }

        // Check for roll conditions
        if (strategyState.HasActiveCalls)
        {
            orders.AddRange(CheckRollConditions(symbol, tick, portfolioState, strategyState));
        }

        return orders;
    }

    private IEnumerable<Order> ProcessQuote(QuoteUpdate quote, PortfolioState portfolioState)
    {
        // For now, just process as market tick with mid price
        var syntheticTick = new MarketTick(
            quote.TimestampNs,
            quote.Symbol,
            quote.GetMidPrice(),
            100,
            MarketDataType.Quote);
        
        return ProcessMarketTick(syntheticTick, portfolioState);
    }

    private Order? CreateCoveredCallOrder(string underlying, decimal underlyingPrice, int shares, ulong timestamp)
    {
        // Calculate strike price for target delta range
        var targetDelta = (_config.MinDelta + _config.MaxDelta) / 2.0;
        var daysToExpiry = _config.TargetDaysToExpiry;
        var timeToExpiry = daysToExpiry / 365.25;

        // Estimate strike for target delta (simplified calculation)
        var volatility = 0.25; // Assumed vol - in real system would use vol surface

        // Find strike that gives approximately target delta
        var atmStrike = (double)underlyingPrice;
        var targetStrike = atmStrike * (1.0 + targetDelta * volatility * Math.Sqrt(timeToExpiry));
        var strikePrice = RoundToNearestStrike((decimal)targetStrike);

        // Create call option order
        var contracts = shares / 100; // Convert shares to contracts
        var orderId = $"CC_{underlying}_{timestamp}_{Random.Shared.Next(1000, 9999)}";

        return new OrderBuilder(orderId)
            .Symbol($"{underlying}_CALL_{strikePrice:F0}".AsMemory())
            .Side(OrderSide.Sell)
            .Type(OrderType.Limit)
            .Quantity(contracts)
            .Price(EstimateCallPrice(strikePrice, underlyingPrice, timeToExpiry, volatility))
            .TimestampNs(timestamp)
            .Build();
    }

    private IEnumerable<Order> CheckRollConditions(
        string symbol, 
        MarketTick tick, 
        PortfolioState portfolioState, 
        StrategyState strategyState)
    {
        var orders = new List<Order>();
        
        // Check time-based roll (simplified)
        var daysSinceLastAction = (tick.TimestampNs - strategyState.LastActionTime) / (24 * 60 * 60 * 1_000_000_000.0);
        
        if (daysSinceLastAction >= _config.RollAtDte)
        {
            // Roll the position
            var rollOrders = CreateRollOrders(symbol, tick.Price, portfolioState, tick.TimestampNs);
            orders.AddRange(rollOrders);
            
            strategyState.LastAction = StrategyAction.Roll;
            strategyState.LastActionTime = tick.TimestampNs;
        }

        return orders;
    }

    private IEnumerable<Order> CreateRollOrders(string underlying, decimal underlyingPrice, PortfolioState portfolioState, ulong timestamp)
    {
        var orders = new List<Order>();

        // Buy back current calls (simplified - assumes we can identify them)
        var buyBackOrderId = $"ROLL_BUY_{underlying}_{timestamp}";
        var sellNewOrderId = $"ROLL_SELL_{underlying}_{timestamp}";

        // In a real implementation, would track specific call contracts
        // For now, create placeholder orders
        
        return orders;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal RoundToNearestStrike(decimal price)
    {
        // Round to nearest $5 for most stocks
        return Math.Round(price / 5m) * 5m;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal EstimateCallPrice(decimal strike, decimal underlying, double timeToExpiry, double volatility)
    {
        // Simplified Black-Scholes call price estimation
        var price = BlackScholes.Price(
            (double)underlying,
            (double)strike,
            timeToExpiry,
            volatility,
            0.05, // Risk-free rate
            0.01, // Dividend yield
            OptionType.Call);
        
        return (decimal)price;
    }

    public void OnFill(in Fill fill, in PortfolioState portfolioState)
    {
        // Update strategy state based on fill
        var symbol = ExtractUnderlyingSymbol(fill.OrderId);
        if (_positions.TryGetValue(symbol, out var state))
        {
            state.LastFillTime = fill.TimestampNs;
            state.LastFillPrice = fill.FillPrice;
        }
    }

    public void OnOrderAck(in OrderAck orderAck)
    {
        // Track order acknowledgments
        _state[$"LastAck_{orderAck.OrderId}"] = orderAck.Status;
    }

    public IReadOnlyDictionary<string, object> GetState()
    {
        var state = new Dictionary<string, object>(_state);
        
        state["PositionCount"] = _positions.Count;
        state["ActiveCalls"] = _positions.Values.Count(p => p.HasActiveCalls);
        state["ConfigMinDelta"] = _config.MinDelta;
        state["ConfigMaxDelta"] = _config.MaxDelta;
        
        return state;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string ExtractUnderlyingSymbol(string orderId)
    {
        // Extract underlying symbol from order ID (simplified)
        var parts = orderId.Split('_');
        return parts.Length > 1 ? parts[1] : "UNKNOWN";
    }
}

/// <summary>
/// Strategy state for tracking positions
/// </summary>
internal sealed class StrategyState
{
    public string Symbol { get; }
    public bool HasActiveCalls { get; set; }
    public StrategyAction LastAction { get; set; }
    public ulong LastActionTime { get; set; }
    public ulong LastFillTime { get; set; }
    public decimal LastFillPrice { get; set; }

    public StrategyState(string symbol)
    {
        Symbol = symbol;
        HasActiveCalls = false;
        LastAction = StrategyAction.None;
        LastActionTime = 0;
        LastFillTime = 0;
        LastFillPrice = 0m;
    }
}

/// <summary>
/// Strategy actions
/// </summary>
internal enum StrategyAction
{
    None,
    SellCall,
    BuyCall,
    Roll
}

/// <summary>
/// Covered call strategy configuration
/// </summary>
public sealed class CoveredCallConfig
{
    public double MinDelta { get; set; } = 0.25;
    public double MaxDelta { get; set; } = 0.35;
    public int TargetDaysToExpiry { get; set; } = 30;
    public int RollAtDte { get; set; } = 21;
    public double RollAtPnLPercent { get; set; } = 0.5;
    public int LotSize { get; set; } = 100;
    public int MaxPositions { get; set; } = 10;
    public List<string> Symbols { get; set; } = new() { "SPY" };
}
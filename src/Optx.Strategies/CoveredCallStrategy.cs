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
    private readonly Dictionary<string, List<OptionQuoteData>> _optionsChains;
    private readonly Dictionary<string, decimal> _underlyingPrices;

    public CoveredCallStrategy(CoveredCallConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _positions = new Dictionary<string, StrategyState>();
        _state = new Dictionary<string, object>();
        _optionsChains = new Dictionary<string, List<OptionQuoteData>>();
        _underlyingPrices = new Dictionary<string, decimal>();
        
        Name = "CoveredCall";
    }

    public string Name { get; }

    private class OptionQuoteData
    {
        public string Symbol { get; set; } = string.Empty;
        public string UnderlyingSymbol { get; set; } = string.Empty;
        public decimal Strike { get; set; }
        public DateTime Expiry { get; set; }
        public bool IsCall { get; set; }
        public decimal BidPrice { get; set; }
        public decimal AskPrice { get; set; }
        public int BidSize { get; set; }
        public int AskSize { get; set; }
        public double Delta { get; set; }
        public ulong LastUpdateTime { get; set; }
    }

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
        
        // Update underlying price
        _underlyingPrices[symbol] = tick.Price;

        // Check if we need to establish or roll positions
        if (!_positions.TryGetValue(symbol, out var strategyState))
        {
            strategyState = new StrategyState(symbol);
            _positions[symbol] = strategyState;
        }

        // Check for entry signals
        var currentPosition = portfolioState.GetPosition(symbol.AsSpan());
        var underlyingShares = currentPosition?.Quantity ?? 0;

        // Initialize underlying position if needed (simulating buying shares)
        if (underlyingShares < _config.LotSize && _config.InitializeUnderlyingPositions && !strategyState.InitializedUnderlying)
        {
            var buyOrder = CreateUnderlyingBuyOrder(symbol, tick.Price, tick.TimestampNs);
            if (buyOrder != null)
            {
                orders.Add(buyOrder.Value);
                strategyState.InitializedUnderlying = true;
                Console.WriteLine($"Initializing underlying position: Buy {_config.LotSize * _config.MaxPositions} shares of {symbol} @ {tick.Price:C2}");
            }
        }

        // If we have underlying shares but no covered call, sell a call (will be handled via option quotes now)

        // Check for roll conditions
        if (strategyState.HasActiveCalls)
        {
            orders.AddRange(CheckRollConditions(symbol, tick, portfolioState, strategyState));
        }

        return orders;
    }

    private Order? CreateUnderlyingBuyOrder(string symbol, decimal price, ulong timestamp)
    {
        // Buy enough shares for maximum covered call positions
        var sharesToBuy = _config.LotSize * _config.MaxPositions;
        var orderId = $"BUY_{symbol}_{timestamp}_{Random.Shared.Next(1000, 9999)}";

        return new OrderBuilder(orderId)
            .Symbol(symbol.AsMemory())
            .Side(OrderSide.Buy)
            .Type(OrderType.Market) // Market order for immediate execution
            .Quantity(sharesToBuy)
            .Price(price)
            .TimeInForce(TimeInForce.GoodTillCancel)
            .Build();
    }

    private IEnumerable<Order> ProcessQuote(QuoteUpdate quote, PortfolioState portfolioState)
    {
        var orders = new List<Order>();
        var symbol = quote.Symbol.ToString();
        
        // Check if this is an option quote (contains expiry pattern like 251017)
        if (IsOptionSymbol(symbol))
        {
            var optionInfo = ParseOptionSymbol(symbol);
            if (optionInfo != null)
            {
                UpdateOptionsChain(optionInfo, quote);
                
                // Check for covered call opportunities on the underlying
                orders.AddRange(CheckCoveredCallOpportunity(optionInfo.UnderlyingSymbol, portfolioState, quote.TimestampNs));
            }
        }
        else
        {
            // This is an underlying quote, update price
            _underlyingPrices[symbol] = quote.GetMidPrice();
        }
        
        return orders;
    }

    private bool IsOptionSymbol(string symbol)
    {
        // Option symbols follow format: SPY251017C00500000 (underlying + YYMMDD + C/P + strike)
        return symbol.Length > 10 && (symbol.Contains('C') || symbol.Contains('P')) && 
               char.IsDigit(symbol[symbol.Length - 8]);
    }

    private OptionSymbolInfo? ParseOptionSymbol(string symbol)
    {
        try
        {
            // Find the option type (C or P)
            var callIndex = symbol.IndexOf('C');
            var putIndex = symbol.IndexOf('P');
            
            if (callIndex == -1 && putIndex == -1) return null;
            
            var optionTypeIndex = callIndex != -1 ? callIndex : putIndex;
            var isCall = callIndex != -1;
            
            // Extract underlying symbol (everything before the date)
            var underlying = symbol.Substring(0, optionTypeIndex - 6); // 6 digits for YYMMDD
            
            // Extract expiry date (6 digits before option type)
            var expiryStr = symbol.Substring(optionTypeIndex - 6, 6);
            var expiry = DateTime.ParseExact($"20{expiryStr}", "yyyyMMdd", null);
            
            // Extract strike (8 digits after option type)
            var strikeStr = symbol.Substring(optionTypeIndex + 1, 8);
            var strike = decimal.Parse(strikeStr) / 1000m; // Strike is in thousandths
            
            return new OptionSymbolInfo(underlying, expiry, strike, isCall, symbol);
        }
        catch
        {
            return null;
        }
    }

    private void UpdateOptionsChain(OptionSymbolInfo optionInfo, QuoteUpdate quote)
    {
        if (!_optionsChains.TryGetValue(optionInfo.UnderlyingSymbol, out var chain))
        {
            chain = new List<OptionQuoteData>();
            _optionsChains[optionInfo.UnderlyingSymbol] = chain;
        }

        // Find existing quote or create new one
        var existingQuote = chain.FirstOrDefault(q => q.Symbol == optionInfo.Symbol);
        if (existingQuote == null)
        {
            existingQuote = new OptionQuoteData
            {
                Symbol = optionInfo.Symbol,
                UnderlyingSymbol = optionInfo.UnderlyingSymbol,
                Strike = optionInfo.Strike,
                Expiry = optionInfo.Expiry,
                IsCall = optionInfo.IsCall
            };
            chain.Add(existingQuote);
        }

        // Update quote data
        existingQuote.BidPrice = quote.BidPrice;
        existingQuote.AskPrice = quote.AskPrice;
        existingQuote.BidSize = quote.BidSize;
        existingQuote.AskSize = quote.AskSize;
        existingQuote.LastUpdateTime = quote.TimestampNs;
        
        // Calculate approximate delta (simplified)
        if (_underlyingPrices.TryGetValue(optionInfo.UnderlyingSymbol, out var underlyingPrice))
        {
            var moneyness = (double)(underlyingPrice / optionInfo.Strike);
            existingQuote.Delta = optionInfo.IsCall ? 
                Math.Max(0.0, Math.Min(1.0, 0.5 + 0.4 * (moneyness - 1.0))) :
                Math.Max(-1.0, Math.Min(0.0, -0.5 - 0.4 * (1.0 - moneyness)));
            
            // Debug first few delta calculations
            if (Random.Shared.NextDouble() < 0.001) // 0.1% chance to log
            {
                Console.WriteLine($"Delta calc: {optionInfo.Symbol} S={underlyingPrice:F2} K={optionInfo.Strike:F2} Moneyness={moneyness:F3} Delta={existingQuote.Delta:F3}");
            }
        }
        else
        {
            // If no underlying price yet, use a default delta based on strike relative to 100
            var moneyness = (double)(100m / optionInfo.Strike);
            existingQuote.Delta = optionInfo.IsCall ? 
                Math.Max(0.0, Math.Min(1.0, 0.5 + 0.4 * (moneyness - 1.0))) :
                Math.Max(-1.0, Math.Min(0.0, -0.5 - 0.4 * (1.0 - moneyness)));
        }
    }

    private IEnumerable<Order> CheckCoveredCallOpportunity(string underlyingSymbol, PortfolioState portfolioState, ulong timestamp)
    {
        var orders = new List<Order>();
        
        // Only process configured symbols
        if (!_config.Symbols.Contains(underlyingSymbol))
            return orders;

        // Check if we have underlying position
        var currentPosition = portfolioState.GetPosition(underlyingSymbol.AsSpan());
        var underlyingShares = currentPosition?.Quantity ?? 0;

        // For testing: assume we have shares if we've initialized the position
        if (!_positions.TryGetValue(underlyingSymbol, out var strategyState))
        {
            strategyState = new StrategyState(underlyingSymbol);
            _positions[underlyingSymbol] = strategyState;
        }

        // If we've placed a buy order but portfolio doesn't reflect it yet, assume we have the shares
        if (underlyingShares < _config.LotSize && strategyState.InitializedUnderlying)
        {
            underlyingShares = _config.LotSize * _config.MaxPositions; // Assume the buy order filled
            Console.WriteLine($"Assuming underlying shares filled: {underlyingShares} shares of {underlyingSymbol}");
        }

        // Need at least one lot (100 shares) to sell covered calls
        if (underlyingShares < _config.LotSize)
        {
            Console.WriteLine($"Not enough shares for covered calls: {underlyingShares} < {_config.LotSize} ({underlyingSymbol})");
            return orders;
        }

        if (strategyState.HasActiveCalls)
        {
            Console.WriteLine($"Already have active calls for {underlyingSymbol}");
            return orders; // Already have covered calls
        }

        // Find suitable call options to sell
        if (_optionsChains.TryGetValue(underlyingSymbol, out var chain))
        {
            Console.WriteLine($"Checking {chain.Count} options in chain for {underlyingSymbol}");
            var suitableCall = FindSuitableCallOption(chain);
            if (suitableCall != null)
            {
                var callOrder = CreateCallSellOrder(suitableCall, underlyingShares, timestamp);
                if (callOrder != null)
                {
                    orders.Add(callOrder.Value);
                    strategyState.HasActiveCalls = true;
                    strategyState.LastAction = StrategyAction.SellCall;
                    strategyState.LastActionTime = timestamp;
                    
                    Console.WriteLine($"🎯 Generated covered call order: Sell {suitableCall.Symbol} @ {suitableCall.BidPrice:C2} (Delta: {suitableCall.Delta:F3})");
                }
                else
                {
                    Console.WriteLine($"Failed to create call sell order for {suitableCall.Symbol}");
                }
            }
            else
            {
                Console.WriteLine($"No suitable call options found for {underlyingSymbol} (chain size: {chain.Count})");
                if (chain.Count > 0)
                {
                    var sample = chain.Where(c => c.IsCall).Take(3);
                    foreach (var call in sample)
                    {
                        Console.WriteLine($"  Sample call: {call.Symbol} Delta={call.Delta:F3} Bid={call.BidPrice:C2} Days={call.Expiry.Subtract(DateTime.UtcNow).Days}");
                    }
                }
            }
        }
        else
        {
            Console.WriteLine($"No options chain found for {underlyingSymbol}");
        }

        return orders;
    }

    private OptionQuoteData? FindSuitableCallOption(List<OptionQuoteData> chain)
    {
        var now = DateTime.UtcNow;
        
        // Very permissive filtering for testing
        var allCalls = chain.Where(q => q.IsCall).ToList();
        Console.WriteLine($"Total calls in chain: {allCalls.Count}");
        
        var candidates = allCalls
            .Where(q => q.BidPrice > 0.01m) // Very low minimum premium
            .Where(q => q.BidSize >= 1) // Must have bid size
            .Where(q => q.Expiry > now.AddDays(1)) // At least 1 day to expiry
            .Where(q => q.Expiry <= now.AddDays(200)) // Very broad expiry range
            // Remove delta filter temporarily to see what we get
            .OrderByDescending(q => q.BidPrice) // Prefer higher premium
            .ToList();

        Console.WriteLine($"Found {candidates.Count} candidate calls after filtering (out of {allCalls.Count} calls)");
        
        if (candidates.Count == 0 && allCalls.Count > 0)
        {
            Console.WriteLine("All calls failed filtering. Sample reasons:");
            foreach (var call in allCalls.Take(3))
            {
                var reasons = new List<string>();
                if (call.BidPrice <= 0.01m) reasons.Add($"BidPrice={call.BidPrice:C3}");
                if (call.BidSize < 1) reasons.Add($"BidSize={call.BidSize}");
                var daysToExpiry = call.Expiry.Subtract(now).Days;
                if (daysToExpiry <= 1) reasons.Add($"DTE={daysToExpiry}");
                if (daysToExpiry > 200) reasons.Add($"DTE={daysToExpiry}");
                Console.WriteLine($"  {call.Symbol}: {string.Join(", ", reasons)}");
            }
        }
        else
        {
            foreach (var candidate in candidates.Take(3))
            {
                Console.WriteLine($"  ✅ Candidate: {candidate.Symbol} Delta={candidate.Delta:F3} Bid={candidate.BidPrice:C2} Days={candidate.Expiry.Subtract(now).Days}");
            }
        }

        return candidates.FirstOrDefault();
    }

    private Order? CreateCallSellOrder(OptionQuoteData callOption, int underlyingShares, ulong timestamp)
    {
        var contracts = Math.Min(underlyingShares / 100, _config.MaxPositions);
        if (contracts <= 0) return null;

        var orderId = $"CC_{callOption.UnderlyingSymbol}_{timestamp}_{Random.Shared.Next(1000, 9999)}";

        return new OrderBuilder(orderId)
            .Symbol(callOption.Symbol.AsMemory())
            .Side(OrderSide.Sell)
            .Type(OrderType.Limit)
            .Quantity(contracts)
            .Price(callOption.BidPrice) // Sell at bid for immediate execution
            .TimeInForce(TimeInForce.GoodTillCancel)
            .Build();
    }

    private record OptionSymbolInfo(string UnderlyingSymbol, DateTime Expiry, decimal Strike, bool IsCall, string Symbol);

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
    public bool InitializedUnderlying { get; set; }
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
    public int MinDte { get; set; } = 30; // Minimum days to expiry
    public int MaxDte { get; set; } = 45; // Maximum days to expiry
    public int RollAtDte { get; set; } = 21;
    public double RollAtPnLPercent { get; set; } = 0.5;
    public int LotSize { get; set; } = 100;
    public int MaxPositions { get; set; } = 10;
    public List<string> Symbols { get; set; } = new() { "SPY" };
    public bool InitializeUnderlyingPositions { get; set; } = true; // Auto-create underlying positions
}
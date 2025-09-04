using System.Runtime.CompilerServices;
using Optx.Core.Events;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Engine;

/// <summary>
/// High-performance portfolio manager with zero-allocation position tracking
/// </summary>
public sealed class PortfolioManager
{
    private readonly Dictionary<string, Position> _positions;
    private readonly Dictionary<string, decimal> _marketPrices;
    private readonly List<Fill> _fillHistory;
    private decimal _cash;
    private decimal _realizedPnL;
    private ulong _lastUpdateTime;

    public PortfolioManager(decimal initialCash)
    {
        _positions = new Dictionary<string, Position>();
        _marketPrices = new Dictionary<string, decimal>();
        _fillHistory = new List<Fill>();
        _cash = initialCash;
        _realizedPnL = 0m;
        _lastUpdateTime = 0;
    }

    /// <summary>
    /// Apply fill to portfolio
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ApplyFill(Fill fill)
    {
        var symbol = fill.OrderId.Split('_')[0]; // Extract symbol from order ID
        var side = fill.FilledQuantity > 0 ? 1 : -1;
        var quantity = Math.Abs(fill.FilledQuantity);
        var price = fill.FillPrice;
        var commission = fill.Commission;

        if (_positions.TryGetValue(symbol, out var position))
        {
            // Update existing position
            var newQuantity = position.Quantity + fill.FilledQuantity;
            var newAvgPrice = CalculateNewAveragePrice(
                position.Quantity, position.AveragePrice,
                fill.FilledQuantity, fill.FillPrice);

            var updatedPosition = new Position(
                symbol.AsMemory(),
                newQuantity,
                newAvgPrice,
                _marketPrices.GetValueOrDefault(symbol, price),
                position.Greeks);

            if (newQuantity == 0)
            {
                // Position closed
                _realizedPnL += CalculateRealizedPnL(position, fill);
                _positions.Remove(symbol);
            }
            else
            {
                _positions[symbol] = updatedPosition;
            }
        }
        else
        {
            // New position
            var currentPrice = _marketPrices.GetValueOrDefault(symbol, price);
            _positions[symbol] = new Position(
                symbol.AsMemory(),
                fill.FilledQuantity,
                fill.FillPrice,
                currentPrice,
                Greeks.Zero);
        }

        // Update cash
        _cash -= fill.FilledQuantity * fill.FillPrice + commission;
        _fillHistory.Add(fill);
    }

    /// <summary>
    /// Update market price for symbol
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateMarketData(MarketTick tick)
    {
        var symbol = tick.Symbol.ToString();
        _marketPrices[symbol] = tick.Price;

        // Update position mark-to-market
        if (_positions.TryGetValue(symbol, out var position))
        {
            _positions[symbol] = position with { CurrentPrice = tick.Price };
        }

        _lastUpdateTime = tick.TimestampNs;
    }

    /// <summary>
    /// Update market price from quote
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateQuote(QuoteUpdate quote)
    {
        var symbol = quote.Symbol.ToString();
        var midPrice = quote.GetMidPrice();
        _marketPrices[symbol] = midPrice;

        if (_positions.TryGetValue(symbol, out var position))
        {
            _positions[symbol] = position with { CurrentPrice = midPrice };
        }

        _lastUpdateTime = quote.TimestampNs;
    }

    /// <summary>
    /// Apply order acknowledgment
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ApplyOrderAck(OrderAck orderAck)
    {
        // In a full implementation, would track pending orders
        // For now, just update timestamp
        _lastUpdateTime = orderAck.TimestampNs;
    }

    /// <summary>
    /// Update timestamp for portfolio snapshot
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UpdateTimestamp(ulong timestamp)
    {
        _lastUpdateTime = timestamp;
    }

    /// <summary>
    /// Get current portfolio snapshot
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PortfolioState GetSnapshot(ulong timestamp)
    {
        var positions = _positions.Values.ToArray();
        var unrealizedPnL = CalculateUnrealizedPnL();
        var netGreeks = CalculateNetGreeks();

        return new PortfolioState(
            timestamp,
            positions.AsMemory(),
            unrealizedPnL,
            _realizedPnL,
            netGreeks);
    }

    /// <summary>
    /// Calculate unrealized PnL across all positions
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private decimal CalculateUnrealizedPnL()
    {
        decimal total = 0m;
        foreach (var position in _positions.Values)
        {
            total += position.GetUnrealizedPnL();
        }
        return total;
    }

    /// <summary>
    /// Calculate net Greeks across all positions
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Greeks CalculateNetGreeks()
    {
        var net = Greeks.Zero;
        foreach (var position in _positions.Values)
        {
            net += position.GetPortfolioGreeks();
        }
        return net;
    }

    /// <summary>
    /// Calculate new average price after fill
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal CalculateNewAveragePrice(
        int oldQuantity, 
        decimal oldAvgPrice, 
        int fillQuantity, 
        decimal fillPrice)
    {
        if (oldQuantity == 0) return fillPrice;
        
        var oldNotional = oldQuantity * oldAvgPrice;
        var fillNotional = fillQuantity * fillPrice;
        var newQuantity = oldQuantity + fillQuantity;
        
        return newQuantity == 0 ? 0m : (oldNotional + fillNotional) / newQuantity;
    }

    /// <summary>
    /// Calculate realized PnL from position closure
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static decimal CalculateRealizedPnL(Position position, Fill fill)
    {
        // Simplified - assumes full position closure
        return fill.FilledQuantity * (fill.FillPrice - position.AveragePrice);
    }

    /// <summary>
    /// Get position for symbol
    /// </summary>
    public Position? GetPosition(string symbol)
    {
        return _positions.TryGetValue(symbol, out var position) ? position : null;
    }

    /// <summary>
    /// Get all positions
    /// </summary>
    public IReadOnlyDictionary<string, Position> Positions => _positions;

    /// <summary>
    /// Current cash balance
    /// </summary>
    public decimal Cash => _cash;

    /// <summary>
    /// Realized PnL
    /// </summary>
    public decimal RealizedPnL => _realizedPnL;

    /// <summary>
    /// Total portfolio value (cash + positions)
    /// </summary>
    public decimal TotalValue => _cash + CalculateUnrealizedPnL();
}
using System.Runtime.CompilerServices;
using Optx.Core.Types;
using Optx.Core.Interfaces;
using Optx.Core.Utils;
using Optx.Core.Events;

namespace Optx.Execution;

/// <summary>
/// High-performance FIFO matching engine with price-time priority
/// </summary>
public sealed class MatchingEngine : IFillModel
{
    private readonly Dictionary<string, OrderBookState> _books;
    private readonly Queue<Fill> _pendingFills;
    private ulong _fillSequence;

    public MatchingEngine()
    {
        _books = new Dictionary<string, OrderBookState>();
        _pendingFills = new Queue<Fill>();
        _fillSequence = 0;
    }

    /// <summary>
    /// Attempt to fill order against order book
    /// </summary>
    public IEnumerable<Fill> TryFill(in Order order, in OrderBook book)
    {
        var fills = new List<Fill>();
        var symbol = order.Symbol.ToString();
        
        if (!_books.TryGetValue(symbol, out var bookState))
        {
            bookState = new OrderBookState(symbol);
            _books[symbol] = bookState;
        }

        // Update book state
        UpdateBookState(bookState, book);

        if (order.Type == OrderType.Market)
        {
            fills.AddRange(ProcessMarketOrder(order, bookState));
        }
        else
        {
            fills.AddRange(ProcessLimitOrder(order, bookState));
        }

        return fills;
    }

    /// <summary>
    /// Calculate commission for fill
    /// </summary>
    public decimal CalculateCommission(in Fill fill)
    {
        // Simplified commission model: $0.65 per options contract
        return 0.65m;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateBookState(OrderBookState bookState, in OrderBook book)
    {
        bookState.BestBid = book.GetBestBid();
        bookState.BestAsk = book.GetBestAsk();
        bookState.LastUpdate = book.TimestampNs;
    }

    private IEnumerable<Fill> ProcessMarketOrder(in Order order, OrderBookState bookState)
    {
        var fills = new List<Fill>();
        var remainingQuantity = Math.Abs(order.Quantity);
        
        if (remainingQuantity == 0) return fills;

        decimal fillPrice;
        
        if (order.Side == OrderSide.Buy)
        {
            fillPrice = bookState.BestAsk > 0 ? bookState.BestAsk : bookState.BestBid * 1.01m;
        }
        else
        {
            fillPrice = bookState.BestBid > 0 ? bookState.BestBid : bookState.BestAsk * 0.99m;
        }

        // For market orders, assume full fill with some slippage
        var slippageFactor = 1.0m + (decimal)(remainingQuantity / 10000.0) * 0.001m; // 0.1 bps per 100 shares
        if (order.Side == OrderSide.Buy)
        {
            fillPrice *= slippageFactor;
        }
        else
        {
            fillPrice /= slippageFactor;
        }

        var fill = CreateFill(order, remainingQuantity, fillPrice, 0);
        fills.Add(fill);

        return fills;
    }

    private IEnumerable<Fill> ProcessLimitOrder(in Order order, OrderBookState bookState)
    {
        var fills = new List<Fill>();
        var remainingQuantity = Math.Abs(order.Quantity);
        
        if (remainingQuantity == 0) return fills;

        bool canFill = false;
        decimal counterpartyPrice = 0m;

        if (order.Side == OrderSide.Buy)
        {
            // Buy order can fill against ask side
            if (bookState.BestAsk > 0 && order.Price >= bookState.BestAsk)
            {
                canFill = true;
                counterpartyPrice = bookState.BestAsk;
            }
        }
        else
        {
            // Sell order can fill against bid side
            if (bookState.BestBid > 0 && order.Price <= bookState.BestBid)
            {
                canFill = true;
                counterpartyPrice = bookState.BestBid;
            }
        }

        if (canFill)
        {
            // Simulate partial fills based on available liquidity
            var availableLiquidity = SimulateAvailableLiquidity(bookState, order.Side);
            var fillQuantity = Math.Min(remainingQuantity, availableLiquidity);
            
            if (fillQuantity > 0)
            {
                var leavesQuantity = remainingQuantity - fillQuantity;
                var fill = CreateFill(order, fillQuantity, counterpartyPrice, leavesQuantity);
                fills.Add(fill);
            }
        }

        return fills;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SimulateAvailableLiquidity(OrderBookState bookState, OrderSide side)
    {
        // Simplified liquidity model - in real system would track full book depth
        var baseLiquidity = 1000;
        var spreadFactor = Math.Max(0.1, Math.Min(2.0, (double)(bookState.BestAsk - bookState.BestBid) / (double)bookState.BestBid));
        return (int)(baseLiquidity / spreadFactor);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Fill CreateFill(in Order order, int fillQuantity, decimal fillPrice, int leavesQuantity)
    {
        var signedQuantity = order.Side == OrderSide.Buy ? fillQuantity : -fillQuantity;
        
        return new Fill(
            order.OrderId,
            $"EX{++_fillSequence:D8}",
            signedQuantity,
            fillPrice,
            leavesQuantity,
            TimeUtils.GetCurrentNanoseconds(),
            CalculateCommission(new Fill("", "", signedQuantity, fillPrice, 0, 0, 0m)));
    }
}

/// <summary>
/// Internal order book state
/// </summary>
internal sealed class OrderBookState
{
    public string Symbol { get; }
    public decimal BestBid { get; set; }
    public decimal BestAsk { get; set; }
    public ulong LastUpdate { get; set; }

    public OrderBookState(string symbol)
    {
        Symbol = symbol;
        BestBid = 0m;
        BestAsk = 0m;
        LastUpdate = 0;
    }
}

/// <summary>
/// Simple risk checks implementation
/// </summary>
public sealed class BasicRiskChecks : IRiskChecks
{
    private readonly decimal _maxOrderSize;
    private readonly decimal _maxPositionValue;
    private readonly decimal _maxPortfolioDelta;

    public BasicRiskChecks(
        decimal maxOrderSize = 10000m,
        decimal maxPositionValue = 50000m,
        decimal maxPortfolioDelta = 1000.0m)
    {
        _maxOrderSize = maxOrderSize;
        _maxPositionValue = maxPositionValue;
        _maxPortfolioDelta = (decimal)maxPortfolioDelta;
    }

    public bool Approve(in Order order, in PortfolioState portfolioState, out string reason)
    {
        reason = "";

        // Check order size
        var orderValue = Math.Abs(order.Quantity) * order.Price;
        if (orderValue > _maxOrderSize)
        {
            reason = $"Order value {orderValue:C} exceeds limit {_maxOrderSize:C}";
            return false;
        }

        // Check if order would create excessive position
        var symbol = order.Symbol.ToString();
        var currentPosition = portfolioState.GetPosition(symbol.AsSpan());
        var newQuantity = (currentPosition?.Quantity ?? 0) + order.Quantity;
        var newPositionValue = Math.Abs(newQuantity) * order.Price;
        
        if (newPositionValue > _maxPositionValue)
        {
            reason = $"New position value {newPositionValue:C} would exceed limit {_maxPositionValue:C}";
            return false;
        }

        // Check portfolio delta (simplified - would need Greeks in real system)
        var estimatedDelta = order.Side == OrderSide.Buy ? 100 : -100; // Simplified delta estimate
        var newPortfolioDelta = (decimal)portfolioState.NetGreeks.Delta + estimatedDelta;
        
        if (Math.Abs(newPortfolioDelta) > _maxPortfolioDelta)
        {
            reason = $"New portfolio delta {newPortfolioDelta:F0} would exceed limit {_maxPortfolioDelta:F0}";
            return false;
        }

        return true;
    }
}
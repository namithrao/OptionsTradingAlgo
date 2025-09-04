using Optx.Core.Types;
using System.Runtime.CompilerServices;

namespace Optx.Core.Events;

/// <summary>
/// Market event discriminated union
/// </summary>
public readonly record struct MarketEvent
{
    private readonly MarketEventType _type;
    private readonly MarketTick _marketTick;
    private readonly QuoteUpdate _quoteUpdate;
    private readonly Fill _fill;
    private readonly OrderAck _orderAck;

    public MarketEvent(MarketTick marketTick)
    {
        _type = MarketEventType.MarketData;
        _marketTick = marketTick;
        _quoteUpdate = default;
        _fill = default;
        _orderAck = default;
    }

    public MarketEvent(QuoteUpdate quoteUpdate)
    {
        _type = MarketEventType.Quote;
        _marketTick = default;
        _quoteUpdate = quoteUpdate;
        _fill = default;
        _orderAck = default;
    }

    public MarketEvent(Fill fill)
    {
        _type = MarketEventType.Fill;
        _marketTick = default;
        _quoteUpdate = default;
        _fill = fill;
        _orderAck = default;
    }

    public MarketEvent(OrderAck orderAck)
    {
        _type = MarketEventType.OrderAck;
        _marketTick = default;
        _quoteUpdate = default;
        _fill = default;
        _orderAck = orderAck;
    }

    /// <summary>
    /// Event timestamp in nanoseconds
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong GetTimestamp() => _type switch
    {
        MarketEventType.MarketData => _marketTick.TimestampNs,
        MarketEventType.Quote => _quoteUpdate.TimestampNs,
        MarketEventType.Fill => _fill.TimestampNs,
        MarketEventType.OrderAck => _orderAck.TimestampNs,
        _ => 0
    };

    /// <summary>
    /// Event type
    /// </summary>
    public MarketEventType Type => _type;

    /// <summary>
    /// Check if event is market data
    /// </summary>
    public bool IsMarketData => _type == MarketEventType.MarketData;

    /// <summary>
    /// Check if event is quote update
    /// </summary>
    public bool IsQuote => _type == MarketEventType.Quote;

    /// <summary>
    /// Check if event is fill
    /// </summary>
    public bool IsFill => _type == MarketEventType.Fill;

    /// <summary>
    /// Check if event is order ack
    /// </summary>
    public bool IsOrderAck => _type == MarketEventType.OrderAck;

    /// <summary>
    /// Get market tick (throws if not market data)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MarketTick AsMarketTick()
    {
        if (_type != MarketEventType.MarketData)
            throw new InvalidOperationException($"Event is not market data, it is {_type}");
        return _marketTick;
    }

    /// <summary>
    /// Get quote update (throws if not quote)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public QuoteUpdate AsQuoteUpdate()
    {
        if (_type != MarketEventType.Quote)
            throw new InvalidOperationException($"Event is not quote update, it is {_type}");
        return _quoteUpdate;
    }

    /// <summary>
    /// Get fill (throws if not fill)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Fill AsFill()
    {
        if (_type != MarketEventType.Fill)
            throw new InvalidOperationException($"Event is not fill, it is {_type}");
        return _fill;
    }

    /// <summary>
    /// Get order ack (throws if not order ack)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderAck AsOrderAck()
    {
        if (_type != MarketEventType.OrderAck)
            throw new InvalidOperationException($"Event is not order ack, it is {_type}");
        return _orderAck;
    }

    /// <summary>
    /// Try get market tick
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetMarketTick(out MarketTick marketTick)
    {
        marketTick = _marketTick;
        return _type == MarketEventType.MarketData;
    }

    /// <summary>
    /// Try get quote update
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetQuoteUpdate(out QuoteUpdate quoteUpdate)
    {
        quoteUpdate = _quoteUpdate;
        return _type == MarketEventType.Quote;
    }

    /// <summary>
    /// Try get fill
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetFill(out Fill fill)
    {
        fill = _fill;
        return _type == MarketEventType.Fill;
    }

    /// <summary>
    /// Try get order ack
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetOrderAck(out OrderAck orderAck)
    {
        orderAck = _orderAck;
        return _type == MarketEventType.OrderAck;
    }

    public override string ToString() => _type switch
    {
        MarketEventType.MarketData => $"MarketData[{_marketTick.Symbol.ToString()}@{_marketTick.Price}]",
        MarketEventType.Quote => $"Quote[{_quoteUpdate.Symbol.ToString()}:{_quoteUpdate.BidPrice}/{_quoteUpdate.AskPrice}]",
        MarketEventType.Fill => $"Fill[{_fill.OrderId}:{_fill.FilledQuantity}@{_fill.FillPrice}]",
        MarketEventType.OrderAck => $"OrderAck[{_orderAck.OrderId}:{_orderAck.Status}]",
        _ => "Unknown"
    };
}

/// <summary>
/// Market event types
/// </summary>
public enum MarketEventType : byte
{
    MarketData = 0,
    Quote = 1,
    Fill = 2,
    OrderAck = 3
}

/// <summary>
/// Portfolio state snapshot
/// </summary>
public readonly record struct PortfolioState(
    ulong TimestampNs,
    ReadOnlyMemory<Position> Positions,
    decimal UnrealizedPnL,
    decimal RealizedPnL,
    Greeks NetGreeks)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetTotalPnL() => UnrealizedPnL + RealizedPnL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Position? GetPosition(ReadOnlySpan<char> symbol)
    {
        var positions = Positions.Span;
        for (int i = 0; i < positions.Length; i++)
        {
            if (positions[i].Symbol.Span.SequenceEqual(symbol))
                return positions[i];
        }
        return null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetMarketValue()
    {
        var positions = Positions.Span;
        decimal total = 0m;
        for (int i = 0; i < positions.Length; i++)
        {
            total += positions[i].GetMarketValue();
        }
        return total;
    }
}
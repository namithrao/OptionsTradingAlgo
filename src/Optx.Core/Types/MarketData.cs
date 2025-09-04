using System.Runtime.CompilerServices;

namespace Optx.Core.Types;

/// <summary>
/// Zero-allocation market data tick representation
/// </summary>
public readonly record struct MarketTick(
    ulong TimestampNs,
    ReadOnlyMemory<char> Symbol,
    decimal Price,
    int Quantity,
    MarketDataType Type)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetPriceScaled() => (long)(Price * 10000m);
}

/// <summary>
/// Market data event types
/// </summary>
public enum MarketDataType : byte
{
    Trade = 0,
    Bid = 1,
    Ask = 2,
    Quote = 3
}

/// <summary>
/// Quote update with bid/ask levels
/// </summary>
public readonly record struct QuoteUpdate(
    ulong TimestampNs,
    ReadOnlyMemory<char> Symbol,
    decimal BidPrice,
    int BidSize,
    decimal AskPrice,
    int AskSize)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetSpread() => AskPrice - BidPrice;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetMidPrice() => (BidPrice + AskPrice) * 0.5m;
}

/// <summary>
/// Order book level
/// </summary>
public readonly record struct BookLevel(
    decimal Price,
    int Size)
{
    public bool IsEmpty => Size == 0;
}

/// <summary>
/// Order book snapshot
/// </summary>
public readonly record struct OrderBook(
    ReadOnlyMemory<char> Symbol,
    ulong TimestampNs,
    ReadOnlyMemory<BookLevel> Bids,
    ReadOnlyMemory<BookLevel> Asks)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetBestBid() => Bids.Length > 0 ? Bids.Span[0].Price : 0m;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetBestAsk() => Asks.Length > 0 ? Asks.Span[0].Price : decimal.MaxValue;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetMidPrice()
    {
        var bid = GetBestBid();
        var ask = GetBestAsk();
        return bid > 0 && ask < decimal.MaxValue ? (bid + ask) * 0.5m : 0m;
    }
}
using System.Runtime.CompilerServices;

namespace Optx.Core.Types;

/// <summary>
/// Zero-allocation order representation
/// </summary>
public readonly record struct Order(
    string OrderId,
    ReadOnlyMemory<char> Symbol,
    OrderSide Side,
    OrderType Type,
    int Quantity,
    decimal Price,
    TimeInForce TimeInForce,
    ulong TimestampNs)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetPriceScaled() => (long)(Price * 10000m);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetNotional() => Math.Abs(Quantity) * Price;
}

/// <summary>
/// Order side enumeration
/// </summary>
public enum OrderSide : byte
{
    Buy = 0,
    Sell = 1
}

/// <summary>
/// Order type enumeration
/// </summary>
public enum OrderType : byte
{
    Market = 0,
    Limit = 1
}

/// <summary>
/// Time in force options
/// </summary>
public enum TimeInForce : byte
{
    GoodTillCancel = 0,
    ImmediateOrCancel = 1,
    FillOrKill = 2
}

/// <summary>
/// Order status enumeration
/// </summary>
public enum OrderStatus : byte
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Filled = 3,
    PartiallyFilled = 4,
    Canceled = 5
}

/// <summary>
/// Order acknowledgment
/// </summary>
public readonly record struct OrderAck(
    string OrderId,
    string ExchangeOrderId,
    OrderStatus Status,
    ulong TimestampNs,
    string? Reason = null);

/// <summary>
/// Fill execution
/// </summary>
public readonly record struct Fill(
    string OrderId,
    string ExchangeOrderId,
    int FilledQuantity,
    decimal FillPrice,
    int LeavesQuantity,
    ulong TimestampNs,
    decimal Commission = 0m)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetNotional() => Math.Abs(FilledQuantity) * FillPrice;
}

/// <summary>
/// Order cancel request
/// </summary>
public readonly record struct CancelOrder(
    string OrderId,
    string ExchangeOrderId,
    ulong TimestampNs);

/// <summary>
/// Order builder for efficient construction
/// </summary>
public ref struct OrderBuilder
{
    private string _orderId;
    private ReadOnlyMemory<char> _symbol;
    private OrderSide _side;
    private OrderType _type;
    private int _quantity;
    private decimal _price;
    private TimeInForce _tif;
    private ulong _timestampNs;

    public OrderBuilder(string orderId)
    {
        _orderId = orderId;
        _symbol = default;
        _side = OrderSide.Buy;
        _type = OrderType.Market;
        _quantity = 0;
        _price = 0m;
        _tif = Types.TimeInForce.GoodTillCancel;
        _timestampNs = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder Symbol(ReadOnlyMemory<char> symbol)
    {
        _symbol = symbol;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder Side(OrderSide side)
    {
        _side = side;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder Type(OrderType type)
    {
        _type = type;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder Quantity(int quantity)
    {
        _quantity = quantity;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder Price(decimal price)
    {
        _price = price;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder TimeInForce(TimeInForce tif)
    {
        _tif = tif;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OrderBuilder TimestampNs(ulong timestampNs)
    {
        _timestampNs = timestampNs;
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly Order Build()
    {
        return new Order(_orderId, _symbol, _side, _type, _quantity, _price, _tif, _timestampNs);
    }
}
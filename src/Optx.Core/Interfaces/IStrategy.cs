using Optx.Core.Types;
using Optx.Core.Events;

namespace Optx.Core.Interfaces;

/// <summary>
/// Strategy interface for processing market events
/// </summary>
public interface IStrategy
{
    /// <summary>
    /// Strategy name for identification
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Process a market event and return orders to execute
    /// </summary>
    /// <param name="marketEvent">The market event</param>
    /// <param name="portfolioState">Current portfolio state</param>
    /// <returns>Orders to execute</returns>
    IEnumerable<Order> OnEvent(in MarketEvent marketEvent, in PortfolioState portfolioState);

    /// <summary>
    /// Called when an order is filled
    /// </summary>
    /// <param name="fill">The fill execution</param>
    /// <param name="portfolioState">Updated portfolio state</param>
    void OnFill(in Fill fill, in PortfolioState portfolioState);

    /// <summary>
    /// Called when an order is acknowledged
    /// </summary>
    /// <param name="orderAck">Order acknowledgment</param>
    void OnOrderAck(in OrderAck orderAck);

    /// <summary>
    /// Get current strategy state for reporting
    /// </summary>
    /// <returns>Strategy state dictionary</returns>
    IReadOnlyDictionary<string, object> GetState();
}

/// <summary>
/// Fill model interface for simulating order executions
/// </summary>
public interface IFillModel
{
    /// <summary>
    /// Attempt to fill an order against an order book
    /// </summary>
    /// <param name="order">Order to fill</param>
    /// <param name="book">Current order book</param>
    /// <returns>Fill executions</returns>
    IEnumerable<Fill> TryFill(in Order order, in OrderBook book);

    /// <summary>
    /// Calculate commission for a fill
    /// </summary>
    /// <param name="fill">Fill execution</param>
    /// <returns>Commission amount</returns>
    decimal CalculateCommission(in Fill fill);
}

/// <summary>
/// Risk check interface for order validation
/// </summary>
public interface IRiskChecks
{
    /// <summary>
    /// Validate an order against risk limits
    /// </summary>
    /// <param name="order">Order to validate</param>
    /// <param name="portfolioState">Current portfolio state</param>
    /// <param name="reason">Rejection reason if not approved</param>
    /// <returns>True if order is approved</returns>
    bool Approve(in Order order, in PortfolioState portfolioState, out string reason);
}

/// <summary>
/// Market data provider interface
/// </summary>
public interface IMarketDataProvider
{
    /// <summary>
    /// Subscribe to market data for a symbol
    /// </summary>
    /// <param name="symbol">Symbol to subscribe to</param>
    void Subscribe(string symbol);

    /// <summary>
    /// Unsubscribe from market data for a symbol
    /// </summary>
    /// <param name="symbol">Symbol to unsubscribe from</param>
    void Unsubscribe(string symbol);

    /// <summary>
    /// Get current order book for a symbol
    /// </summary>
    /// <param name="symbol">Symbol to get book for</param>
    /// <returns>Order book or null if not available</returns>
    OrderBook? GetOrderBook(string symbol);

    /// <summary>
    /// Market data event stream
    /// </summary>
    IObservable<MarketEvent> MarketEvents { get; }
}

/// <summary>
/// Order gateway interface
/// </summary>
public interface IOrderGateway
{
    /// <summary>
    /// Submit an order
    /// </summary>
    /// <param name="order">Order to submit</param>
    /// <returns>Task completing when order is acknowledged</returns>
    Task<OrderAck> SubmitOrderAsync(Order order);

    /// <summary>
    /// Cancel an order
    /// </summary>
    /// <param name="cancel">Cancel request</param>
    /// <returns>Task completing when cancel is acknowledged</returns>
    Task<OrderAck> CancelOrderAsync(CancelOrder cancel);

    /// <summary>
    /// Order acknowledgment stream
    /// </summary>
    IObservable<OrderAck> OrderAcks { get; }

    /// <summary>
    /// Fill execution stream
    /// </summary>
    IObservable<Fill> Fills { get; }
}
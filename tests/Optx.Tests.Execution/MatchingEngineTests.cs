using Optx.Core.Types;
using Optx.Core.Utils;
using Optx.Execution;
using Optx.Core.Events;
using Xunit;

namespace Optx.Tests.Execution;

public class MatchingEngineTests
{
    private readonly MatchingEngine _engine;

    public MatchingEngineTests()
    {
        _engine = new MatchingEngine();
    }

    [Fact]
    public void TryFill_MarketBuyOrder_ShouldFillAtAskPrice()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateMarketOrder(OrderSide.Buy, 100);

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Single(fills);
        var fill = fills[0];
        Assert.Equal(100, fill.FilledQuantity);
        Assert.True(fill.FillPrice >= 100.50m, $"Fill price {fill.FillPrice} should be >= ask price 100.50");
        Assert.Equal(0, fill.LeavesQuantity);
    }

    [Fact]
    public void TryFill_MarketSellOrder_ShouldFillAtBidPrice()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateMarketOrder(OrderSide.Sell, 100);

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Single(fills);
        var fill = fills[0];
        Assert.Equal(-100, fill.FilledQuantity);
        Assert.True(fill.FillPrice <= 99.50m, $"Fill price {fill.FillPrice} should be <= bid price 99.50");
        Assert.Equal(0, fill.LeavesQuantity);
    }

    [Fact]
    public void TryFill_LimitBuyOrder_AboveAsk_ShouldFill()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateLimitOrder(OrderSide.Buy, 100, 101.00m); // Buy at 101, ask is 100.50

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Single(fills);
        var fill = fills[0];
        Assert.Equal(100, fill.FilledQuantity);
        Assert.Equal(100.50m, fill.FillPrice); // Should fill at ask price
    }

    [Fact]
    public void TryFill_LimitBuyOrder_BelowAsk_ShouldNotFill()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateLimitOrder(OrderSide.Buy, 100, 100.00m); // Buy at 100, ask is 100.50

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Empty(fills); // Should not fill
    }

    [Fact]
    public void TryFill_LimitSellOrder_BelowBid_ShouldFill()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateLimitOrder(OrderSide.Sell, 100, 99.00m); // Sell at 99, bid is 99.50

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Single(fills);
        var fill = fills[0];
        Assert.Equal(-100, fill.FilledQuantity);
        Assert.Equal(99.50m, fill.FillPrice); // Should fill at bid price
    }

    [Fact]
    public void TryFill_LimitSellOrder_AboveBid_ShouldNotFill()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateLimitOrder(OrderSide.Sell, 100, 100.00m); // Sell at 100, bid is 99.50

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Empty(fills); // Should not fill
    }

    [Fact]
    public void CalculateCommission_ShouldReturnFixedAmount()
    {
        var fill = new Fill("TEST", "EX001", 100, 100m, 0, 0, 0m);
        var commission = _engine.CalculateCommission(in fill);

        Assert.Equal(0.65m, commission);
    }

    [Fact]
    public void TryFill_ZeroQuantityOrder_ShouldNotFill()
    {
        var book = CreateTestOrderBook(99.50m, 100.50m);
        var order = CreateMarketOrder(OrderSide.Buy, 0);

        var fills = _engine.TryFill(in order, in book).ToList();

        Assert.Empty(fills);
    }

    [Fact]
    public void BasicRiskChecks_OrderTooLarge_ShouldReject()
    {
        var riskChecks = new BasicRiskChecks(maxOrderSize: 1000m);
        var portfolioState = CreateEmptyPortfolio();
        
        var largeOrder = CreateMarketOrder(OrderSide.Buy, 100, 20m); // 100 * 20 = 2000 > 1000 limit

        var approved = riskChecks.Approve(in largeOrder, in portfolioState, out string reason);

        Assert.False(approved);
        Assert.Contains("Order value", reason);
        Assert.Contains("exceeds limit", reason);
    }

    [Fact]
    public void BasicRiskChecks_NormalOrder_ShouldApprove()
    {
        var riskChecks = new BasicRiskChecks(maxOrderSize: 10000m);
        var portfolioState = CreateEmptyPortfolio();
        
        var normalOrder = CreateMarketOrder(OrderSide.Buy, 100, 50m); // 100 * 50 = 5000 < 10000 limit

        var approved = riskChecks.Approve(in normalOrder, in portfolioState, out string reason);

        Assert.True(approved);
        Assert.Empty(reason);
    }

    private static OrderBook CreateTestOrderBook(decimal bidPrice, decimal askPrice)
    {
        var bidLevel = new BookLevel(bidPrice, 1000);
        var askLevel = new BookLevel(askPrice, 1000);
        
        return new OrderBook(
            "SPY".AsMemory(),
            TimeUtils.GetCurrentNanoseconds(),
            new BookLevel[] { bidLevel },
            new BookLevel[] { askLevel });
    }

    private static Order CreateMarketOrder(OrderSide side, int quantity, decimal price = 100m)
    {
        return new OrderBuilder($"TEST_{Random.Shared.Next(1000, 9999)}")
            .Symbol("SPY".AsMemory())
            .Side(side)
            .Type(OrderType.Market)
            .Quantity(quantity)
            .Price(price)
            .TimestampNs(TimeUtils.GetCurrentNanoseconds())
            .Build();
    }

    private static Order CreateLimitOrder(OrderSide side, int quantity, decimal price)
    {
        return new OrderBuilder($"TEST_{Random.Shared.Next(1000, 9999)}")
            .Symbol("SPY".AsMemory())
            .Side(side)
            .Type(OrderType.Limit)
            .Quantity(quantity)
            .Price(price)
            .TimestampNs(TimeUtils.GetCurrentNanoseconds())
            .Build();
    }

    private static PortfolioState CreateEmptyPortfolio()
    {
        return new PortfolioState(
            TimeUtils.GetCurrentNanoseconds(),
            ReadOnlyMemory<Position>.Empty,
            0m,
            0m,
            Greeks.Zero);
    }
}
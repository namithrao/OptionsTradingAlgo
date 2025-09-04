using Optx.Core.Events;
using Optx.Core.Interfaces;
using Optx.Core.Types;
using Optx.Core.Utils;
using Optx.Engine;
using Optx.Execution;
using Xunit;

namespace Optx.Tests.Engine;

public class BacktestEngineTests
{
    [Fact]
    public void BacktestEngine_WithNoEvents_ShouldCompleteSuccessfully()
    {
        var strategy = new TestStrategy();
        var fillModel = new MatchingEngine();
        var riskChecks = new BasicRiskChecks();
        var config = new BacktestConfig { InitialCash = 10000m };

        var engine = new BacktestEngine(strategy, fillModel, riskChecks, config);

        // Should complete without any events
        var task = engine.RunAsync();
        Assert.True(task.Wait(1000), "Engine should complete within 1 second");

        var results = task.Result;
        Assert.NotNull(results);
        Assert.Equal(0, results.EventsProcessed);
    }

    [Fact]
    public void BacktestEngine_WithMarketEvents_ShouldProcessInOrder()
    {
        var strategy = new TestStrategy();
        var fillModel = new MatchingEngine();
        var riskChecks = new BasicRiskChecks();
        var config = new BacktestConfig { InitialCash = 10000m };

        var engine = new BacktestEngine(strategy, fillModel, riskChecks, config);

        // Add events out of chronological order
        var tick2 = new MarketTick(2000, "SPY".AsMemory(), 102m, 100, MarketDataType.Trade);
        var tick1 = new MarketTick(1000, "SPY".AsMemory(), 101m, 100, MarketDataType.Trade);
        var tick3 = new MarketTick(3000, "SPY".AsMemory(), 103m, 100, MarketDataType.Trade);

        engine.AddEvent(new MarketEvent(tick2));
        engine.AddEvent(new MarketEvent(tick1));
        engine.AddEvent(new MarketEvent(tick3));

        var task = engine.RunAsync();
        Assert.True(task.Wait(5000), "Engine should complete within 5 seconds");

        var results = task.Result;
        Assert.Equal(3, results.EventsProcessed);

        // Verify strategy received events in chronological order
        var testStrategy = (TestStrategy)strategy;
        Assert.Equal(3, testStrategy.EventsReceived.Count);
        Assert.Equal(1000UL, testStrategy.EventsReceived[0].GetTimestamp());
        Assert.Equal(2000UL, testStrategy.EventsReceived[1].GetTimestamp());
        Assert.Equal(3000UL, testStrategy.EventsReceived[2].GetTimestamp());
    }

    [Fact]
    public void BacktestEngine_Determinism_ShouldProduceSameResults()
    {
        // Run the same backtest twice and verify identical results
        var results1 = RunDeterministicBacktest();
        var results2 = RunDeterministicBacktest();

        Assert.Equal(results1.EventsProcessed, results2.EventsProcessed);
        Assert.Equal(results1.FinalPortfolioState.RealizedPnL, results2.FinalPortfolioState.RealizedPnL);
        Assert.Equal(results1.FinalPortfolioState.UnrealizedPnL, results2.FinalPortfolioState.UnrealizedPnL);
    }

    private static BacktestResults RunDeterministicBacktest()
    {
        var strategy = new TestStrategy();
        var fillModel = new MatchingEngine();
        var riskChecks = new BasicRiskChecks();
        var config = new BacktestConfig { InitialCash = 10000m };

        var engine = new BacktestEngine(strategy, fillModel, riskChecks, config);

        // Add deterministic set of events
        for (int i = 0; i < 100; i++)
        {
            var price = 100m + (decimal)(Math.Sin(i * 0.1) * 5);
            var tick = new MarketTick((ulong)(i * 1000), "SPY".AsMemory(), price, 100, MarketDataType.Trade);
            engine.AddEvent(new MarketEvent(tick));
        }

        return engine.RunAsync().Result;
    }

    [Fact]
    public void BacktestEngine_WithOrders_ShouldProcessFills()
    {
        var strategy = new OrderGeneratingStrategy();
        var fillModel = new MatchingEngine();
        var riskChecks = new BasicRiskChecks();
        var config = new BacktestConfig { InitialCash = 100000m };

        var engine = new BacktestEngine(strategy, fillModel, riskChecks, config);

        // Add market data that will trigger orders
        var tick = new MarketTick(1000, "SPY".AsMemory(), 100m, 1000, MarketDataType.Trade);
        engine.AddEvent(new MarketEvent(tick));

        var task = engine.RunAsync();
        Assert.True(task.Wait(5000), "Engine should complete within 5 seconds");

        var results = task.Result;
        
        // Verify the strategy generated orders
        var testStrategy = (OrderGeneratingStrategy)strategy;
        Assert.True(testStrategy.OrdersGenerated > 0, "Strategy should have generated orders");
    }
}

// Test strategy implementations
internal class TestStrategy : IStrategy
{
    public string Name => "TestStrategy";
    public List<MarketEvent> EventsReceived { get; } = new();

    public IEnumerable<Order> OnEvent(in MarketEvent marketEvent, in PortfolioState portfolioState)
    {
        EventsReceived.Add(marketEvent);
        return Array.Empty<Order>();
    }

    public void OnFill(in Fill fill, in PortfolioState portfolioState) { }
    public void OnOrderAck(in OrderAck orderAck) { }
    public IReadOnlyDictionary<string, object> GetState() => new Dictionary<string, object>();
}

internal class OrderGeneratingStrategy : IStrategy
{
    public string Name => "OrderGeneratingStrategy";
    public int OrdersGenerated { get; private set; }

    public IEnumerable<Order> OnEvent(in MarketEvent marketEvent, in PortfolioState portfolioState)
    {
        if (marketEvent.IsMarketData && OrdersGenerated == 0)
        {
            OrdersGenerated++;
            var tick = marketEvent.AsMarketTick();
            
            return new[]
            {
                new OrderBuilder($"TEST_{TimeUtils.GetCurrentNanoseconds()}")
                    .Symbol(tick.Symbol)
                    .Side(OrderSide.Buy)
                    .Type(OrderType.Market)
                    .Quantity(100)
                    .Price(tick.Price)
                    .TimestampNs(tick.TimestampNs)
                    .Build()
            };
        }

        return Array.Empty<Order>();
    }

    public void OnFill(in Fill fill, in PortfolioState portfolioState) { }
    public void OnOrderAck(in OrderAck orderAck) { }
    public IReadOnlyDictionary<string, object> GetState() => new Dictionary<string, object>
    {
        ["OrdersGenerated"] = OrdersGenerated
    };
}
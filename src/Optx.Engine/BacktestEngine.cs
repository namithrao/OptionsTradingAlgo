using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Optx.Core.Events;
using Optx.Core.Interfaces;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Engine;

/// <summary>
/// Deterministic backtesting engine with precise event ordering
/// </summary>
public sealed class BacktestEngine
{
    private readonly IStrategy _strategy;
    private readonly IFillModel _fillModel;
    private readonly IRiskChecks _riskChecks;
    private readonly PortfolioManager _portfolio;
    private readonly Dictionary<string, OrderBook> _orderBooks;
    private readonly SortedList<ulong, List<MarketEvent>> _eventQueue;
    private readonly BacktestConfig _config;
    private readonly PerformanceMetrics _metrics;

    private ulong _currentTimestamp;
    private int _eventCount;
    private bool _isRunning;

    public BacktestEngine(
        IStrategy strategy,
        IFillModel fillModel,
        IRiskChecks riskChecks,
        BacktestConfig config)
    {
        _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        _fillModel = fillModel ?? throw new ArgumentNullException(nameof(fillModel));
        _riskChecks = riskChecks ?? throw new ArgumentNullException(nameof(riskChecks));
        _config = config ?? throw new ArgumentNullException(nameof(config));

        _portfolio = new PortfolioManager(config.InitialCash);
        _orderBooks = new Dictionary<string, OrderBook>();
        _eventQueue = new SortedList<ulong, List<MarketEvent>>();
        _metrics = new PerformanceMetrics();
        
        _currentTimestamp = 0;
        _eventCount = 0;
    }

    /// <summary>
    /// Add market event to the backtest
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddEvent(MarketEvent marketEvent)
    {
        var timestamp = marketEvent.GetTimestamp();
        
        if (!_eventQueue.TryGetValue(timestamp, out var events))
        {
            events = new List<MarketEvent>();
            _eventQueue[timestamp] = events;
        }
        
        events.Add(marketEvent);
    }

    /// <summary>
    /// Add batch of events efficiently
    /// </summary>
    public void AddEvents(IEnumerable<MarketEvent> events)
    {
        foreach (var marketEvent in events)
        {
            AddEvent(marketEvent);
        }
    }

    /// <summary>
    /// Run the backtest deterministically
    /// </summary>
    public async Task<BacktestResults> RunAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning)
            throw new InvalidOperationException("Backtest is already running");

        _isRunning = true;
        var startTime = DateTime.UtcNow;

        try
        {
            return await RunBacktestLoop(cancellationToken);
        }
        finally
        {
            _isRunning = false;
            _metrics.SetBacktestDuration(DateTime.UtcNow - startTime);
        }
    }

    private async Task<BacktestResults> RunBacktestLoop(CancellationToken cancellationToken)
    {
        var eventsProcessed = 0;
        var checkpointInterval = Math.Max(_config.CheckpointInterval, 1000);

        foreach (var (timestamp, events) in _eventQueue)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            _currentTimestamp = timestamp;
            
            // Process all events at this timestamp
            foreach (var marketEvent in events.OrderBy(e => GetEventPriority(e)))
            {
                await ProcessEvent(marketEvent);
                eventsProcessed++;
                
                if (eventsProcessed % checkpointInterval == 0)
                {
                    ReportProgress(eventsProcessed, _eventQueue.Sum(kvp => kvp.Value.Count));
                    
                    if (_config.EnableCheckpointing)
                    {
                        await CreateCheckpoint();
                    }
                }
            }

            // Update portfolio valuation
            _portfolio.UpdateTimestamp(_currentTimestamp);
        }

        return GenerateResults();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private async Task ProcessEvent(MarketEvent marketEvent)
    {
        var eventStartTime = GetHighPrecisionTimestamp();

        try
        {
            switch (marketEvent.Type)
            {
                case MarketEventType.MarketData:
                    ProcessMarketData(marketEvent.AsMarketTick());
                    break;
                
                case MarketEventType.Quote:
                    ProcessQuoteUpdate(marketEvent.AsQuoteUpdate());
                    break;
                
                case MarketEventType.Fill:
                    ProcessFill(marketEvent.AsFill());
                    break;
                
                case MarketEventType.OrderAck:
                    ProcessOrderAck(marketEvent.AsOrderAck());
                    break;
            }

            // Generate strategy signals
            var portfolioState = _portfolio.GetSnapshot(_currentTimestamp);
            var orders = _strategy.OnEvent(in marketEvent, in portfolioState);

            // Process orders
            await ProcessOrders(orders);
        }
        finally
        {
            var eventDuration = GetHighPrecisionTimestamp() - eventStartTime;
            _metrics.RecordEventLatency(marketEvent.Type, eventDuration);
            _eventCount++;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessMarketData(MarketTick tick)
    {
        var symbol = tick.Symbol.ToString();
        UpdateOrderBook(symbol, tick);
        _portfolio.UpdateMarketData(tick);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessQuoteUpdate(QuoteUpdate quote)
    {
        var symbol = quote.Symbol.ToString();
        UpdateOrderBook(symbol, quote);
        _portfolio.UpdateQuote(quote);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessFill(Fill fill)
    {
        _portfolio.ApplyFill(fill);
        _strategy.OnFill(in fill, _portfolio.GetSnapshot(_currentTimestamp));
        _metrics.RecordFill(fill);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ProcessOrderAck(OrderAck orderAck)
    {
        _portfolio.ApplyOrderAck(orderAck);
        _strategy.OnOrderAck(in orderAck);
        _metrics.RecordOrderAck(orderAck);
    }

    private Task ProcessOrders(IEnumerable<Order> orders)
    {
        foreach (var order in orders)
        {
            var portfolioState = _portfolio.GetSnapshot(_currentTimestamp);
            
            // Risk check
            if (!_riskChecks.Approve(in order, in portfolioState, out string reason))
            {
                var rejection = new OrderAck(
                    order.OrderId,
                    "",
                    OrderStatus.Rejected,
                    _currentTimestamp,
                    reason);
                
                ProcessOrderAck(rejection);
                continue;
            }

            // Accept order
            var acceptance = new OrderAck(
                order.OrderId,
                $"EX{Random.Shared.Next(100000, 999999)}",
                OrderStatus.Accepted,
                _currentTimestamp);
            
            ProcessOrderAck(acceptance);

            // Try to fill against order book
            var symbol = order.Symbol.ToString();
            if (_orderBooks.TryGetValue(symbol, out var book))
            {
                var fills = _fillModel.TryFill(in order, in book);
                
                foreach (var fill in fills)
                {
                    ProcessFill(fill);
                }
            }
        }
        
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateOrderBook(string symbol, MarketTick tick)
    {
        if (!_orderBooks.ContainsKey(symbol))
        {
            _orderBooks[symbol] = new OrderBook(
                symbol.AsMemory(),
                tick.TimestampNs,
                ReadOnlyMemory<BookLevel>.Empty,
                ReadOnlyMemory<BookLevel>.Empty);
        }

        // Simplified book update - in real system would maintain full depth
        var level = new BookLevel(tick.Price, tick.Quantity);
        var levels = new BookLevel[] { level };
        
        _orderBooks[symbol] = tick.Type == MarketDataType.Bid
            ? new OrderBook(symbol.AsMemory(), tick.TimestampNs, levels.AsMemory(), ReadOnlyMemory<BookLevel>.Empty)
            : new OrderBook(symbol.AsMemory(), tick.TimestampNs, ReadOnlyMemory<BookLevel>.Empty, levels.AsMemory());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void UpdateOrderBook(string symbol, QuoteUpdate quote)
    {
        var bidLevel = new BookLevel(quote.BidPrice, quote.BidSize);
        var askLevel = new BookLevel(quote.AskPrice, quote.AskSize);
        
        _orderBooks[symbol] = new OrderBook(
            symbol.AsMemory(),
            quote.TimestampNs,
            new BookLevel[] { bidLevel },
            new BookLevel[] { askLevel });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetEventPriority(MarketEvent marketEvent)
    {
        // Process market data first, then fills, then acks
        return marketEvent.Type switch
        {
            MarketEventType.MarketData => 0,
            MarketEventType.Quote => 0,
            MarketEventType.Fill => 1,
            MarketEventType.OrderAck => 2,
            _ => 3
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetHighPrecisionTimestamp()
    {
        return DateTime.UtcNow.Ticks;
    }

    private void ReportProgress(int eventsProcessed, int totalEvents)
    {
        if (_config.EnableProgressReporting)
        {
            var progress = (double)eventsProcessed / totalEvents * 100;
            Console.WriteLine($"Progress: {progress:F1}% ({eventsProcessed:N0}/{totalEvents:N0} events)");
        }
    }

    private async Task CreateCheckpoint()
    {
        if (!_config.EnableCheckpointing || string.IsNullOrEmpty(_config.CheckpointPath))
            return;

        // Simplified checkpointing - serialize portfolio state
        var checkpointPath = Path.Combine(_config.CheckpointPath, $"checkpoint_{_currentTimestamp}.json");
        var portfolioState = _portfolio.GetSnapshot(_currentTimestamp);
        
        // In real implementation, would serialize full state
        await File.WriteAllTextAsync(checkpointPath, 
            System.Text.Json.JsonSerializer.Serialize(portfolioState));
    }

    private BacktestResults GenerateResults()
    {
        var finalPortfolioState = _portfolio.GetSnapshot(_currentTimestamp);
        var strategyState = _strategy.GetState();
        
        return new BacktestResults
        {
            StartTimestamp = _eventQueue.Keys.FirstOrDefault(),
            EndTimestamp = _currentTimestamp,
            EventsProcessed = _eventCount,
            FinalPortfolioState = finalPortfolioState,
            PerformanceMetrics = _metrics.GetSnapshot(),
            StrategyState = strategyState,
            Duration = _metrics.GetSnapshot().BacktestDuration
        };
    }

    /// <summary>
    /// Current backtest timestamp
    /// </summary>
    public ulong CurrentTimestamp => _currentTimestamp;

    /// <summary>
    /// Current portfolio state
    /// </summary>
    public PortfolioState CurrentPortfolio => _portfolio.GetSnapshot(_currentTimestamp);

    /// <summary>
    /// Performance metrics
    /// </summary>
    public PerformanceMetrics Metrics => _metrics;
}

/// <summary>
/// Backtest configuration
/// </summary>
public sealed class BacktestConfig
{
    public decimal InitialCash { get; set; } = 100000m;
    public int CheckpointInterval { get; set; } = 10000;
    public bool EnableCheckpointing { get; set; } = false;
    public bool EnableProgressReporting { get; set; } = true;
    public string? CheckpointPath { get; set; }
    public TimeSpan ReportingInterval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Backtest results
/// </summary>
public sealed class BacktestResults
{
    public required ulong StartTimestamp { get; init; }
    public required ulong EndTimestamp { get; init; }
    public required int EventsProcessed { get; init; }
    public required PortfolioState FinalPortfolioState { get; init; }
    public required PerformanceSnapshot PerformanceMetrics { get; init; }
    public required IReadOnlyDictionary<string, object> StrategyState { get; init; }
    public required TimeSpan Duration { get; init; }
    
    public decimal TotalReturn => FinalPortfolioState.GetTotalPnL();
    public double EventsPerSecond => EventsProcessed / Duration.TotalSeconds;
}
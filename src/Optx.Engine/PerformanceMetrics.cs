using System.Runtime.CompilerServices;
using Optx.Core.Events;
using Optx.Core.Types;

namespace Optx.Engine;

/// <summary>
/// Performance metrics collection with latency histograms
/// </summary>
public sealed class PerformanceMetrics
{
    private readonly Dictionary<MarketEventType, LatencyHistogram> _eventLatencies;
    private readonly LatencyHistogram _orderProcessingLatency;
    private readonly Dictionary<string, int> _fillCounts;
    private readonly Dictionary<OrderStatus, int> _orderStatusCounts;
    
    private TimeSpan _backtestDuration;
    private int _totalEvents;
    private int _totalFills;
    private int _totalOrders;

    public PerformanceMetrics()
    {
        _eventLatencies = new Dictionary<MarketEventType, LatencyHistogram>
        {
            [MarketEventType.MarketData] = new LatencyHistogram(),
            [MarketEventType.Quote] = new LatencyHistogram(),
            [MarketEventType.Fill] = new LatencyHistogram(),
            [MarketEventType.OrderAck] = new LatencyHistogram()
        };
        
        _orderProcessingLatency = new LatencyHistogram();
        _fillCounts = new Dictionary<string, int>();
        _orderStatusCounts = new Dictionary<OrderStatus, int>();
    }

    /// <summary>
    /// Record event processing latency
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordEventLatency(MarketEventType eventType, long latencyTicks)
    {
        _eventLatencies[eventType].Record(latencyTicks);
        _totalEvents++;
    }

    /// <summary>
    /// Record order processing latency
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordOrderLatency(long latencyTicks)
    {
        _orderProcessingLatency.Record(latencyTicks);
        _totalOrders++;
    }

    /// <summary>
    /// Record fill execution
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordFill(Fill fill)
    {
        var symbol = fill.OrderId.Split('_')[0]; // Extract symbol from order ID
        _fillCounts[symbol] = _fillCounts.GetValueOrDefault(symbol, 0) + 1;
        _totalFills++;
    }

    /// <summary>
    /// Record order acknowledgment
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RecordOrderAck(OrderAck orderAck)
    {
        _orderStatusCounts[orderAck.Status] = _orderStatusCounts.GetValueOrDefault(orderAck.Status, 0) + 1;
    }

    /// <summary>
    /// Set backtest duration
    /// </summary>
    public void SetBacktestDuration(TimeSpan duration)
    {
        _backtestDuration = duration;
    }

    /// <summary>
    /// Get performance snapshot
    /// </summary>
    public PerformanceSnapshot GetSnapshot()
    {
        return new PerformanceSnapshot
        {
            BacktestDuration = _backtestDuration,
            TotalEvents = _totalEvents,
            TotalFills = _totalFills,
            TotalOrders = _totalOrders,
            EventsPerSecond = _backtestDuration.TotalSeconds > 0 ? _totalEvents / _backtestDuration.TotalSeconds : 0,
            EventLatencies = _eventLatencies.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.GetStats()),
            OrderProcessingLatency = _orderProcessingLatency.GetStats(),
            FillCounts = new Dictionary<string, int>(_fillCounts),
            OrderStatusCounts = new Dictionary<OrderStatus, int>(_orderStatusCounts)
        };
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        foreach (var histogram in _eventLatencies.Values)
        {
            histogram.Reset();
        }
        
        _orderProcessingLatency.Reset();
        _fillCounts.Clear();
        _orderStatusCounts.Clear();
        
        _backtestDuration = TimeSpan.Zero;
        _totalEvents = 0;
        _totalFills = 0;
        _totalOrders = 0;
    }
}

/// <summary>
/// Performance metrics snapshot
/// </summary>
public sealed class PerformanceSnapshot
{
    public required TimeSpan BacktestDuration { get; init; }
    public required int TotalEvents { get; init; }
    public required int TotalFills { get; init; }
    public required int TotalOrders { get; init; }
    public required double EventsPerSecond { get; init; }
    public required Dictionary<MarketEventType, LatencyStats> EventLatencies { get; init; }
    public required LatencyStats OrderProcessingLatency { get; init; }
    public required Dictionary<string, int> FillCounts { get; init; }
    public required Dictionary<OrderStatus, int> OrderStatusCounts { get; init; }
}

/// <summary>
/// Latency histogram for performance measurement
/// </summary>
public sealed class LatencyHistogram
{
    private readonly long[] _buckets;
    private readonly long[] _bucketBounds;
    private long _count;
    private long _sum;
    private long _min;
    private long _max;

    public LatencyHistogram()
    {
        // Exponential buckets: 1μs, 10μs, 100μs, 1ms, 10ms, 100ms, 1s, 10s
        _bucketBounds = new long[] { 10, 100, 1000, 10000, 100000, 1000000, 10000000, 100000000 };
        _buckets = new long[_bucketBounds.Length + 1];
        _min = long.MaxValue;
        _max = long.MinValue;
    }

    /// <summary>
    /// Record latency sample in ticks
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Record(long latencyTicks)
    {
        var latencyMicros = latencyTicks / 10; // Convert 100ns ticks to microseconds
        
        _count++;
        _sum += latencyMicros;
        _min = Math.Min(_min, latencyMicros);
        _max = Math.Max(_max, latencyMicros);

        // Find appropriate bucket
        for (int i = 0; i < _bucketBounds.Length; i++)
        {
            if (latencyMicros <= _bucketBounds[i])
            {
                _buckets[i]++;
                return;
            }
        }
        
        // Overflow bucket
        _buckets[_buckets.Length - 1]++;
    }

    /// <summary>
    /// Get latency statistics
    /// </summary>
    public LatencyStats GetStats()
    {
        if (_count == 0)
        {
            return new LatencyStats
            {
                Count = 0,
                Mean = 0,
                Min = 0,
                Max = 0,
                P50 = 0,
                P90 = 0,
                P99 = 0,
                P999 = 0
            };
        }

        return new LatencyStats
        {
            Count = _count,
            Mean = (double)_sum / _count,
            Min = _min,
            Max = _max,
            P50 = CalculatePercentile(0.5),
            P90 = CalculatePercentile(0.9),
            P99 = CalculatePercentile(0.99),
            P999 = CalculatePercentile(0.999)
        };
    }

    private long CalculatePercentile(double percentile)
    {
        var targetCount = (long)(_count * percentile);
        var cumulativeCount = 0L;

        for (int i = 0; i < _buckets.Length; i++)
        {
            cumulativeCount += _buckets[i];
            if (cumulativeCount >= targetCount)
            {
                return i < _bucketBounds.Length ? _bucketBounds[i] : _bucketBounds[^1] * 10;
            }
        }

        return _max;
    }

    /// <summary>
    /// Reset histogram
    /// </summary>
    public void Reset()
    {
        Array.Clear(_buckets);
        _count = 0;
        _sum = 0;
        _min = long.MaxValue;
        _max = long.MinValue;
    }
}

/// <summary>
/// Latency statistics
/// </summary>
public sealed class LatencyStats
{
    public required long Count { get; init; }
    public required double Mean { get; init; }
    public required long Min { get; init; }
    public required long Max { get; init; }
    public required long P50 { get; init; }
    public required long P90 { get; init; }
    public required long P99 { get; init; }
    public required long P999 { get; init; }

    public override string ToString()
    {
        return $"Count: {Count:N0}, Mean: {Mean:F1}μs, P50: {P50}μs, P99: {P99}μs, P999: {P999}μs";
    }
}
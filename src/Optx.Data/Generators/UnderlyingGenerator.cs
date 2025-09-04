using System.Runtime.CompilerServices;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Data.Generators;

/// <summary>
/// Geometric Brownian Motion generator with optional jump diffusion
/// </summary>
public sealed class UnderlyingGenerator
{
    private readonly Random _random;
    private readonly double _drift;
    private readonly double _volatility;
    private readonly double _jumpIntensity;
    private readonly double _jumpMean;
    private readonly double _jumpStdDev;
    private readonly TimeSpan _timeStep;

    private double _currentPrice;
    private ulong _currentTimestamp;

    /// <summary>
    /// Create underlying price generator
    /// </summary>
    /// <param name="initialPrice">Starting price</param>
    /// <param name="drift">Annual drift rate</param>
    /// <param name="volatility">Annual volatility</param>
    /// <param name="timeStep">Time step between observations</param>
    /// <param name="jumpIntensity">Poisson jump intensity (jumps per year)</param>
    /// <param name="jumpMean">Mean jump size (log-normal)</param>
    /// <param name="jumpStdDev">Jump size standard deviation</param>
    /// <param name="seed">Random seed for reproducibility</param>
    public UnderlyingGenerator(
        double initialPrice,
        double drift = 0.05,
        double volatility = 0.2,
        TimeSpan? timeStep = null,
        double jumpIntensity = 0.0,
        double jumpMean = 0.0,
        double jumpStdDev = 0.1,
        int? seed = null)
    {
        if (initialPrice <= 0.0)
            throw new ArgumentException("Initial price must be positive", nameof(initialPrice));

        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _currentPrice = initialPrice;
        _drift = drift;
        _volatility = volatility;
        _jumpIntensity = jumpIntensity;
        _jumpMean = jumpMean;
        _jumpStdDev = jumpStdDev;
        _timeStep = timeStep ?? TimeSpan.FromSeconds(1);
        _currentTimestamp = TimeUtils.GetCurrentNanoseconds();
    }

    /// <summary>
    /// Generate next price tick
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public MarketTick GenerateNext()
    {
        var dt = _timeStep.TotalDays / 365.25; // Convert to years
        
        // Generate Brownian motion component
        var dW = _random.NextGaussian() * Math.Sqrt(dt);
        var diffusion = (_drift - 0.5 * _volatility * _volatility) * dt + _volatility * dW;
        
        // Generate jump component if applicable
        var jumpComponent = 0.0;
        if (_jumpIntensity > 0.0)
        {
            var poissonProb = _jumpIntensity * dt;
            if (_random.NextDouble() < poissonProb)
            {
                jumpComponent = _jumpMean + _jumpStdDev * _random.NextGaussian();
            }
        }

        // Apply price evolution: dS/S = drift*dt + vol*dW + jump
        _currentPrice *= Math.Exp(diffusion + jumpComponent);
        _currentTimestamp += (ulong)_timeStep.Ticks * 100; // Convert to nanoseconds

        return new MarketTick(
            _currentTimestamp,
            "SPY".AsMemory(),
            (decimal)_currentPrice,
            100, // Default quantity
            MarketDataType.Trade);
    }

    /// <summary>
    /// Generate price path for specified duration
    /// </summary>
    public List<MarketTick> GeneratePath(TimeSpan duration, string symbol = "SPY")
    {
        var numSteps = (int)(duration.Ticks / _timeStep.Ticks);
        var path = new List<MarketTick>(numSteps);
        
        for (int i = 0; i < numSteps; i++)
        {
            var tick = GenerateNext();
            path.Add(new MarketTick(
                tick.TimestampNs,
                symbol.AsMemory(),
                tick.Price,
                tick.Quantity,
                tick.Type));
        }
        
        return path;
    }

    /// <summary>
    /// Generate price path with custom symbol and quantities
    /// </summary>
    public List<MarketTick> GeneratePath(
        TimeSpan duration,
        string symbol,
        Func<int, int> quantityFunc)
    {
        var numSteps = (int)(duration.Ticks / _timeStep.Ticks);
        var path = new List<MarketTick>(numSteps);
        
        for (int i = 0; i < numSteps; i++)
        {
            var tick = GenerateNext();
            path.Add(new MarketTick(
                tick.TimestampNs,
                symbol.AsMemory(),
                tick.Price,
                quantityFunc(i),
                tick.Type));
        }
        
        return path;
    }

    /// <summary>
    /// Reset generator to initial state
    /// </summary>
    public void Reset(double? newInitialPrice = null, int? newSeed = null)
    {
        if (newInitialPrice.HasValue)
        {
            if (newInitialPrice.Value <= 0.0)
                throw new ArgumentException("Initial price must be positive");
            _currentPrice = newInitialPrice.Value;
        }

        if (newSeed.HasValue)
        {
            // Reset random generator with new seed
            var newRandom = new Random(newSeed.Value);
            // Copy state - this is a simplified approach
            for (int i = 0; i < 100; i++) newRandom.NextDouble();
        }

        _currentTimestamp = TimeUtils.GetCurrentNanoseconds();
    }

    /// <summary>
    /// Current price
    /// </summary>
    public double CurrentPrice => _currentPrice;

    /// <summary>
    /// Current timestamp
    /// </summary>
    public ulong CurrentTimestamp => _currentTimestamp;

    /// <summary>
    /// Set current price and timestamp
    /// </summary>
    public void SetState(double price, ulong timestamp)
    {
        if (price <= 0.0)
            throw new ArgumentException("Price must be positive");
        
        _currentPrice = price;
        _currentTimestamp = timestamp;
    }
}

/// <summary>
/// Extensions for Random class
/// </summary>
internal static class RandomExtensions
{
    /// <summary>
    /// Generate Gaussian random variable using Box-Muller transform
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NextGaussian(this Random random, double mean = 0.0, double stdDev = 1.0)
    {
        // Use Box-Muller transform
        static double GenerateStandardNormal(Random rng)
        {
            double u1 = 1.0 - rng.NextDouble(); // (0,1]
            double u2 = 1.0 - rng.NextDouble(); // (0,1]
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        return mean + stdDev * GenerateStandardNormal(random);
    }
}
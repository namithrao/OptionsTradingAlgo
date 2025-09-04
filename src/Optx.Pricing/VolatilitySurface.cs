using System.Runtime.CompilerServices;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Pricing;

/// <summary>
/// Volatility surface implementation with bilinear interpolation in variance space
/// </summary>
public sealed class VolatilitySurface
{
    private readonly double[] _expiries;
    private readonly double[] _strikes;
    private readonly double[,] _volatilities;
    private readonly int _numExpiries;
    private readonly int _numStrikes;

    /// <summary>
    /// Create volatility surface from grid data
    /// </summary>
    /// <param name="expiries">Expiry times in years (sorted)</param>
    /// <param name="strikes">Strike prices (sorted)</param>
    /// <param name="volatilities">Volatility matrix [expiry, strike]</param>
    public VolatilitySurface(double[] expiries, double[] strikes, double[,] volatilities)
    {
        if (expiries.Length == 0 || strikes.Length == 0)
            throw new ArgumentException("Expiries and strikes cannot be empty");

        if (volatilities.GetLength(0) != expiries.Length || volatilities.GetLength(1) != strikes.Length)
            throw new ArgumentException("Volatility matrix dimensions must match expiries and strikes");

        _expiries = (double[])expiries.Clone();
        _strikes = (double[])strikes.Clone();
        _volatilities = (double[,])volatilities.Clone();
        _numExpiries = expiries.Length;
        _numStrikes = strikes.Length;

        // Validate sorting
        for (int i = 1; i < _numExpiries; i++)
        {
            if (_expiries[i] <= _expiries[i - 1])
                throw new ArgumentException("Expiries must be sorted in ascending order");
        }

        for (int i = 1; i < _numStrikes; i++)
        {
            if (_strikes[i] <= _strikes[i - 1])
                throw new ArgumentException("Strikes must be sorted in ascending order");
        }
    }

    /// <summary>
    /// Get interpolated volatility for given expiry and strike
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetVolatility(double expiry, double strike)
    {
        if (expiry <= 0.0 || strike <= 0.0)
            return double.NaN;

        // Find expiry bounds
        var (expiryIndex, expiryWeight) = FindBounds(_expiries, expiry, _numExpiries);
        
        // Find strike bounds
        var (strikeIndex, strikeWeight) = FindBounds(_strikes, strike, _numStrikes);

        // Get corner volatilities
        var vol00 = _volatilities[expiryIndex, strikeIndex];
        var vol01 = strikeIndex + 1 < _numStrikes ? _volatilities[expiryIndex, strikeIndex + 1] : vol00;
        var vol10 = expiryIndex + 1 < _numExpiries ? _volatilities[expiryIndex + 1, strikeIndex] : vol00;
        var vol11 = (expiryIndex + 1 < _numExpiries && strikeIndex + 1 < _numStrikes) 
            ? _volatilities[expiryIndex + 1, strikeIndex + 1] : vol00;

        // Convert to variance for interpolation
        var var00 = vol00 * vol00 * _expiries[expiryIndex];
        var var01 = vol01 * vol01 * _expiries[expiryIndex];
        var var10 = vol10 * vol10 * (expiryIndex + 1 < _numExpiries ? _expiries[expiryIndex + 1] : _expiries[expiryIndex]);
        var var11 = vol11 * vol11 * (expiryIndex + 1 < _numExpiries ? _expiries[expiryIndex + 1] : _expiries[expiryIndex]);

        // Bilinear interpolation in variance space
        var var0 = MathUtils.Lerp(var00, var01, strikeWeight);
        var var1 = MathUtils.Lerp(var10, var11, strikeWeight);
        var varInterpolated = MathUtils.Lerp(var0, var1, expiryWeight);

        // Convert back to volatility
        return MathUtils.SafeSqrt(varInterpolated / expiry);
    }

    /// <summary>
    /// Get volatility for an option contract
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetVolatility(OptionContract contract, DateTime currentTime)
    {
        var timeToExpiry = contract.GetTimeToExpiry(currentTime);
        return GetVolatility(timeToExpiry, (double)contract.Strike);
    }

    /// <summary>
    /// Get ATM volatility for given expiry
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetAtmVolatility(double expiry, double atmStrike)
    {
        return GetVolatility(expiry, atmStrike);
    }

    /// <summary>
    /// Get volatility smile for specific expiry
    /// </summary>
    public double[] GetSmile(double expiry)
    {
        var smile = new double[_numStrikes];
        
        for (int i = 0; i < _numStrikes; i++)
        {
            smile[i] = GetVolatility(expiry, _strikes[i]);
        }
        
        return smile;
    }

    /// <summary>
    /// Get term structure for specific strike
    /// </summary>
    public double[] GetTermStructure(double strike)
    {
        var termStructure = new double[_numExpiries];
        
        for (int i = 0; i < _numExpiries; i++)
        {
            termStructure[i] = GetVolatility(_expiries[i], strike);
        }
        
        return termStructure;
    }

    /// <summary>
    /// Find interpolation bounds and weights
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int index, double weight) FindBounds(ReadOnlySpan<double> array, double value, int length)
    {
        if (value <= array[0])
            return (0, 0.0);

        if (value >= array[length - 1])
            return (length - 2, 1.0);

        // Binary search
        int left = 0;
        int right = length - 1;
        
        while (right - left > 1)
        {
            int mid = (left + right) / 2;
            if (array[mid] <= value)
                left = mid;
            else
                right = mid;
        }

        var weight = (value - array[left]) / (array[right] - array[left]);
        return (left, weight);
    }

    /// <summary>
    /// Bump volatility surface for risk calculations
    /// </summary>
    public VolatilitySurface BumpVolatility(double bumpSize)
    {
        var bumpedVols = new double[_numExpiries, _numStrikes];
        
        for (int i = 0; i < _numExpiries; i++)
        {
            for (int j = 0; j < _numStrikes; j++)
            {
                bumpedVols[i, j] = _volatilities[i, j] + bumpSize;
            }
        }
        
        return new VolatilitySurface(_expiries, _strikes, bumpedVols);
    }

    /// <summary>
    /// Get surface summary statistics
    /// </summary>
    public SurfaceStats GetStats()
    {
        double minVol = double.MaxValue;
        double maxVol = double.MinValue;
        double sumVol = 0.0;
        int count = 0;

        for (int i = 0; i < _numExpiries; i++)
        {
            for (int j = 0; j < _numStrikes; j++)
            {
                var vol = _volatilities[i, j];
                if (!double.IsNaN(vol))
                {
                    minVol = Math.Min(minVol, vol);
                    maxVol = Math.Max(maxVol, vol);
                    sumVol += vol;
                    count++;
                }
            }
        }

        return new SurfaceStats
        {
            MinVolatility = count > 0 ? minVol : double.NaN,
            MaxVolatility = count > 0 ? maxVol : double.NaN,
            AverageVolatility = count > 0 ? sumVol / count : double.NaN,
            ValidPoints = count,
            TotalPoints = _numExpiries * _numStrikes
        };
    }

    /// <summary>
    /// Available expiry times
    /// </summary>
    public ReadOnlySpan<double> Expiries => _expiries.AsSpan();

    /// <summary>
    /// Available strike prices
    /// </summary>
    public ReadOnlySpan<double> Strikes => _strikes.AsSpan();
}

/// <summary>
/// Volatility surface statistics
/// </summary>
public readonly record struct SurfaceStats(
    double MinVolatility,
    double MaxVolatility,
    double AverageVolatility,
    int ValidPoints,
    int TotalPoints)
{
    public double Coverage => TotalPoints > 0 ? (double)ValidPoints / TotalPoints : 0.0;
}

/// <summary>
/// Builder for constructing volatility surfaces
/// </summary>
public sealed class VolatilitySurfaceBuilder
{
    private readonly List<(double expiry, double strike, double volatility)> _points = new();

    /// <summary>
    /// Add a volatility point
    /// </summary>
    public VolatilitySurfaceBuilder AddPoint(double expiry, double strike, double volatility)
    {
        if (expiry > 0.0 && strike > 0.0 && volatility > 0.0)
            _points.Add((expiry, strike, volatility));
        return this;
    }

    /// <summary>
    /// Build the volatility surface
    /// </summary>
    public VolatilitySurface Build()
    {
        if (_points.Count == 0)
            throw new InvalidOperationException("No valid points added to surface builder");

        var expiries = _points.Select(p => p.expiry).Distinct().OrderBy(x => x).ToArray();
        var strikes = _points.Select(p => p.strike).Distinct().OrderBy(x => x).ToArray();

        var volatilities = new double[expiries.Length, strikes.Length];
        
        // Fill with NaN initially
        for (int i = 0; i < expiries.Length; i++)
        {
            for (int j = 0; j < strikes.Length; j++)
            {
                volatilities[i, j] = double.NaN;
            }
        }

        // Fill in available points
        foreach (var (expiry, strike, volatility) in _points)
        {
            var expiryIndex = Array.IndexOf(expiries, expiry);
            var strikeIndex = Array.IndexOf(strikes, strike);
            
            if (expiryIndex >= 0 && strikeIndex >= 0)
                volatilities[expiryIndex, strikeIndex] = volatility;
        }

        // Simple interpolation for missing points
        InterpolateMissingPoints(volatilities, expiries.Length, strikes.Length);

        return new VolatilitySurface(expiries, strikes, volatilities);
    }

    private static void InterpolateMissingPoints(double[,] volatilities, int numExpiries, int numStrikes)
    {
        // Forward fill missing values (simple approach)
        for (int i = 0; i < numExpiries; i++)
        {
            for (int j = 0; j < numStrikes; j++)
            {
                if (double.IsNaN(volatilities[i, j]))
                {
                    // Look for nearest non-NaN value
                    double nearestVol = FindNearestVolatility(volatilities, i, j, numExpiries, numStrikes);
                    if (!double.IsNaN(nearestVol))
                        volatilities[i, j] = nearestVol;
                }
            }
        }
    }

    private static double FindNearestVolatility(double[,] volatilities, int targetI, int targetJ, int numExpiries, int numStrikes)
    {
        for (int radius = 1; radius < Math.Max(numExpiries, numStrikes); radius++)
        {
            for (int i = Math.Max(0, targetI - radius); i <= Math.Min(numExpiries - 1, targetI + radius); i++)
            {
                for (int j = Math.Max(0, targetJ - radius); j <= Math.Min(numStrikes - 1, targetJ + radius); j++)
                {
                    if (!double.IsNaN(volatilities[i, j]))
                        return volatilities[i, j];
                }
            }
        }
        return 0.2; // Default fallback volatility
    }
}
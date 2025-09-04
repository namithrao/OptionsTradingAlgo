using System.Runtime.CompilerServices;

namespace Optx.Core.Utils;

/// <summary>
/// High-performance mathematical utilities for options pricing
/// </summary>
public static class MathUtils
{
    private const double SqrtTwoPi = 2.5066282746310005024;
    private const double InvSqrtTwoPi = 0.3989422804014326779;
    
    /// <summary>
    /// Cumulative normal distribution function using Abramowitz and Stegun approximation
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NormalCdf(double x)
    {
        if (x > 6.0) return 1.0;
        if (x < -6.0) return 0.0;

        const double a1 = 0.254829592;
        const double a2 = -0.284496736;
        const double a3 = 1.421413741;
        const double a4 = -1.453152027;
        const double a5 = 1.061405429;
        const double p = 0.3275911;

        int sign = x < 0 ? -1 : 1;
        x = Math.Abs(x) / Math.Sqrt(2.0);

        double t = 1.0 / (1.0 + p * x);
        double y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);

        return 0.5 * (1.0 + sign * y);
    }

    /// <summary>
    /// Normal probability density function
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double NormalPdf(double x)
    {
        return InvSqrtTwoPi * Math.Exp(-0.5 * x * x);
    }

    /// <summary>
    /// Fast natural logarithm approximation
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FastLog(double x)
    {
        if (x <= 0.0) return double.NegativeInfinity;
        return Math.Log(x);
    }

    /// <summary>
    /// Fast exponential approximation
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double FastExp(double x)
    {
        if (x > 700.0) return double.PositiveInfinity;
        if (x < -700.0) return 0.0;
        return Math.Exp(x);
    }

    /// <summary>
    /// Safe square root that handles negative values
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double SafeSqrt(double x)
    {
        return x <= 0.0 ? 0.0 : Math.Sqrt(x);
    }

    /// <summary>
    /// Clamp value between min and max
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Clamp(double value, double min, double max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// Clamp value between min and max (decimal version)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal Clamp(decimal value, decimal min, decimal max)
    {
        return value < min ? min : (value > max ? max : value);
    }

    /// <summary>
    /// Check if two doubles are approximately equal
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsApproximatelyEqual(double a, double b, double epsilon = 1e-9)
    {
        return Math.Abs(a - b) < epsilon;
    }

    /// <summary>
    /// Check if value is within tolerance of target
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsWithinTolerance(double value, double target, double tolerance)
    {
        return Math.Abs(value - target) <= tolerance;
    }

    /// <summary>
    /// Linear interpolation
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Lerp(double a, double b, double t)
    {
        return a + t * (b - a);
    }

    /// <summary>
    /// Variance interpolation (for vol surface)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double VarianceInterp(double vol1, double t1, double vol2, double t2, double t)
    {
        if (t <= t1) return vol1;
        if (t >= t2) return vol2;
        
        double var1 = vol1 * vol1 * t1;
        double var2 = vol2 * vol2 * t2;
        double varT = Lerp(var1, var2, (t - t1) / (t2 - t1));
        
        return SafeSqrt(varT / t);
    }
}
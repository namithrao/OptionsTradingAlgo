using System.Runtime.CompilerServices;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Pricing;

/// <summary>
/// High-performance implied volatility solver using Newton-Raphson with bisection fallback
/// </summary>
public static class ImpliedVolatility
{
    private const double MinVolatility = 0.001;
    private const double MaxVolatility = 5.0;
    private const double Tolerance = 1e-7;
    private const int MaxIterations = 100;
    private const double MinVega = 1e-10;

    /// <summary>
    /// Calculate implied volatility from option price using Newton-Raphson method
    /// </summary>
    /// <param name="optionPrice">Market price of option</param>
    /// <param name="spot">Current underlying price</param>
    /// <param name="strike">Strike price</param>
    /// <param name="timeToExpiry">Time to expiry in years</param>
    /// <param name="riskFreeRate">Risk-free rate</param>
    /// <param name="dividendYield">Continuous dividend yield</param>
    /// <param name="optionType">Call or Put</param>
    /// <param name="initialGuess">Initial volatility guess (default: ATM vol estimate)</param>
    /// <returns>Implied volatility or NaN if convergence failed</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Solve(
        double optionPrice,
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType,
        double initialGuess = double.NaN)
    {
        if (optionPrice <= 0.0 || spot <= 0.0 || strike <= 0.0 || timeToExpiry <= 0.0)
            return double.NaN;

        // Check intrinsic bounds
        var intrinsic = CalculateIntrinsic(spot, strike, timeToExpiry, riskFreeRate, dividendYield, optionType);
        if (optionPrice < intrinsic)
            return double.NaN;

        // Use initial guess or estimate
        var vol = double.IsNaN(initialGuess) ? EstimateInitialVolatility(optionPrice, spot, strike, timeToExpiry) : initialGuess;
        vol = MathUtils.Clamp(vol, MinVolatility, MaxVolatility);

        // Try Newton-Raphson first
        var result = SolveNewtonRaphson(optionPrice, spot, strike, timeToExpiry, riskFreeRate, dividendYield, optionType, vol);
        
        if (!double.IsNaN(result))
            return result;

        // Fallback to bisection
        return SolveBisection(optionPrice, spot, strike, timeToExpiry, riskFreeRate, dividendYield, optionType);
    }

    /// <summary>
    /// Newton-Raphson solver implementation
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SolveNewtonRaphson(
        double targetPrice,
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType,
        double initialVol)
    {
        var vol = initialVol;
        
        for (int i = 0; i < MaxIterations; i++)
        {
            var price = BlackScholes.Price(spot, strike, timeToExpiry, vol, riskFreeRate, dividendYield, optionType);
            var vega = BlackScholes.Vega(spot, strike, timeToExpiry, vol, riskFreeRate, dividendYield) * 100.0; // Convert back to per vol point

            if (Math.Abs(vega) < MinVega)
                return double.NaN; // Vega too small, method will be unstable

            var priceDiff = price - targetPrice;
            
            if (Math.Abs(priceDiff) < Tolerance)
                return vol;

            var volAdjustment = priceDiff / vega;
            vol -= volAdjustment;
            
            vol = MathUtils.Clamp(vol, MinVolatility, MaxVolatility);

            // Check for oscillation
            if (i > 10 && Math.Abs(volAdjustment) < 1e-8)
                return vol;
        }

        return double.NaN;
    }

    /// <summary>
    /// Bisection method fallback
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double SolveBisection(
        double targetPrice,
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        double volLow = MinVolatility;
        double volHigh = MaxVolatility;
        
        var priceLow = BlackScholes.Price(spot, strike, timeToExpiry, volLow, riskFreeRate, dividendYield, optionType);
        var priceHigh = BlackScholes.Price(spot, strike, timeToExpiry, volHigh, riskFreeRate, dividendYield, optionType);

        // Check if target is within bounds
        if (targetPrice < priceLow || targetPrice > priceHigh)
            return double.NaN;

        for (int i = 0; i < MaxIterations; i++)
        {
            var volMid = (volLow + volHigh) * 0.5;
            var priceMid = BlackScholes.Price(spot, strike, timeToExpiry, volMid, riskFreeRate, dividendYield, optionType);

            if (Math.Abs(priceMid - targetPrice) < Tolerance || Math.Abs(volHigh - volLow) < 1e-10)
                return volMid;

            if (priceMid < targetPrice)
                volLow = volMid;
            else
                volHigh = volMid;
        }

        return (volLow + volHigh) * 0.5;
    }

    /// <summary>
    /// Estimate initial volatility using Brenner-Subrahmanyam approximation
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double EstimateInitialVolatility(double optionPrice, double spot, double strike, double timeToExpiry)
    {
        var sqrtT = Math.Sqrt(timeToExpiry);
        
        // Brenner-Subrahmanyam approximation: vol ≈ sqrt(2π) * optionPrice / (spot * sqrt(T))
        var estimate = Math.Sqrt(2.0 * Math.PI) * optionPrice / (spot * sqrtT);
        
        return MathUtils.Clamp(estimate, 0.1, 1.0); // Reasonable initial bounds
    }

    /// <summary>
    /// Calculate intrinsic value
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double CalculateIntrinsic(
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        var discountedSpot = spot * MathUtils.FastExp(-dividendYield * timeToExpiry);
        
        return optionType == OptionType.Call
            ? Math.Max(0.0, discountedSpot - strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry))
            : Math.Max(0.0, strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry) - discountedSpot);
    }

    /// <summary>
    /// Batch solve implied volatilities for multiple options
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SolveBatch(
        ReadOnlySpan<double> optionPrices,
        ReadOnlySpan<double> strikes,
        double spot,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType,
        Span<double> results)
    {
        if (optionPrices.Length != strikes.Length || results.Length < optionPrices.Length)
            throw new ArgumentException("Array length mismatch");

        for (int i = 0; i < optionPrices.Length; i++)
        {
            results[i] = Solve(
                optionPrices[i],
                spot,
                strikes[i],
                timeToExpiry,
                riskFreeRate,
                dividendYield,
                optionType);
        }
    }

    /// <summary>
    /// Check if implied volatility calculation makes sense
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValidImpliedVol(
        double impliedVol,
        double optionPrice,
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        if (double.IsNaN(impliedVol) || impliedVol <= 0.0)
            return false;

        var calculatedPrice = BlackScholes.Price(spot, strike, timeToExpiry, impliedVol, riskFreeRate, dividendYield, optionType);
        return Math.Abs(calculatedPrice - optionPrice) < Tolerance * 10; // Allow some tolerance for validation
    }
}
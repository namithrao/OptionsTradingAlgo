using System.Runtime.CompilerServices;
using Optx.Core.Types;
using Optx.Core.Utils;

namespace Optx.Pricing;

/// <summary>
/// High-performance Black-Scholes options pricing implementation
/// </summary>
public static class BlackScholes
{
    private const double MinVolatility = 0.001;
    private const double MaxVolatility = 5.0;
    private const double MinTimeToExpiry = 1e-6;

    /// <summary>
    /// Calculate option price using Black-Scholes formula
    /// </summary>
    /// <param name="spot">Current underlying price</param>
    /// <param name="strike">Strike price</param>
    /// <param name="timeToExpiry">Time to expiry in years</param>
    /// <param name="volatility">Implied volatility</param>
    /// <param name="riskFreeRate">Risk-free rate</param>
    /// <param name="dividendYield">Continuous dividend yield</param>
    /// <param name="optionType">Call or Put</param>
    /// <returns>Option theoretical price</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Price(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return 0.0;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);
        var d2 = d1 - volatility * sqrtT;

        var nd1 = MathUtils.NormalCdf(optionType == OptionType.Call ? d1 : -d1);
        var nd2 = MathUtils.NormalCdf(optionType == OptionType.Call ? d2 : -d2);

        var discountedSpot = spot * MathUtils.FastExp(-dividendYield * timeToExpiry);
        var discountedStrike = strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry);

        return optionType == OptionType.Call
            ? discountedSpot * nd1 - discountedStrike * nd2
            : discountedStrike * nd2 - discountedSpot * nd1;
    }

    /// <summary>
    /// Calculate option delta
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Delta(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return 0.0;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);

        var nd1 = MathUtils.NormalCdf(optionType == OptionType.Call ? d1 : -d1);
        var discountFactor = MathUtils.FastExp(-dividendYield * timeToExpiry);

        return optionType == OptionType.Call
            ? discountFactor * nd1
            : discountFactor * (nd1 - 1.0);
    }

    /// <summary>
    /// Calculate option gamma
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Gamma(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return 0.0;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);

        var phi_d1 = MathUtils.NormalPdf(d1);
        var discountFactor = MathUtils.FastExp(-dividendYield * timeToExpiry);

        return discountFactor * phi_d1 / (spot * volatility * sqrtT);
    }

    /// <summary>
    /// Calculate option theta (time decay)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Theta(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return 0.0;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);
        var d2 = d1 - volatility * sqrtT;

        var phi_d1 = MathUtils.NormalPdf(d1);
        var nd1 = MathUtils.NormalCdf(optionType == OptionType.Call ? d1 : -d1);
        var nd2 = MathUtils.NormalCdf(optionType == OptionType.Call ? d2 : -d2);

        var discountedSpot = spot * MathUtils.FastExp(-dividendYield * timeToExpiry);
        var discountedStrike = strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry);

        var theta1 = -discountedSpot * phi_d1 * volatility / (2.0 * sqrtT);
        
        if (optionType == OptionType.Call)
        {
            var theta2 = dividendYield * discountedSpot * nd1;
            var theta3 = -riskFreeRate * discountedStrike * nd2;
            return (theta1 + theta2 + theta3) / 365.25; // Per day
        }
        else
        {
            var theta2 = -dividendYield * discountedSpot * nd1;
            var theta3 = riskFreeRate * discountedStrike * nd2;
            return (theta1 + theta2 + theta3) / 365.25; // Per day
        }
    }

    /// <summary>
    /// Calculate option vega (volatility sensitivity)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Vega(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return 0.0;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);

        var phi_d1 = MathUtils.NormalPdf(d1);
        var discountedSpot = spot * MathUtils.FastExp(-dividendYield * timeToExpiry);

        return discountedSpot * phi_d1 * sqrtT / 100.0; // Per 1% vol change
    }

    /// <summary>
    /// Calculate option rho (interest rate sensitivity)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Rho(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return 0.0;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);
        var d2 = d1 - volatility * sqrtT;

        var nd2 = MathUtils.NormalCdf(optionType == OptionType.Call ? d2 : -d2);
        var discountedStrike = strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry);

        var rho = optionType == OptionType.Call
            ? timeToExpiry * discountedStrike * nd2
            : -timeToExpiry * discountedStrike * nd2;

        return rho / 100.0; // Per 1% rate change
    }

    /// <summary>
    /// Calculate all Greeks at once for efficiency
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Greeks CalculateGreeks(
        double spot,
        double strike,
        double timeToExpiry,
        double volatility,
        double riskFreeRate,
        double dividendYield,
        OptionType optionType)
    {
        if (spot <= 0.0 || strike <= 0.0 || volatility <= 0.0)
            return Greeks.Zero;

        timeToExpiry = Math.Max(timeToExpiry, MinTimeToExpiry);
        volatility = MathUtils.Clamp(volatility, MinVolatility, MaxVolatility);

        var sqrtT = Math.Sqrt(timeToExpiry);
        var d1 = (MathUtils.FastLog(spot / strike) + (riskFreeRate - dividendYield + 0.5 * volatility * volatility) * timeToExpiry) / (volatility * sqrtT);
        var d2 = d1 - volatility * sqrtT;

        var nd1 = MathUtils.NormalCdf(optionType == OptionType.Call ? d1 : -d1);
        var nd2 = MathUtils.NormalCdf(optionType == OptionType.Call ? d2 : -d2);
        var phi_d1 = MathUtils.NormalPdf(d1);

        var discountedSpot = spot * MathUtils.FastExp(-dividendYield * timeToExpiry);
        var discountedStrike = strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry);

        // Delta
        var delta = optionType == OptionType.Call
            ? MathUtils.FastExp(-dividendYield * timeToExpiry) * nd1
            : MathUtils.FastExp(-dividendYield * timeToExpiry) * (nd1 - 1.0);

        // Gamma
        var gamma = MathUtils.FastExp(-dividendYield * timeToExpiry) * phi_d1 / (spot * volatility * sqrtT);

        // Theta
        var theta1 = -discountedSpot * phi_d1 * volatility / (2.0 * sqrtT);
        var theta = optionType == OptionType.Call
            ? (theta1 + dividendYield * discountedSpot * nd1 - riskFreeRate * discountedStrike * nd2) / 365.25
            : (theta1 - dividendYield * discountedSpot * nd1 + riskFreeRate * discountedStrike * nd2) / 365.25;

        // Vega
        var vega = discountedSpot * phi_d1 * sqrtT / 100.0;

        // Rho
        var rho = (optionType == OptionType.Call ? 1.0 : -1.0) * timeToExpiry * discountedStrike * nd2 / 100.0;

        return new Greeks(delta, gamma, theta, vega, rho);
    }

    /// <summary>
    /// Check put-call parity
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double PutCallParity(
        double callPrice,
        double putPrice,
        double spot,
        double strike,
        double timeToExpiry,
        double riskFreeRate,
        double dividendYield)
    {
        var discountedSpot = spot * MathUtils.FastExp(-dividendYield * timeToExpiry);
        var discountedStrike = strike * MathUtils.FastExp(-riskFreeRate * timeToExpiry);
        
        var theoreticalDiff = discountedSpot - discountedStrike;
        var actualDiff = callPrice - putPrice;
        
        return actualDiff - theoreticalDiff;
    }
}
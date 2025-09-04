using System.Runtime.CompilerServices;
using Optx.Core.Types;
using Optx.Core.Utils;
using Optx.Pricing;

namespace Optx.Data.Generators;

/// <summary>
/// Generate synthetic options chains with realistic bid/ask spreads
/// </summary>
public sealed class OptionsChainGenerator
{
    private readonly Random _random;
    private readonly VolatilitySurface _volSurface;
    private readonly double _riskFreeRate;
    private readonly double _dividendYield;
    private readonly SpreadModel _spreadModel;

    /// <summary>
    /// Create options chain generator
    /// </summary>
    /// <param name="volSurface">Volatility surface for pricing</param>
    /// <param name="riskFreeRate">Risk-free rate</param>
    /// <param name="dividendYield">Dividend yield</param>
    /// <param name="spreadModel">Bid-ask spread model</param>
    /// <param name="seed">Random seed</param>
    public OptionsChainGenerator(
        VolatilitySurface volSurface,
        double riskFreeRate = 0.05,
        double dividendYield = 0.01,
        SpreadModel? spreadModel = null,
        int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
        _volSurface = volSurface ?? throw new ArgumentNullException(nameof(volSurface));
        _riskFreeRate = riskFreeRate;
        _dividendYield = dividendYield;
        _spreadModel = spreadModel ?? new SpreadModel();
    }

    /// <summary>
    /// Generate options chain for given underlying price and expiry
    /// </summary>
    public List<OptionQuote> GenerateChain(
        double underlyingPrice,
        DateTime expiry,
        DateTime currentTime,
        string underlyingSymbol = "SPY",
        int numStrikes = 21,
        double strikeDelta = 5.0)
    {
        var timeToExpiry = TimeUtils.GetTimeToExpiry(currentTime, expiry);
        if (timeToExpiry <= 0.0)
            return new List<OptionQuote>();

        var atmStrike = Math.Round(underlyingPrice / strikeDelta) * strikeDelta;
        var strikes = GenerateStrikes(atmStrike, numStrikes, strikeDelta);
        var options = new List<OptionQuote>();

        foreach (var strike in strikes)
        {
            // Generate call
            var callContract = CreateOptionContract(
                underlyingSymbol, strike, expiry, OptionType.Call);
            var callQuote = GenerateQuote(callContract, underlyingPrice, currentTime);
            if (callQuote != null)
                options.Add(callQuote.Value);

            // Generate put
            var putContract = CreateOptionContract(
                underlyingSymbol, strike, expiry, OptionType.Put);
            var putQuote = GenerateQuote(putContract, underlyingPrice, currentTime);
            if (putQuote != null)
                options.Add(putQuote.Value);
        }

        return options;
    }

    /// <summary>
    /// Generate multiple expiry chains
    /// </summary>
    public Dictionary<DateTime, List<OptionQuote>> GenerateMultiExpiryChain(
        double underlyingPrice,
        DateTime[] expiries,
        DateTime currentTime,
        string underlyingSymbol = "SPY",
        int numStrikes = 21,
        double strikeDelta = 5.0)
    {
        var chains = new Dictionary<DateTime, List<OptionQuote>>();

        foreach (var expiry in expiries)
        {
            var chain = GenerateChain(
                underlyingPrice, expiry, currentTime, 
                underlyingSymbol, numStrikes, strikeDelta);
            
            if (chain.Count > 0)
                chains[expiry] = chain;
        }

        return chains;
    }

    /// <summary>
    /// Generate quote for specific option contract
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public OptionQuote? GenerateQuote(OptionContract contract, double underlyingPrice, DateTime currentTime)
    {
        var timeToExpiry = contract.GetTimeToExpiry(currentTime);
        if (timeToExpiry <= 0.0)
            return null;

        var volatility = _volSurface.GetVolatility(contract, currentTime);
        if (double.IsNaN(volatility) || volatility <= 0.0)
            return null;

        // Calculate theoretical price and Greeks
        var theoreticalPrice = BlackScholes.Price(
            underlyingPrice, (double)contract.Strike, timeToExpiry,
            volatility, _riskFreeRate, _dividendYield, contract.OptionType);

        var greeks = BlackScholes.CalculateGreeks(
            underlyingPrice, (double)contract.Strike, timeToExpiry,
            volatility, _riskFreeRate, _dividendYield, contract.OptionType);

        // Apply spread model
        var spread = _spreadModel.CalculateSpread(theoreticalPrice, contract, greeks);
        var bidPrice = Math.Max(0.01, theoreticalPrice - spread / 2.0);
        var askPrice = theoreticalPrice + spread / 2.0;

        // Generate realistic sizes
        var bidSize = GenerateSize(contract, greeks);
        var askSize = GenerateSize(contract, greeks);

        return new OptionQuote(
            contract,
            (decimal)bidPrice,
            bidSize,
            (decimal)askPrice,
            askSize,
            volatility,
            greeks);
    }

    /// <summary>
    /// Update existing quote with new underlying price
    /// </summary>
    public OptionQuote? UpdateQuote(OptionQuote existingQuote, double newUnderlyingPrice, DateTime currentTime)
    {
        return GenerateQuote(existingQuote.Contract, newUnderlyingPrice, currentTime);
    }

    private static OptionContract CreateOptionContract(
        string underlyingSymbol, 
        double strike, 
        DateTime expiry, 
        OptionType optionType)
    {
        var expiryStr = expiry.ToString("yyMMdd");
        var typeStr = optionType == OptionType.Call ? "C" : "P";
        var strikeStr = ((int)(strike * 1000)).ToString("D8");
        var symbol = $"{underlyingSymbol}{expiryStr}{typeStr}{strikeStr}";

        return new OptionContract(
            symbol.AsMemory(),
            underlyingSymbol.AsMemory(),
            (decimal)strike,
            expiry,
            optionType);
    }

    private static double[] GenerateStrikes(double atmStrike, int numStrikes, double strikeDelta)
    {
        var strikes = new double[numStrikes];
        var halfStrikes = numStrikes / 2;
        
        for (int i = 0; i < numStrikes; i++)
        {
            strikes[i] = atmStrike + (i - halfStrikes) * strikeDelta;
        }
        
        return strikes.Where(s => s > 0).OrderBy(s => s).ToArray();
    }

    private int GenerateSize(OptionContract contract, Greeks greeks)
    {
        // Size based on moneyness and liquidity
        var moneyness = contract.GetMoneyness(100m); // Assume $100 for relative sizing
        var atmDistance = Math.Abs(moneyness - 1.0);
        
        // More size at ATM options
        var baseLiquidity = Math.Exp(-atmDistance * 2.0);
        var randomFactor = 0.5 + _random.NextDouble();
        
        var size = (int)(baseLiquidity * randomFactor * 100);
        return Math.Max(1, Math.Min(size, 1000));
    }
}

/// <summary>
/// Bid-ask spread model
/// </summary>
public sealed class SpreadModel
{
    private readonly double _baseSpread;
    private readonly double _volMultiplier;
    private readonly double _timeDecayMultiplier;
    private readonly double _minSpread;

    public SpreadModel(
        double baseSpread = 0.05,
        double volMultiplier = 0.1,
        double timeDecayMultiplier = 0.02,
        double minSpread = 0.01)
    {
        _baseSpread = baseSpread;
        _volMultiplier = volMultiplier;
        _timeDecayMultiplier = timeDecayMultiplier;
        _minSpread = minSpread;
    }

    /// <summary>
    /// Calculate bid-ask spread for option
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double CalculateSpread(double theoreticalPrice, OptionContract contract, Greeks greeks)
    {
        // Base spread proportional to price
        var spread = _baseSpread * theoreticalPrice;

        // Increase spread for high volatility options
        spread += _volMultiplier * Math.Abs(greeks.Vega) * 0.01;

        // Increase spread for options close to expiry (time decay risk)
        var moneyness = contract.GetMoneyness(100m);
        var atmDistance = Math.Abs(moneyness - 1.0);
        if (atmDistance < 0.1) // Near ATM
        {
            spread += _timeDecayMultiplier * Math.Abs(greeks.Theta);
        }

        // Widen spreads for deep OTM options
        if (atmDistance > 0.2)
        {
            spread *= (1.0 + atmDistance);
        }

        return Math.Max(_minSpread, spread);
    }
}
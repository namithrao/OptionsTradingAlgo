using Optx.Core.Types;
using Optx.Pricing;
using System.Collections.Concurrent;

namespace Optx.Web.Services;

public interface IOptionsCalculationService
{
    Task<OptionQuote> EnrichQuoteWithGreeksAsync(string optionSymbol, decimal bidPrice, decimal askPrice, decimal underlyingPrice, double riskFreeRate = 0.05);
    Task<Dictionary<string, Greeks>> CalculatePortfolioGreeksAsync(Dictionary<string, Position> positions, Dictionary<string, decimal> underlyingPrices);
    Task<double> CalculateImpliedVolatilityAsync(decimal optionPrice, string optionSymbol, decimal underlyingPrice, double riskFreeRate = 0.05);
    VolatilitySurface? GetVolatilitySurface(string underlyingSymbol);
}

public class OptionsCalculationService : IOptionsCalculationService
{
    private readonly ILogger<OptionsCalculationService> _logger;
    private readonly ConcurrentDictionary<string, OptionContract> _contractCache;
    private readonly ConcurrentDictionary<string, VolatilitySurface> _volatilitySurfaces;
    
    public OptionsCalculationService(ILogger<OptionsCalculationService> logger)
    {
        _logger = logger;
        _contractCache = new ConcurrentDictionary<string, OptionContract>();
        _volatilitySurfaces = new ConcurrentDictionary<string, VolatilitySurface>();
        
        // Initialize with default volatility surfaces for common symbols
        InitializeDefaultVolatilitySurfaces();
    }

    public async Task<OptionQuote> EnrichQuoteWithGreeksAsync(string optionSymbol, decimal bidPrice, decimal askPrice, decimal underlyingPrice, double riskFreeRate = 0.05)
    {
        try
        {
            var contract = ParseOptionSymbol(optionSymbol);
            if (contract == null)
            {
                _logger.LogWarning("Could not parse option symbol: {Symbol}", optionSymbol);
                return CreateDefaultQuote(optionSymbol, bidPrice, askPrice);
            }

            var midPrice = (bidPrice + askPrice) * 0.5m;
            var timeToExpiry = contract.Value.GetTimeToExpiry(DateTime.UtcNow);
            
            if (timeToExpiry <= 0)
            {
                return CreateExpiredQuote(contract.Value, bidPrice, askPrice);
            }

            // Calculate implied volatility using existing Optx.Pricing infrastructure
            var impliedVol = ImpliedVolatility.Solve(
                (double)midPrice,
                (double)underlyingPrice,
                (double)contract.Value.Strike,
                timeToExpiry,
                riskFreeRate,
                0.0, // dividendYield
                contract.Value.OptionType);

            // If IV calculation failed, use surface volatility or default
            if (double.IsNaN(impliedVol))
            {
                var surface = GetVolatilitySurface(contract.Value.UnderlyingSymbol.ToString());
                if (surface != null)
                {
                    impliedVol = surface.GetVolatility(contract.Value, DateTime.UtcNow);
                }
                else
                {
                    impliedVol = 0.25; // Default 25% vol
                }
            }

            // Calculate Greeks using existing BlackScholes implementation
            var greeks = BlackScholes.CalculateGreeks(
                (double)underlyingPrice,
                (double)contract.Value.Strike,
                timeToExpiry,
                impliedVol,
                riskFreeRate,
                0.0, // dividendYield
                contract.Value.OptionType);

            return new OptionQuote(
                contract.Value,
                bidPrice,
                0, // BidSize not provided in this context
                askPrice,
                0, // AskSize not provided in this context
                impliedVol,
                greeks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enriching quote with Greeks for {Symbol}", optionSymbol);
            return CreateDefaultQuote(optionSymbol, bidPrice, askPrice);
        }
    }

    public async Task<Dictionary<string, Greeks>> CalculatePortfolioGreeksAsync(Dictionary<string, Position> positions, Dictionary<string, decimal> underlyingPrices)
    {
        var portfolioGreeks = new Dictionary<string, Greeks>();
        var totalGreeks = Greeks.Zero;

        foreach (var (symbol, position) in positions)
        {
            try
            {
                if (IsOptionSymbol(symbol))
                {
                    var contract = ParseOptionSymbol(symbol);
                    if (contract != null && underlyingPrices.TryGetValue(contract.Value.UnderlyingSymbol.ToString(), out var underlyingPrice))
                    {
                        var timeToExpiry = contract.Value.GetTimeToExpiry(DateTime.UtcNow);
                        if (timeToExpiry > 0)
                        {
                            // Get volatility from surface or use default
                            var surface = GetVolatilitySurface(contract.Value.UnderlyingSymbol.ToString());
                            var vol = surface?.GetVolatility(contract.Value, DateTime.UtcNow) ?? 0.25;

                            // Calculate Greeks using existing infrastructure
                            var greeks = BlackScholes.CalculateGreeks(
                                (double)underlyingPrice,
                                (double)contract.Value.Strike,
                                timeToExpiry,
                                vol,
                                0.05, // default risk-free rate
                                0.0,  // dividendYield
                                contract.Value.OptionType);
                            
                            // Scale by position size
                            var positionGreeks = greeks * position.Quantity;
                            portfolioGreeks[symbol] = positionGreeks;
                            totalGreeks = totalGreeks + positionGreeks;
                        }
                    }
                }
                else
                {
                    // For underlying positions, delta = 1.0 per share
                    var underlyingGreeks = new Greeks(position.Quantity, 0, 0, 0, 0);
                    portfolioGreeks[symbol] = underlyingGreeks;
                    totalGreeks = totalGreeks + underlyingGreeks;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating Greeks for position {Symbol}", symbol);
            }
        }

        portfolioGreeks["TOTAL"] = totalGreeks;
        return portfolioGreeks;
    }

    public async Task<double> CalculateImpliedVolatilityAsync(decimal optionPrice, string optionSymbol, decimal underlyingPrice, double riskFreeRate = 0.05)
    {
        var contract = ParseOptionSymbol(optionSymbol);
        if (contract == null)
        {
            return double.NaN;
        }

        var timeToExpiry = contract.Value.GetTimeToExpiry(DateTime.UtcNow);
        if (timeToExpiry <= 0)
        {
            return double.NaN;
        }

        // Use existing ImpliedVolatility solver
        return ImpliedVolatility.Solve(
            (double)optionPrice,
            (double)underlyingPrice,
            (double)contract.Value.Strike,
            timeToExpiry,
            riskFreeRate,
            0.0, // dividendYield
            contract.Value.OptionType);
    }

    public VolatilitySurface? GetVolatilitySurface(string underlyingSymbol)
    {
        return _volatilitySurfaces.TryGetValue(underlyingSymbol, out var surface) ? surface : null;
    }

    private OptionContract? ParseOptionSymbol(string optionSymbol)
    {
        if (_contractCache.TryGetValue(optionSymbol, out var cachedContract))
        {
            return cachedContract;
        }

        try
        {
            // Parse format: SPY251121C00090000
            // SPY = underlying, 251121 = YYMMDD, C/P = call/put, 00090000 = strike * 1000
            
            var callIndex = optionSymbol.IndexOf('C');
            var putIndex = optionSymbol.IndexOf('P');
            
            if (callIndex == -1 && putIndex == -1)
            {
                return null;
            }

            var isCall = callIndex != -1;
            var optionTypeIndex = isCall ? callIndex : putIndex;
            
            if (optionTypeIndex < 6) // Need at least 6 chars for underlying + date
            {
                return null;
            }

            var underlyingSymbol = optionSymbol.Substring(0, optionTypeIndex - 6);
            var dateString = optionSymbol.Substring(optionTypeIndex - 6, 6);
            var strikeString = optionSymbol.Substring(optionTypeIndex + 1);
            
            // Parse date (YYMMDD)
            if (!DateTime.TryParseExact($"20{dateString}", "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var expiry))
            {
                return null;
            }

            // Parse strike (8 digits, divide by 1000)
            if (!decimal.TryParse(strikeString, out var strikeRaw) || strikeString.Length != 8)
            {
                return null;
            }
            
            var strike = strikeRaw / 1000m;
            var optionType = isCall ? OptionType.Call : OptionType.Put;

            var contract = new OptionContract(
                optionSymbol.AsMemory(),
                underlyingSymbol.AsMemory(),
                strike,
                expiry,
                optionType);

            _contractCache.TryAdd(optionSymbol, contract);
            return contract;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing option symbol: {Symbol}", optionSymbol);
            return null;
        }
    }

    private void InitializeDefaultVolatilitySurfaces()
    {
        // Create default volatility surfaces for common symbols
        var commonSymbols = new[] { "SPY", "QQQ", "AAPL", "MSFT", "TSLA", "NVDA" };
        
        foreach (var symbol in commonSymbols)
        {
            var surface = CreateDefaultVolatilitySurface();
            _volatilitySurfaces.TryAdd(symbol, surface);
        }
    }

    private VolatilitySurface CreateDefaultVolatilitySurface()
    {
        // Create a simple default surface
        var builder = new VolatilitySurfaceBuilder();
        
        var expiries = new[] { 0.0274, 0.0822, 0.1644, 0.2466, 0.5 }; // 10D, 30D, 60D, 90D, 6M
        var moneyness = new[] { 0.8, 0.9, 1.0, 1.1, 1.2 }; // 80% to 120% of spot
        var baseVol = 0.25; // 25% base volatility
        
        foreach (var expiry in expiries)
        {
            foreach (var money in moneyness)
            {
                // Simple volatility smile: higher vol for OTM options
                var vol = baseVol + Math.Abs(money - 1.0) * 0.1;
                builder.AddPoint(expiry, money * 100, vol); // Assume $100 spot for scaling
            }
        }
        
        return builder.Build();
    }

    private OptionQuote CreateDefaultQuote(string optionSymbol, decimal bidPrice, decimal askPrice)
    {
        var defaultContract = new OptionContract(
            optionSymbol.AsMemory(),
            "UNKNOWN".AsMemory(),
            0m,
            DateTime.UtcNow.AddDays(30),
            OptionType.Call);

        return new OptionQuote(
            defaultContract,
            bidPrice,
            0,
            askPrice,
            0,
            0.20, // Default 20% IV
            Greeks.Zero);
    }

    private OptionQuote CreateExpiredQuote(OptionContract contract, decimal bidPrice, decimal askPrice)
    {
        return new OptionQuote(
            contract,
            bidPrice,
            0,
            askPrice,
            0,
            0.0, // Zero IV for expired options
            Greeks.Zero);
    }

    private bool IsOptionSymbol(string symbol)
    {
        return symbol.Contains('C') || symbol.Contains('P');
    }
}
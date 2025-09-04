using BenchmarkDotNet.Attributes;
using Optx.Core.Types;
using Optx.Pricing;

namespace Optx.Tools.Bench;

[MemoryDiagnoser]
[SimpleJob]
public class PricingBenchmarks
{
    private const double Spot = 100.0;
    private const double Strike = 105.0;
    private const double TimeToExpiry = 0.25;
    private const double Volatility = 0.2;
    private const double RiskFreeRate = 0.05;
    private const double DividendYield = 0.01;

    [Benchmark]
    public double BlackScholesCallPrice()
    {
        return BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility, 
            RiskFreeRate, DividendYield, OptionType.Call);
    }

    [Benchmark]
    public double BlackScholesPutPrice()
    {
        return BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility, 
            RiskFreeRate, DividendYield, OptionType.Put);
    }

    [Benchmark]
    public Greeks CalculateAllGreeks()
    {
        return BlackScholes.CalculateGreeks(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
    }

    [Benchmark]
    public double Delta()
    {
        return BlackScholes.Delta(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
    }

    [Benchmark]
    public double Gamma()
    {
        return BlackScholes.Gamma(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield);
    }

    [Benchmark]
    public double CalculateImpliedVolatility()
    {
        var targetPrice = BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        
        return ImpliedVolatility.Solve(targetPrice, Spot, Strike, TimeToExpiry,
            RiskFreeRate, DividendYield, OptionType.Call);
    }

    [Benchmark]
    [Arguments(1000)]
    public void BatchPricing(int count)
    {
        for (int i = 0; i < count; i++)
        {
            var strike = 95.0 + i * 0.01;
            BlackScholes.Price(Spot, strike, TimeToExpiry, Volatility,
                RiskFreeRate, DividendYield, OptionType.Call);
        }
    }
}
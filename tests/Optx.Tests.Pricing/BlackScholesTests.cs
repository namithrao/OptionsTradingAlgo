using Optx.Core.Types;
using Optx.Pricing;
using Xunit;

namespace Optx.Tests.Pricing;

public class BlackScholesTests
{
    private const double Tolerance = 1e-6;
    private const double Spot = 100.0;
    private const double Strike = 105.0;
    private const double TimeToExpiry = 0.25; // 3 months
    private const double Volatility = 0.2; // 20%
    private const double RiskFreeRate = 0.05; // 5%
    private const double DividendYield = 0.01; // 1%

    [Fact]
    public void CallPrice_ShouldBePositive()
    {
        var price = BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);

        Assert.True(price > 0, "Call price should be positive");
    }

    [Fact]
    public void PutPrice_ShouldBePositive()
    {
        var price = BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Put);

        Assert.True(price > 0, "Put price should be positive");
    }

    [Fact]
    public void PutCallParity_ShouldHold()
    {
        var callPrice = BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        var putPrice = BlackScholes.Price(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Put);

        var parityDifference = BlackScholes.PutCallParity(callPrice, putPrice, Spot, Strike,
            TimeToExpiry, RiskFreeRate, DividendYield);

        Assert.True(Math.Abs(parityDifference) < Tolerance, 
            $"Put-call parity violation: {parityDifference}");
    }

    [Fact]
    public void CallDelta_ShouldBeBetweenZeroAndOne()
    {
        var delta = BlackScholes.Delta(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);

        Assert.InRange(delta, 0.0, 1.0);
    }

    [Fact]
    public void PutDelta_ShouldBeBetweenMinusOneAndZero()
    {
        var delta = BlackScholes.Delta(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Put);

        Assert.InRange(delta, -1.0, 0.0);
    }

    [Fact]
    public void Gamma_ShouldBePositive()
    {
        var gamma = BlackScholes.Gamma(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield);

        Assert.True(gamma > 0, "Gamma should be positive");
    }

    [Fact]
    public void Vega_ShouldBePositive()
    {
        var vega = BlackScholes.Vega(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield);

        Assert.True(vega > 0, "Vega should be positive");
    }

    [Fact]
    public void CallTheta_ShouldBeNegative()
    {
        var theta = BlackScholes.Theta(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);

        Assert.True(theta < 0, "Call theta should be negative (time decay)");
    }

    [Fact]
    public void GreeksConsistency_AllMethodsShouldMatch()
    {
        var allGreeks = BlackScholes.CalculateGreeks(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);

        var individualDelta = BlackScholes.Delta(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        var individualGamma = BlackScholes.Gamma(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield);
        var individualTheta = BlackScholes.Theta(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        var individualVega = BlackScholes.Vega(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield);
        var individualRho = BlackScholes.Rho(Spot, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);

        Assert.True(Math.Abs(allGreeks.Delta - individualDelta) < Tolerance, "Delta mismatch");
        Assert.True(Math.Abs(allGreeks.Gamma - individualGamma) < Tolerance, "Gamma mismatch");
        Assert.True(Math.Abs(allGreeks.Theta - individualTheta) < Tolerance, "Theta mismatch");
        Assert.True(Math.Abs(allGreeks.Vega - individualVega) < Tolerance, "Vega mismatch");
        Assert.True(Math.Abs(allGreeks.Rho - individualRho) < Tolerance, "Rho mismatch");
    }

    [Theory]
    [InlineData(80.0)] // Deep ITM
    [InlineData(100.0)] // ATM
    [InlineData(120.0)] // OTM
    public void Price_ShouldHandleDifferentMoneyness(double spotPrice)
    {
        var callPrice = BlackScholes.Price(spotPrice, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        var putPrice = BlackScholes.Price(spotPrice, Strike, TimeToExpiry, Volatility,
            RiskFreeRate, DividendYield, OptionType.Put);

        Assert.True(callPrice >= 0, $"Call price should be non-negative for spot {spotPrice}");
        Assert.True(putPrice >= 0, $"Put price should be non-negative for spot {spotPrice}");
    }

    [Fact]
    public void Price_AtExpiry_ShouldEqualIntrinsic()
    {
        const double zeroTime = 1e-10; // Very close to expiry
        
        // ITM call
        var itmCallPrice = BlackScholes.Price(110.0, Strike, zeroTime, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        var expectedIntrinsic = 110.0 - Strike;
        
        Assert.True(Math.Abs(itmCallPrice - expectedIntrinsic) < 0.01, 
            "ITM call should approach intrinsic value at expiry");
        
        // OTM call
        var otmCallPrice = BlackScholes.Price(95.0, Strike, zeroTime, Volatility,
            RiskFreeRate, DividendYield, OptionType.Call);
        
        Assert.True(otmCallPrice < 0.01, "OTM call should be nearly worthless at expiry");
    }
}
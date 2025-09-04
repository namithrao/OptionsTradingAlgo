using System.Runtime.CompilerServices;

namespace Optx.Core.Types;

/// <summary>
/// Option contract specification
/// </summary>
public readonly record struct OptionContract(
    ReadOnlyMemory<char> Symbol,
    ReadOnlyMemory<char> UnderlyingSymbol,
    decimal Strike,
    DateTime Expiry,
    OptionType OptionType)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetTimeToExpiry(DateTime currentTime)
    {
        var timeSpan = Expiry - currentTime;
        return Math.Max(0.0, timeSpan.TotalDays / 365.25);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetMoneyness(decimal underlyingPrice)
    {
        return (double)(underlyingPrice / Strike);
    }

    public bool IsCall => OptionType == OptionType.Call;

    public bool IsPut => OptionType == OptionType.Put;
}

/// <summary>
/// Option type enumeration
/// </summary>
public enum OptionType : byte
{
    Call = 0,
    Put = 1
}

/// <summary>
/// Option quote with Greeks
/// </summary>
public readonly record struct OptionQuote(
    OptionContract Contract,
    decimal BidPrice,
    int BidSize,
    decimal AskPrice,
    int AskSize,
    double ImpliedVolatility,
    Greeks Greeks)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetMidPrice() => (BidPrice + AskPrice) * 0.5m;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetSpread() => AskPrice - BidPrice;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double GetSpreadPercent() => (double)((AskPrice - BidPrice) / GetMidPrice() * 100m);
}

/// <summary>
/// Option Greeks
/// </summary>
public readonly struct Greeks
{
    public double Delta { get; }
    public double Gamma { get; }
    public double Theta { get; }
    public double Vega { get; }
    public double Rho { get; }

    public Greeks(double delta, double gamma, double theta, double vega, double rho)
    {
        Delta = delta;
        Gamma = gamma;
        Theta = theta;
        Vega = vega;
        Rho = rho;
    }
    public static readonly Greeks Zero = new(0.0, 0.0, 0.0, 0.0, 0.0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Greeks operator +(Greeks left, Greeks right)
    {
        return new Greeks(
            left.Delta + right.Delta,
            left.Gamma + right.Gamma,
            left.Theta + right.Theta,
            left.Vega + right.Vega,
            left.Rho + right.Rho);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Greeks operator -(Greeks left, Greeks right)
    {
        return new Greeks(
            left.Delta - right.Delta,
            left.Gamma - right.Gamma,
            left.Theta - right.Theta,
            left.Vega - right.Vega,
            left.Rho - right.Rho);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Greeks operator *(Greeks greeks, double scalar)
    {
        return new Greeks(
            greeks.Delta * scalar,
            greeks.Gamma * scalar,
            greeks.Theta * scalar,
            greeks.Vega * scalar,
            greeks.Rho * scalar);
    }
}

/// <summary>
/// Position in an option or underlying
/// </summary>
public readonly record struct Position(
    ReadOnlyMemory<char> Symbol,
    int Quantity,
    decimal AveragePrice,
    decimal CurrentPrice,
    Greeks Greeks = default)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetMarketValue() => Quantity * CurrentPrice;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal GetUnrealizedPnL() => Quantity * (CurrentPrice - AveragePrice);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Greeks GetPortfolioGreeks() => Greeks * Quantity;

    public bool IsLong => Quantity > 0;

    public bool IsShort => Quantity < 0;

    public bool IsFlat => Quantity == 0;
}
using System.Runtime.CompilerServices;

namespace Optx.Core.Utils;

/// <summary>
/// High-performance time utilities
/// </summary>
public static class TimeUtils
{
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    
    /// <summary>
    /// Convert DateTime to nanoseconds since Unix epoch
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong ToNanoseconds(DateTime dateTime)
    {
        var ticks = dateTime.ToUniversalTime().Ticks - UnixEpoch.Ticks;
        return (ulong)(ticks * 100); // Convert from 100ns ticks to nanoseconds
    }

    /// <summary>
    /// Convert nanoseconds since Unix epoch to DateTime
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime FromNanoseconds(ulong nanoseconds)
    {
        var ticks = (long)(nanoseconds / 100); // Convert from nanoseconds to 100ns ticks
        return UnixEpoch.AddTicks(ticks);
    }

    /// <summary>
    /// Get current timestamp in nanoseconds since Unix epoch
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong GetCurrentNanoseconds()
    {
        return ToNanoseconds(DateTime.UtcNow);
    }

    /// <summary>
    /// Calculate time to expiry in years
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetTimeToExpiry(DateTime current, DateTime expiry)
    {
        if (expiry <= current) return 0.0;
        return (expiry - current).TotalDays / 365.25;
    }

    /// <summary>
    /// Calculate time to expiry in years from nanoseconds
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double GetTimeToExpiry(ulong currentNs, ulong expiryNs)
    {
        if (expiryNs <= currentNs) return 0.0;
        var diffNs = expiryNs - currentNs;
        var diffSeconds = diffNs / 1_000_000_000.0;
        return diffSeconds / (365.25 * 24 * 3600);
    }

    /// <summary>
    /// Check if market is open (simplified NYSE hours)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMarketOpen(DateTime dateTime)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var easternTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime.ToUniversalTime(), eastern);
        
        if (easternTime.DayOfWeek == DayOfWeek.Saturday || easternTime.DayOfWeek == DayOfWeek.Sunday)
            return false;

        var marketOpen = new TimeSpan(9, 30, 0);
        var marketClose = new TimeSpan(16, 0, 0);
        
        return easternTime.TimeOfDay >= marketOpen && easternTime.TimeOfDay < marketClose;
    }

    /// <summary>
    /// Get next market open time
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTime GetNextMarketOpen(DateTime dateTime)
    {
        var eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var easternTime = TimeZoneInfo.ConvertTimeFromUtc(dateTime.ToUniversalTime(), eastern);
        
        var marketOpen = new TimeSpan(9, 30, 0);
        var nextOpen = easternTime.Date.Add(marketOpen);
        
        if (easternTime.TimeOfDay >= marketOpen || 
            easternTime.DayOfWeek == DayOfWeek.Saturday || 
            easternTime.DayOfWeek == DayOfWeek.Sunday)
        {
            do
            {
                nextOpen = nextOpen.AddDays(1);
            }
            while (nextOpen.DayOfWeek == DayOfWeek.Saturday || nextOpen.DayOfWeek == DayOfWeek.Sunday);
        }
        
        return TimeZoneInfo.ConvertTimeToUtc(nextOpen, eastern);
    }

    /// <summary>
    /// Calculate business days between two dates
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetBusinessDays(DateTime start, DateTime end)
    {
        if (start > end) return 0;
        
        int businessDays = 0;
        var current = start.Date;
        
        while (current <= end.Date)
        {
            if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                businessDays++;
            current = current.AddDays(1);
        }
        
        return businessDays;
    }
}
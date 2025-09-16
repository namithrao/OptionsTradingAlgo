using System.Text.Json.Serialization;

namespace Optx.Web.Models;

/// <summary>
/// Polygon API response models for historical data
/// </summary>

public class PolygonAggregatesResponse
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("adjusted")]
    public bool Adjusted { get; set; }

    [JsonPropertyName("queryCount")]
    public int QueryCount { get; set; }

    [JsonPropertyName("resultsCount")]
    public int ResultsCount { get; set; }

    [JsonPropertyName("results")]
    public List<PolygonAggregate> Results { get; set; } = new();

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; set; }
}

public class PolygonAggregate
{
    [JsonPropertyName("c")]
    public decimal Close { get; set; }

    [JsonPropertyName("h")]
    public decimal High { get; set; }

    [JsonPropertyName("l")]
    public decimal Low { get; set; }

    [JsonPropertyName("n")]
    public int NumberOfTransactions { get; set; }

    [JsonPropertyName("o")]
    public decimal Open { get; set; }

    [JsonPropertyName("t")]
    public long Timestamp { get; set; }

    [JsonPropertyName("v")]
    public decimal Volume { get; set; }

    [JsonPropertyName("vw")]
    public decimal VolumeWeightedAveragePrice { get; set; }
}

public class PolygonOptionsContractsResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("results")]
    public List<PolygonOptionsContract> Results { get; set; } = new();

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; set; }
}

public class PolygonOptionsContract
{
    [JsonPropertyName("ticker")]
    public string Ticker { get; set; } = string.Empty;

    [JsonPropertyName("underlying_ticker")]
    public string UnderlyingTicker { get; set; } = string.Empty;

    [JsonPropertyName("contract_type")]
    public string ContractType { get; set; } = string.Empty;

    [JsonPropertyName("exercise_style")]
    public string ExerciseStyle { get; set; } = string.Empty;

    [JsonPropertyName("expiration_date")]
    public string ExpirationDate { get; set; } = string.Empty;

    [JsonPropertyName("primary_exchange")]
    public string PrimaryExchange { get; set; } = string.Empty;

    [JsonPropertyName("shares_per_contract")]
    public decimal SharesPerContract { get; set; }

    [JsonPropertyName("strike_price")]
    public decimal StrikePrice { get; set; }

    [JsonPropertyName("additional_underlyings")]
    public List<object>? AdditionalUnderlyings { get; set; }
}

public class PolygonQuotesResponse
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("request_id")]
    public string RequestId { get; set; } = string.Empty;

    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("results")]
    public List<PolygonQuote> Results { get; set; } = new();

    [JsonPropertyName("next_url")]
    public string? NextUrl { get; set; }
}

public class PolygonQuote
{
    [JsonPropertyName("participant_timestamp")]
    public long ParticipantTimestamp { get; set; }

    [JsonPropertyName("conditions")]
    public List<int>? Conditions { get; set; }

    [JsonPropertyName("indicators")]
    public List<int>? Indicators { get; set; }

    [JsonPropertyName("bid")]
    public decimal Bid { get; set; }

    [JsonPropertyName("bid_size")]
    public int BidSize { get; set; }

    [JsonPropertyName("ask")]
    public decimal Ask { get; set; }

    [JsonPropertyName("ask_size")]
    public int AskSize { get; set; }

    [JsonPropertyName("exchange")]
    public int Exchange { get; set; }

    [JsonPropertyName("sip_timestamp")]
    public long SipTimestamp { get; set; }
}

/// <summary>
/// Rate limiting information for API calls
/// </summary>
public class RateLimitInfo
{
    public DateTime LastCallTime { get; set; }
    public int CallsInCurrentMinute { get; set; }
    public int MaxCallsPerMinute { get; set; } = 5;

    public TimeSpan GetDelayUntilNextCall()
    {
        var now = DateTime.UtcNow;
        var minuteStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        if (LastCallTime < minuteStart)
        {
            CallsInCurrentMinute = 0;
        }

        if (CallsInCurrentMinute >= MaxCallsPerMinute)
        {
            var nextMinute = minuteStart.AddMinutes(1);
            return nextMinute - now;
        }

        return TimeSpan.Zero;
    }

    public void RecordCall()
    {
        var now = DateTime.UtcNow;
        var minuteStart = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        if (LastCallTime < minuteStart)
        {
            CallsInCurrentMinute = 0;
        }

        CallsInCurrentMinute++;
        LastCallTime = now;
    }
}

/// <summary>
/// Historical data request parameters
/// </summary>
public class HistoricalDataRequest
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public int Multiplier { get; set; } = 1;
    public string Timespan { get; set; } = "day"; // minute, hour, day, week, month, quarter, year
    public bool Adjusted { get; set; } = true;
    public string Sort { get; set; } = "asc";
    public int Limit { get; set; } = 50000;
}

/// <summary>
/// Options contracts request parameters
/// </summary>
public class OptionsContractsRequest
{
    public string UnderlyingTicker { get; set; } = string.Empty;
    public string? ContractType { get; set; } // call, put
    public DateTime? ExpirationDate { get; set; }
    public decimal? StrikePrice { get; set; }
    public bool Expired { get; set; } = false;
    public DateTime? AsOf { get; set; } // Point in time for contracts as of this date
    public string Sort { get; set; } = "ticker";
    public string Order { get; set; } = "asc";
    public int Limit { get; set; } = 1000;
}
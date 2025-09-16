using System.Text.Json;
using System.Web;
using Optx.Core.Events;
using Optx.Core.Types;
using Optx.Web.Models;

namespace Optx.Web.Services;

public interface IHistoricalDataService
{
    Task<List<MarketTick>> GetHistoricalStockDataAsync(HistoricalDataRequest request, CancellationToken cancellationToken = default);
    Task<List<MarketTick>> GetHistoricalOptionsDataAsync(HistoricalDataRequest request, CancellationToken cancellationToken = default);
    Task<List<QuoteUpdate>> GetHistoricalOptionsQuotesAsync(string optionsTicker, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task<List<PolygonOptionsContract>> GetOptionsContractsAsync(OptionsContractsRequest request, CancellationToken cancellationToken = default);
    Task<List<PolygonOptionsContract>> GetOptionsChainAsync(string underlyingTicker, DateTime expirationDate, CancellationToken cancellationToken = default);
}

public class PolygonHistoricalDataService : IHistoricalDataService, IDisposable
{
    private readonly ILogger<PolygonHistoricalDataService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private readonly RateLimitInfo _rateLimitInfo;
    private readonly SemaphoreSlim _rateLimitSemaphore = new(1, 1);

    public PolygonHistoricalDataService(
        ILogger<PolygonHistoricalDataService> logger,
        IConfiguration configuration,
        HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
        _rateLimitInfo = new RateLimitInfo();
    }

    public async Task<List<MarketTick>> GetHistoricalStockDataAsync(
        HistoricalDataRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching historical stock data for {Symbol} from {From} to {To}",
            request.Symbol, request.FromDate, request.ToDate);

        var url = BuildAggregatesUrl(request);
        var response = await MakeRateLimitedRequestAsync<PolygonAggregatesResponse>(url, cancellationToken);

        if (response?.Results == null)
        {
            _logger.LogWarning("No results returned for {Symbol}", request.Symbol);
            return new List<MarketTick>();
        }

        var ticks = new List<MarketTick>();
        foreach (var aggregate in response.Results)
        {
            // Convert Polygon timestamp (milliseconds) to nanoseconds
            var timestampNs = (ulong)(aggregate.Timestamp * 1_000_000);

            // Create market tick for each price point (using close price)
            var tick = new MarketTick(
                timestampNs,
                request.Symbol.AsMemory(),
                aggregate.Close,
                (int)Math.Min(aggregate.Volume, int.MaxValue), // Cap volume at int.MaxValue
                MarketDataType.Trade);

            ticks.Add(tick);
        }

        _logger.LogInformation("Retrieved {Count} historical data points for {Symbol}", ticks.Count, request.Symbol);
        return ticks;
    }

    public async Task<List<MarketTick>> GetHistoricalOptionsDataAsync(
        HistoricalDataRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching historical options data for {Symbol} from {From} to {To}",
            request.Symbol, request.FromDate, request.ToDate);

        var url = BuildAggregatesUrl(request);
        var response = await MakeRateLimitedRequestAsync<PolygonAggregatesResponse>(url, cancellationToken);

        if (response?.Results == null)
        {
            _logger.LogWarning("No results returned for options {Symbol}", request.Symbol);
            return new List<MarketTick>();
        }

        var ticks = new List<MarketTick>();
        foreach (var aggregate in response.Results)
        {
            var timestampNs = (ulong)(aggregate.Timestamp * 1_000_000);

            var tick = new MarketTick(
                timestampNs,
                request.Symbol.AsMemory(),
                aggregate.Close,
                (int)Math.Min(aggregate.Volume, int.MaxValue), // Cap volume at int.MaxValue
                MarketDataType.Trade);

            ticks.Add(tick);
        }

        _logger.LogInformation("Retrieved {Count} historical options data points for {Symbol}", ticks.Count, request.Symbol);
        return ticks;
    }

    public async Task<List<QuoteUpdate>> GetHistoricalOptionsQuotesAsync(
        string optionsTicker,
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching historical options quotes for {Ticker} from {From} to {To}",
            optionsTicker, from, to);

        var apiKey = GetApiKey();
        var baseUrl = _configuration["Polygon:RestApiUrl"] ?? "https://api.polygon.io";
        
        var fromStr = from.ToString("yyyy-MM-dd");
        var toStr = to.ToString("yyyy-MM-dd");
        
        var url = $"{baseUrl}/v3/quotes/{optionsTicker}?timestamp.gte={fromStr}&timestamp.lte={toStr}&limit=50000&apikey={apiKey}";

        var response = await MakeRateLimitedRequestAsync<PolygonQuotesResponse>(url, cancellationToken);

        if (response?.Results == null)
        {
            _logger.LogWarning("No quote results returned for options {Ticker}", optionsTicker);
            return new List<QuoteUpdate>();
        }

        var quotes = new List<QuoteUpdate>();
        foreach (var quote in response.Results)
        {
            var timestampNs = (ulong)(quote.SipTimestamp * 1_000_000);

            var quoteUpdate = new QuoteUpdate(
                timestampNs,
                optionsTicker.AsMemory(),
                quote.Bid,
                quote.BidSize,
                quote.Ask,
                quote.AskSize);

            quotes.Add(quoteUpdate);
        }

        _logger.LogInformation("Retrieved {Count} historical quotes for {Ticker}", quotes.Count, optionsTicker);
        return quotes;
    }

    public async Task<List<PolygonOptionsContract>> GetOptionsContractsAsync(
        OptionsContractsRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching options contracts for {UnderlyingTicker}", request.UnderlyingTicker);

        var apiKey = GetApiKey();
        var baseUrl = _configuration["Polygon:RestApiUrl"] ?? "https://api.polygon.io";

        var queryParams = new List<string>
        {
            $"underlying_ticker={request.UnderlyingTicker}",
            $"limit={request.Limit}",
            $"sort={request.Sort}",
            $"order={request.Order}",
            $"apikey={apiKey}"
        };

        if (!string.IsNullOrEmpty(request.ContractType))
            queryParams.Add($"contract_type={request.ContractType}");

        if (request.ExpirationDate.HasValue)
            queryParams.Add($"expiration_date={request.ExpirationDate.Value:yyyy-MM-dd}");

        if (request.StrikePrice.HasValue)
            queryParams.Add($"strike_price={request.StrikePrice.Value}");

        if (request.AsOf.HasValue)
            queryParams.Add($"as_of={request.AsOf.Value:yyyy-MM-dd}");

        queryParams.Add($"expired={request.Expired.ToString().ToLower()}");

        var url = $"{baseUrl}/v3/reference/options/contracts?{string.Join("&", queryParams)}";

        _logger.LogInformation("Making API request to: {Url}", url);
        
        var response = await MakeRateLimitedRequestAsync<PolygonOptionsContractsResponse>(url, cancellationToken);

        if (response?.Results == null)
        {
            _logger.LogWarning("No options contracts found for {UnderlyingTicker}", request.UnderlyingTicker);
            return new List<PolygonOptionsContract>();
        }

        _logger.LogInformation("Retrieved {Count} options contracts for {UnderlyingTicker}", 
            response.Results.Count, request.UnderlyingTicker);
        return response.Results;
    }

    public async Task<List<PolygonOptionsContract>> GetOptionsChainAsync(
        string underlyingTicker,
        DateTime expirationDate,
        CancellationToken cancellationToken = default)
    {
        var request = new OptionsContractsRequest
        {
            UnderlyingTicker = underlyingTicker,
            ExpirationDate = expirationDate,
            Limit = 1000
        };

        return await GetOptionsContractsAsync(request, cancellationToken);
    }

    private string BuildAggregatesUrl(HistoricalDataRequest request)
    {
        var apiKey = GetApiKey();
        var baseUrl = _configuration["Polygon:RestApiUrl"] ?? "https://api.polygon.io";
        
        var fromStr = request.FromDate.ToString("yyyy-MM-dd");
        var toStr = request.ToDate.ToString("yyyy-MM-dd");
        
        var queryParams = new Dictionary<string, string>
        {
            ["adjusted"] = request.Adjusted.ToString().ToLower(),
            ["sort"] = request.Sort,
            ["limit"] = request.Limit.ToString(),
            ["apikey"] = apiKey
        };

        var queryString = string.Join("&", queryParams.Select(kvp => $"{kvp.Key}={HttpUtility.UrlEncode(kvp.Value)}"));
        
        return $"{baseUrl}/v2/aggs/ticker/{request.Symbol}/range/{request.Multiplier}/{request.Timespan}/{fromStr}/{toStr}?{queryString}";
    }

    private async Task<T?> MakeRateLimitedRequestAsync<T>(string url, CancellationToken cancellationToken)
    {
        await _rateLimitSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            // Check rate limit and wait if necessary
            var delay = _rateLimitInfo.GetDelayUntilNextCall();
            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Rate limit reached, waiting {Delay} before next call", delay);
                await Task.Delay(delay, cancellationToken);
            }

            _logger.LogDebug("Making API call to: {Url}", url);
            
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("API call failed: {StatusCode} {ReasonPhrase} for URL: {Url}", 
                    response.StatusCode, response.ReasonPhrase, url);
                return default;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            
            _rateLimitInfo.RecordCall();
            
            var result = JsonSerializer.Deserialize<T>(json);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error making API request to {Url}", url);
            return default;
        }
        finally
        {
            _rateLimitSemaphore.Release();
        }
    }

    private string GetApiKey()
    {
        var apiKey = _configuration["Polygon:ApiKey"];
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("Polygon API key is required. Set Polygon:ApiKey in configuration.");
        }
        return apiKey;
    }

    public void Dispose()
    {
        _rateLimitSemaphore.Dispose();
    }
}
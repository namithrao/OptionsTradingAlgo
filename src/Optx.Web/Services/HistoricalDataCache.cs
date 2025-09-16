using System.Text.Json;
using Optx.Core.Types;
using Optx.Web.Models;

namespace Optx.Web.Services;

public interface IHistoricalDataCache
{
    Task<List<MarketTick>?> GetCachedStockDataAsync(string symbol, DateTime from, DateTime to);
    Task CacheStockDataAsync(string symbol, DateTime from, DateTime to, List<MarketTick> data);
    Task<List<MarketTick>?> GetCachedOptionsDataAsync(string symbol, DateTime from, DateTime to);
    Task CacheOptionsDataAsync(string symbol, DateTime from, DateTime to, List<MarketTick> data);
    Task<List<PolygonOptionsContract>?> GetCachedOptionsContractsAsync(string underlying, DateTime? expiration = null);
    Task CacheOptionsContractsAsync(string underlying, DateTime? expiration, List<PolygonOptionsContract> contracts);
    Task ClearExpiredCacheAsync();
}

public class HistoricalDataCache : IHistoricalDataCache
{
    private readonly ILogger<HistoricalDataCache> _logger;
    private readonly string _cacheDirectory;
    private readonly TimeSpan _cacheExpiry;

    public HistoricalDataCache(ILogger<HistoricalDataCache> logger, IConfiguration configuration)
    {
        _logger = logger;
        _cacheDirectory = configuration["Cache:Directory"] ?? Path.Combine(Directory.GetCurrentDirectory(), "cache");
        _cacheExpiry = TimeSpan.FromHours(configuration.GetValue("Cache:ExpiryHours", 24));
        
        // Ensure cache directory exists
        Directory.CreateDirectory(_cacheDirectory);
        Directory.CreateDirectory(Path.Combine(_cacheDirectory, "stocks"));
        Directory.CreateDirectory(Path.Combine(_cacheDirectory, "options"));
        Directory.CreateDirectory(Path.Combine(_cacheDirectory, "contracts"));
    }

    public async Task<List<MarketTick>?> GetCachedStockDataAsync(string symbol, DateTime from, DateTime to)
    {
        var cacheKey = GenerateStockCacheKey(symbol, from, to);
        var cacheFile = Path.Combine(_cacheDirectory, "stocks", $"{cacheKey}.json");
        
        if (!File.Exists(cacheFile))
        {
            _logger.LogDebug("Cache miss for stock data: {Symbol} {From} {To}", symbol, from, to);
            return null;
        }

        var fileInfo = new FileInfo(cacheFile);
        if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _cacheExpiry)
        {
            _logger.LogDebug("Cache expired for stock data: {Symbol} {From} {To}", symbol, from, to);
            File.Delete(cacheFile);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cacheFile);
            var cachedData = JsonSerializer.Deserialize<CachedMarketData>(json);
            
            if (cachedData?.Data == null)
            {
                return null;
            }

            _logger.LogDebug("Cache hit for stock data: {Symbol} {From} {To} - {Count} records", 
                symbol, from, to, cachedData.Data.Count);
            
            return ConvertFromCachedMarketTicks(cachedData.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading cached stock data for {Symbol}", symbol);
            return null;
        }
    }

    public async Task CacheStockDataAsync(string symbol, DateTime from, DateTime to, List<MarketTick> data)
    {
        var cacheKey = GenerateStockCacheKey(symbol, from, to);
        var cacheFile = Path.Combine(_cacheDirectory, "stocks", $"{cacheKey}.json");
        
        try
        {
            var cachedData = new CachedMarketData
            {
                Symbol = symbol,
                FromDate = from,
                ToDate = to,
                CachedAt = DateTime.UtcNow,
                Data = ConvertToCachedMarketTicks(data)
            };

            var json = JsonSerializer.Serialize(cachedData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
            
            _logger.LogDebug("Cached stock data: {Symbol} {From} {To} - {Count} records", 
                symbol, from, to, data.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching stock data for {Symbol}", symbol);
        }
    }

    public async Task<List<MarketTick>?> GetCachedOptionsDataAsync(string symbol, DateTime from, DateTime to)
    {
        var cacheKey = GenerateOptionsCacheKey(symbol, from, to);
        var cacheFile = Path.Combine(_cacheDirectory, "options", $"{cacheKey}.json");
        
        if (!File.Exists(cacheFile))
        {
            _logger.LogDebug("Cache miss for options data: {Symbol} {From} {To}", symbol, from, to);
            return null;
        }

        var fileInfo = new FileInfo(cacheFile);
        if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _cacheExpiry)
        {
            _logger.LogDebug("Cache expired for options data: {Symbol} {From} {To}", symbol, from, to);
            File.Delete(cacheFile);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cacheFile);
            var cachedData = JsonSerializer.Deserialize<CachedMarketData>(json);
            
            if (cachedData?.Data == null)
            {
                return null;
            }

            _logger.LogDebug("Cache hit for options data: {Symbol} {From} {To} - {Count} records", 
                symbol, from, to, cachedData.Data.Count);
            
            return ConvertFromCachedMarketTicks(cachedData.Data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading cached options data for {Symbol}", symbol);
            return null;
        }
    }

    public async Task CacheOptionsDataAsync(string symbol, DateTime from, DateTime to, List<MarketTick> data)
    {
        var cacheKey = GenerateOptionsCacheKey(symbol, from, to);
        var cacheFile = Path.Combine(_cacheDirectory, "options", $"{cacheKey}.json");
        
        try
        {
            var cachedData = new CachedMarketData
            {
                Symbol = symbol,
                FromDate = from,
                ToDate = to,
                CachedAt = DateTime.UtcNow,
                Data = ConvertToCachedMarketTicks(data)
            };

            var json = JsonSerializer.Serialize(cachedData, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
            
            _logger.LogDebug("Cached options data: {Symbol} {From} {To} - {Count} records", 
                symbol, from, to, data.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching options data for {Symbol}", symbol);
        }
    }

    public async Task<List<PolygonOptionsContract>?> GetCachedOptionsContractsAsync(string underlying, DateTime? expiration = null)
    {
        var cacheKey = GenerateContractsCacheKey(underlying, expiration);
        var cacheFile = Path.Combine(_cacheDirectory, "contracts", $"{cacheKey}.json");
        
        if (!File.Exists(cacheFile))
        {
            _logger.LogDebug("Cache miss for options contracts: {Underlying} {Expiration}", underlying, expiration);
            return null;
        }

        var fileInfo = new FileInfo(cacheFile);
        if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _cacheExpiry)
        {
            _logger.LogDebug("Cache expired for options contracts: {Underlying} {Expiration}", underlying, expiration);
            File.Delete(cacheFile);
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(cacheFile);
            var contracts = JsonSerializer.Deserialize<List<PolygonOptionsContract>>(json);
            
            _logger.LogDebug("Cache hit for options contracts: {Underlying} {Expiration} - {Count} contracts", 
                underlying, expiration, contracts?.Count ?? 0);
            
            return contracts ?? new List<PolygonOptionsContract>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading cached options contracts for {Underlying}", underlying);
            return null;
        }
    }

    public async Task CacheOptionsContractsAsync(string underlying, DateTime? expiration, List<PolygonOptionsContract> contracts)
    {
        var cacheKey = GenerateContractsCacheKey(underlying, expiration);
        var cacheFile = Path.Combine(_cacheDirectory, "contracts", $"{cacheKey}.json");
        
        try
        {
            var json = JsonSerializer.Serialize(contracts, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);
            
            _logger.LogDebug("Cached options contracts: {Underlying} {Expiration} - {Count} contracts", 
                underlying, expiration, contracts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching options contracts for {Underlying}", underlying);
        }
    }

    public async Task ClearExpiredCacheAsync()
    {
        try
        {
            var directories = new[] { "stocks", "options", "contracts" };
            
            foreach (var dir in directories)
            {
                var dirPath = Path.Combine(_cacheDirectory, dir);
                if (!Directory.Exists(dirPath)) continue;
                
                var files = Directory.GetFiles(dirPath, "*.json");
                var expiredCount = 0;
                
                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file);
                    if (DateTime.UtcNow - fileInfo.LastWriteTimeUtc > _cacheExpiry)
                    {
                        File.Delete(file);
                        expiredCount++;
                    }
                }
                
                if (expiredCount > 0)
                {
                    _logger.LogInformation("Cleared {Count} expired cache files from {Directory}", expiredCount, dir);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing expired cache");
        }
    }

    private string GenerateStockCacheKey(string symbol, DateTime from, DateTime to)
    {
        return $"{symbol}_{from:yyyyMMdd}_{to:yyyyMMdd}_stock";
    }

    private string GenerateOptionsCacheKey(string symbol, DateTime from, DateTime to)
    {
        return $"{symbol}_{from:yyyyMMdd}_{to:yyyyMMdd}_options";
    }

    private string GenerateContractsCacheKey(string underlying, DateTime? expiration)
    {
        var expirationStr = expiration?.ToString("yyyyMMdd") ?? "all";
        return $"{underlying}_{expirationStr}_contracts";
    }

    private List<CachedMarketTick> ConvertToCachedMarketTicks(List<MarketTick> ticks)
    {
        return ticks.Select(t => new CachedMarketTick
        {
            TimestampNs = t.TimestampNs,
            Symbol = t.Symbol.ToString(),
            Price = t.Price,
            Quantity = t.Quantity,
            Type = t.Type.ToString()
        }).ToList();
    }

    private List<MarketTick> ConvertFromCachedMarketTicks(List<CachedMarketTick> cachedTicks)
    {
        return cachedTicks.Select(ct => new MarketTick(
            ct.TimestampNs,
            ct.Symbol.AsMemory(),
            ct.Price,
            ct.Quantity,
            Enum.Parse<MarketDataType>(ct.Type)
        )).ToList();
    }
}

public class CachedMarketData
{
    public string Symbol { get; set; } = string.Empty;
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public DateTime CachedAt { get; set; }
    public List<CachedMarketTick> Data { get; set; } = new();
}

public class CachedMarketTick
{
    public ulong TimestampNs { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; }
    public string Type { get; set; } = string.Empty;
}
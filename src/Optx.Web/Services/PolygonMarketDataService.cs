using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Optx.Core.Events;
using Optx.Core.Types;

namespace Optx.Web.Services;

public interface IMarketDataService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    IAsyncEnumerable<MarketEvent> GetLiveEventsAsync(CancellationToken cancellationToken = default);
    Task SubscribeToSymbolAsync(string symbol);
    Task UnsubscribeFromSymbolAsync(string symbol);
    Task<decimal?> GetCurrentStockPriceAsync(string symbol);
}

public class PolygonMarketDataService : IMarketDataService, IDisposable
{
    private readonly ILogger<PolygonMarketDataService> _logger;
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);
    private readonly HashSet<string> _subscribedSymbols = new();
    private readonly Channel<MarketEvent> _eventChannel;
    private readonly ChannelWriter<MarketEvent> _eventWriter;
    private readonly ChannelReader<MarketEvent> _eventReader;

    public PolygonMarketDataService(ILogger<PolygonMarketDataService> logger, IConfiguration configuration, HttpClient httpClient)
    {
        _logger = logger;
        _configuration = configuration;
        _httpClient = httpClient;
        
        var channel = Channel.CreateUnbounded<MarketEvent>();
        _eventChannel = channel;
        _eventWriter = channel.Writer;
        _eventReader = channel.Reader;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            if (_webSocket != null && _webSocket.State == WebSocketState.Open)
            {
                _logger.LogInformation("WebSocket already connected");
                return;
            }

            var apiKey = _configuration["Polygon:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Polygon API key not found in configuration");
                throw new InvalidOperationException("Polygon API key is required");
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _webSocket = new ClientWebSocket();

            var wsUrl = $"wss://socket.polygon.io/options";
            await _webSocket.ConnectAsync(new Uri(wsUrl), cancellationToken);

            _logger.LogInformation("Connected to Polygon WebSocket");

            // Authenticate
            var authMessage = new { action = "auth", @params = apiKey };
            var authJson = JsonSerializer.Serialize(authMessage);
            var authBytes = Encoding.UTF8.GetBytes(authJson);
            await _webSocket.SendAsync(new ArraySegment<byte>(authBytes), WebSocketMessageType.Text, true, cancellationToken);

            // Start receiving messages
            _ = Task.Run(() => ReceiveMessagesAsync(_cancellationTokenSource.Token), cancellationToken);
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _connectionSemaphore.WaitAsync(cancellationToken);
        
        try
        {
            _cancellationTokenSource?.Cancel();

            if (_webSocket?.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Service stopping", cancellationToken);
            }

            _webSocket?.Dispose();
            _webSocket = null;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
        finally
        {
            _connectionSemaphore.Release();
        }
    }

    public async Task SubscribeToSymbolAsync(string symbol)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("Cannot subscribe - WebSocket not connected");
            return;
        }

        lock (_subscribedSymbols)
        {
            if (_subscribedSymbols.Contains(symbol))
            {
                _logger.LogInformation("Already subscribed to {Symbol}", symbol);
                return;
            }
            _subscribedSymbols.Add(symbol);
        }

        // Subscribe to option quotes and trades
        var subscribeMessage = new 
        { 
            action = "subscribe", 
            @params = $"OQ.{symbol}*,OT.{symbol}*" // Options quotes and trades
        };
        
        var subscribeJson = JsonSerializer.Serialize(subscribeMessage);
        var subscribeBytes = Encoding.UTF8.GetBytes(subscribeJson);
        
        await _webSocket.SendAsync(new ArraySegment<byte>(subscribeBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        
        _logger.LogInformation("Subscribed to options data for {Symbol}", symbol);
    }

    public async Task UnsubscribeFromSymbolAsync(string symbol)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        lock (_subscribedSymbols)
        {
            if (!_subscribedSymbols.Remove(symbol))
            {
                return;
            }
        }

        var unsubscribeMessage = new 
        { 
            action = "unsubscribe", 
            @params = $"OQ.{symbol}*,OT.{symbol}*"
        };
        
        var unsubscribeJson = JsonSerializer.Serialize(unsubscribeMessage);
        var unsubscribeBytes = Encoding.UTF8.GetBytes(unsubscribeJson);
        
        await _webSocket.SendAsync(new ArraySegment<byte>(unsubscribeBytes), WebSocketMessageType.Text, true, CancellationToken.None);
        
        _logger.LogInformation("Unsubscribed from options data for {Symbol}", symbol);
    }

    public async IAsyncEnumerable<MarketEvent> GetLiveEventsAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var marketEvent in _eventReader.ReadAllAsync(cancellationToken))
        {
            yield return marketEvent;
        }
    }

    private async Task ReceiveMessagesAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        var messageBuffer = new StringBuilder();

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    messageBuffer.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        var message = messageBuffer.ToString();
                        messageBuffer.Clear();

                        try
                        {
                            await ProcessMessageAsync(message);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error processing message: {Message}", message);
                        }
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("WebSocket closed by server");
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WebSocket receive cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in WebSocket receive loop");
        }
    }

    private async Task ProcessMessageAsync(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            var elements = document.RootElement.EnumerateArray();

            foreach (var element in elements)
            {
                if (element.TryGetProperty("ev", out var eventType))
                {
                    var eventTypeString = eventType.GetString();
                    
                    switch (eventTypeString)
                    {
                        case "OQ": // Options quote
                            await ProcessOptionsQuoteAsync(element);
                            break;
                        case "OT": // Options trade
                            await ProcessOptionsTradeAsync(element);
                            break;
                        case "status":
                            ProcessStatusMessage(element);
                            break;
                    }
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse JSON message: {Message}", message);
        }
    }

    private async Task ProcessOptionsQuoteAsync(JsonElement element)
    {
        try
        {
            var symbol = element.GetProperty("sym").GetString();
            var timestamp = element.GetProperty("t").GetUInt64() * 1_000_000; // Convert to nanoseconds
            var bidPrice = element.TryGetProperty("bp", out var bp) ? bp.GetDecimal() : 0m;
            var bidSize = element.TryGetProperty("bs", out var bs) ? bs.GetInt32() : 0;
            var askPrice = element.TryGetProperty("ap", out var ap) ? ap.GetDecimal() : 0m;
            var askSize = element.TryGetProperty("as", out var asv) ? asv.GetInt32() : 0;

            if (!string.IsNullOrEmpty(symbol))
            {
                var quoteUpdate = new QuoteUpdate(
                    timestamp,
                    symbol.AsMemory(),
                    bidPrice,
                    bidSize,
                    askPrice,
                    askSize
                );

                var marketEvent = new MarketEvent(quoteUpdate);
                
                if (!_eventWriter.TryWrite(marketEvent))
                {
                    _logger.LogWarning("Failed to write quote event to channel");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing options quote");
        }
    }

    private async Task ProcessOptionsTradeAsync(JsonElement element)
    {
        try
        {
            var symbol = element.GetProperty("sym").GetString();
            var timestamp = element.GetProperty("t").GetUInt64() * 1_000_000; // Convert to nanoseconds
            var price = element.GetProperty("p").GetDecimal();
            var size = element.GetProperty("s").GetInt32();

            if (!string.IsNullOrEmpty(symbol))
            {
                var marketTick = new MarketTick(
                    timestamp,
                    symbol.AsMemory(),
                    price,
                    size,
                    MarketDataType.Trade
                );

                var marketEvent = new MarketEvent(marketTick);
                
                if (!_eventWriter.TryWrite(marketEvent))
                {
                    _logger.LogWarning("Failed to write trade event to channel");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing options trade");
        }
    }

    private void ProcessStatusMessage(JsonElement element)
    {
        if (element.TryGetProperty("status", out var status) && 
            element.TryGetProperty("message", out var message))
        {
            var statusString = status.GetString();
            var messageString = message.GetString();
            
            _logger.LogInformation("Polygon status: {Status} - {Message}", statusString, messageString);
        }
    }

    public async Task<decimal?> GetCurrentStockPriceAsync(string symbol)
    {
        try
        {
            var apiKey = _configuration["Polygon:ApiKey"];
            if (string.IsNullOrEmpty(apiKey))
            {
                _logger.LogError("Polygon API key not found in configuration");
                return null;
            }

            var baseUrl = _configuration["Polygon:RestApiUrl"] ?? "https://api.polygon.io";
            var url = $"{baseUrl}/v2/aggs/ticker/{symbol}/prev?adjusted=true&apikey={apiKey}";
            
            _logger.LogDebug("Fetching stock price for {Symbol} from {Url}", symbol, url);
            
            var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to fetch stock price for {Symbol}: {StatusCode} {ReasonPhrase}", 
                    symbol, response.StatusCode, response.ReasonPhrase);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
            {
                var resultsArray = results.EnumerateArray();
                var firstResult = resultsArray.FirstOrDefault();
                
                if (firstResult.ValueKind != JsonValueKind.Undefined && 
                    firstResult.TryGetProperty("c", out var closePrice))
                {
                    var price = closePrice.GetDecimal();
                    _logger.LogDebug("Retrieved stock price for {Symbol}: ${Price}", symbol, price);
                    return price;
                }
            }

            _logger.LogWarning("No price data found for {Symbol}", symbol);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching stock price for {Symbol}", symbol);
            return null;
        }
    }

    public void Dispose()
    {
        _eventWriter.Complete();
        StopAsync().GetAwaiter().GetResult();
        _connectionSemaphore.Dispose();
    }
}
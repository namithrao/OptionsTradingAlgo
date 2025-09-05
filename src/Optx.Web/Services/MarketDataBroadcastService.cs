using Microsoft.AspNetCore.SignalR;
using Optx.Core.Events;
using Optx.Web.Hubs;
using System.Threading.Channels;

namespace Optx.Web.Services;

public class MarketDataBroadcastService : BackgroundService
{
    private readonly IMarketDataService _marketDataService;
    private readonly IHubContext<MarketDataHub> _hubContext;
    private readonly ILogger<MarketDataBroadcastService> _logger;

    public MarketDataBroadcastService(
        IMarketDataService marketDataService,
        IHubContext<MarketDataHub> hubContext,
        ILogger<MarketDataBroadcastService> logger)
    {
        _marketDataService = marketDataService;
        _hubContext = hubContext;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Market data broadcast service started");

        try
        {
            await foreach (var marketEvent in _marketDataService.GetLiveEventsAsync(stoppingToken))
            {
                await BroadcastMarketEventAsync(marketEvent, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Market data broadcast service cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in market data broadcast service");
        }
    }

    private async Task BroadcastMarketEventAsync(MarketEvent marketEvent, CancellationToken cancellationToken)
    {
        try
        {
            string? symbol = null;
            object? eventData = null;

            if (marketEvent.IsQuote)
            {
                var quote = marketEvent.AsQuoteUpdate();
                symbol = ExtractSymbolFromOptionSymbol(quote.Symbol.ToString());
                eventData = new
                {
                    Type = "quote",
                    Symbol = quote.Symbol.ToString(),
                    Timestamp = quote.TimestampNs,
                    BidPrice = quote.BidPrice,
                    BidSize = quote.BidSize,
                    AskPrice = quote.AskPrice,
                    AskSize = quote.AskSize,
                    Mid = quote.GetMidPrice(),
                    Spread = quote.GetSpread()
                };
            }
            else if (marketEvent.IsMarketData)
            {
                var tick = marketEvent.AsMarketTick();
                symbol = ExtractSymbolFromOptionSymbol(tick.Symbol.ToString());
                eventData = new
                {
                    Type = "trade",
                    Symbol = tick.Symbol.ToString(),
                    Timestamp = tick.TimestampNs,
                    Price = tick.Price,
                    Quantity = tick.Quantity,
                    DataType = tick.Type.ToString()
                };
            }

            if (!string.IsNullOrEmpty(symbol) && eventData != null)
            {
                // Broadcast to all clients subscribed to this symbol
                await _hubContext.Clients.Group($"symbol_{symbol}")
                    .SendAsync("MarketEvent", eventData, cancellationToken);

                // Also broadcast to general market data feed
                await _hubContext.Clients.Group("market_feed")
                    .SendAsync("MarketEvent", eventData, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error broadcasting market event");
        }
    }

    private static string ExtractSymbolFromOptionSymbol(string optionSymbol)
    {
        // Extract underlying symbol from option symbol (e.g., "SPY251121C00090000" -> "SPY")
        var callIndex = optionSymbol.IndexOf('C');
        var putIndex = optionSymbol.IndexOf('P');
        
        if (callIndex > 0)
            return optionSymbol.Substring(0, callIndex - 6); // Remove date part
        if (putIndex > 0)
            return optionSymbol.Substring(0, putIndex - 6); // Remove date part
            
        return optionSymbol; // Return as-is if not an option symbol
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await _marketDataService.StartAsync(cancellationToken);
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _marketDataService.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
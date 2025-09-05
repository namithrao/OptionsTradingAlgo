using Microsoft.AspNetCore.Mvc;
using Optx.Web.Services;
using Optx.Core.Types;
using Optx.Core.Events;

namespace Optx.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketDataController : ControllerBase
{
    private readonly IMarketDataService _marketDataService;
    private readonly ILogger<MarketDataController> _logger;

    public MarketDataController(IMarketDataService marketDataService, ILogger<MarketDataController> logger)
    {
        _marketDataService = marketDataService;
        _logger = logger;
    }

    [HttpPost("subscribe")]
    public async Task<IActionResult> Subscribe([FromBody] SubscriptionRequest request)
    {
        try
        {
            await _marketDataService.SubscribeToSymbolAsync(request.Symbol);
            return Ok(new { Message = $"Subscribed to {request.Symbol}", Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error subscribing to {Symbol}", request.Symbol);
            return BadRequest(new { Message = ex.Message, Success = false });
        }
    }

    [HttpPost("unsubscribe")]
    public async Task<IActionResult> Unsubscribe([FromBody] SubscriptionRequest request)
    {
        try
        {
            await _marketDataService.UnsubscribeFromSymbolAsync(request.Symbol);
            return Ok(new { Message = $"Unsubscribed from {request.Symbol}", Success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unsubscribing from {Symbol}", request.Symbol);
            return BadRequest(new { Message = ex.Message, Success = false });
        }
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        return Ok(new 
        { 
            Status = "Healthy",
            Timestamp = DateTime.UtcNow,
            Service = "Market Data API"
        });
    }

    [HttpGet("stock-price/{symbol}")]
    public async Task<IActionResult> GetStockPrice(string symbol)
    {
        try
        {
            var price = await _marketDataService.GetCurrentStockPriceAsync(symbol);
            
            if (price.HasValue)
            {
                return Ok(new
                {
                    Symbol = symbol,
                    Price = price.Value,
                    Timestamp = DateTime.UtcNow,
                    Success = true
                });
            }
            else
            {
                return NotFound(new 
                { 
                    Message = $"Stock price not found for {symbol}", 
                    Success = false 
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting stock price for {Symbol}", symbol);
            return BadRequest(new { Message = ex.Message, Success = false });
        }
    }

    [HttpGet("options-chain/{symbol}")]
    public async Task<IActionResult> GetOptionsChain(string symbol)
    {
        try
        {
            // This would typically query a database or cache
            // For now, return a mock response structure
            var optionsChain = new
            {
                Symbol = symbol,
                Timestamp = DateTime.UtcNow,
                Strikes = new[]
                {
                    new { Strike = 100m, Calls = new { Bid = 5.0m, Ask = 5.2m, IV = 0.25, Delta = 0.5, Gamma = 0.02 }, Puts = new { Bid = 3.0m, Ask = 3.2m, IV = 0.26, Delta = -0.5, Gamma = 0.02 } },
                    new { Strike = 105m, Calls = new { Bid = 3.5m, Ask = 3.7m, IV = 0.24, Delta = 0.4, Gamma = 0.025 }, Puts = new { Bid = 5.5m, Ask = 5.7m, IV = 0.25, Delta = -0.6, Gamma = 0.025 } }
                }
            };

            return Ok(optionsChain);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting options chain for {Symbol}", symbol);
            return BadRequest(new { Message = ex.Message, Success = false });
        }
    }
}

public record SubscriptionRequest(string Symbol);
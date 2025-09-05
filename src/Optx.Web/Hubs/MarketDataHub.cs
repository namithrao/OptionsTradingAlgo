using Microsoft.AspNetCore.SignalR;
using Optx.Core.Events;
using Optx.Core.Types;
using Optx.Web.Services;

namespace Optx.Web.Hubs;

public class MarketDataHub : Hub
{
    private readonly IMarketDataService _marketDataService;
    private readonly ILogger<MarketDataHub> _logger;
    
    public MarketDataHub(IMarketDataService marketDataService, ILogger<MarketDataHub> logger)
    {
        _marketDataService = marketDataService;
        _logger = logger;
    }

    public async Task SubscribeToSymbol(string symbol)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"symbol_{symbol}");
        await _marketDataService.SubscribeToSymbolAsync(symbol);
        
        _logger.LogInformation("Client {ConnectionId} subscribed to {Symbol}", Context.ConnectionId, symbol);
    }

    public async Task UnsubscribeFromSymbol(string symbol)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"symbol_{symbol}");
        
        _logger.LogInformation("Client {ConnectionId} unsubscribed from {Symbol}", Context.ConnectionId, symbol);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client {ConnectionId} connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}

public class StrategyHub : Hub
{
    private readonly ILogger<StrategyHub> _logger;
    
    public StrategyHub(ILogger<StrategyHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinStrategyRoom(string strategyId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"strategy_{strategyId}");
        _logger.LogInformation("Client {ConnectionId} joined strategy room {StrategyId}", Context.ConnectionId, strategyId);
    }

    public async Task LeaveStrategyRoom(string strategyId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"strategy_{strategyId}");
        _logger.LogInformation("Client {ConnectionId} left strategy room {StrategyId}", Context.ConnectionId, strategyId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected from strategy hub", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}

public class BacktestHub : Hub
{
    private readonly ILogger<BacktestHub> _logger;
    
    public BacktestHub(ILogger<BacktestHub> logger)
    {
        _logger = logger;
    }

    public async Task JoinBacktestRoom(string backtestId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"backtest_{backtestId}");
        _logger.LogInformation("Client {ConnectionId} joined backtest room {BacktestId}", Context.ConnectionId, backtestId);
    }

    public async Task LeaveBacktestRoom(string backtestId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"backtest_{backtestId}");
        _logger.LogInformation("Client {ConnectionId} left backtest room {BacktestId}", Context.ConnectionId, backtestId);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client {ConnectionId} disconnected from backtest hub", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
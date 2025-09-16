using Optx.Web.Services;
using Optx.Web.Hubs;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR
builder.Services.AddSignalR();

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", builder =>
    {
        builder
            .AllowAnyMethod()
            .AllowAnyHeader()
            .SetIsOriginAllowed(origin => true)
            .AllowCredentials();
    });
});

// Add HttpClient for Polygon API
builder.Services.AddHttpClient();

// Add market data services
builder.Services.AddSingleton<IMarketDataService, PolygonMarketDataService>();
builder.Services.AddHostedService<MarketDataBroadcastService>();

// Add historical data services
builder.Services.AddSingleton<IHistoricalDataService, PolygonHistoricalDataService>();
builder.Services.AddSingleton<IHistoricalDataCache, HistoricalDataCache>();

// Add options calculation services
builder.Services.AddSingleton<IOptionsCalculationService, OptionsCalculationService>();

// Add configuration for Polygon API
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);
builder.Configuration.AddEnvironmentVariables();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAll");
app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();

// Map SignalR hubs
app.MapHub<MarketDataHub>("/hubs/marketdata");
app.MapHub<StrategyHub>("/hubs/strategy");
app.MapHub<BacktestHub>("/hubs/backtest");

// Health check endpoint
app.MapGet("/health", () => new { Status = "Healthy", Timestamp = DateTime.UtcNow });

app.Run();

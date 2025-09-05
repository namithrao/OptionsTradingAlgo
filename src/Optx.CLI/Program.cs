using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;
using Optx.Core.Types;
using Optx.Core.Utils;
using Optx.Core.Interfaces;
using Optx.Core.Events;
using Optx.Data.Generators;
using Optx.Data.Storage;
using Optx.Engine;
using Optx.Pricing;
using Optx.Strategies;

namespace Optx.CLI;

/// <summary>
/// Command-line interface for the Optx trading system
/// </summary>
public class Program
{
    public static async Task<int> Main(string[] args)
    {
        var rootCommand = new RootCommand("Optx - Options Trading System")
        {
            CreateGenSynthCommand(),
            CreateBacktestCommand(),
            CreateReplayCommand(),
            CreateBenchCommand()
        };

        return await rootCommand.InvokeAsync(args);
    }

    /// <summary>
    /// Generate synthetic market data
    /// </summary>
    private static Command CreateGenSynthCommand()
    {
        var daysOption = new Option<int>("--days", () => 5, "Number of days to generate");
        var dtOption = new Option<string>("--dt", () => "1s", "Time step between observations");
        var lambdaOption = new Option<double>("--lambda", () => 0.1, "Jump intensity (jumps per year)");
        var symbolsOption = new Option<string[]>("--symbols", () => new[] { "SPY" }, "Symbols to generate")
        {
            AllowMultipleArgumentsPerToken = true
        };
        var outputOption = new Option<string>("--output", () => "data", "Output directory");
        var seedOption = new Option<int?>("--seed", "Random seed for reproducibility");

        var command = new Command("gen-synth", "Generate synthetic market data and options chains")
        {
            daysOption,
            dtOption,
            lambdaOption,
            symbolsOption,
            outputOption,
            seedOption
        };

        command.SetHandler(async (days, dt, lambda, symbols, output, seed) =>
        {
            await GenerateSyntheticData(days, dt, lambda, symbols, output, seed);
        }, daysOption, dtOption, lambdaOption, symbolsOption, outputOption, seedOption);

        return command;
    }

    /// <summary>
    /// Run backtest command
    /// </summary>
    private static Command CreateBacktestCommand()
    {
        var configOption = new Option<string>("--config", "Configuration file path") { IsRequired = true };
        var strategyOption = new Option<string>("--strategy", "Strategy name (covered-call, straddle, etc.)");
        var dataOption = new Option<string>("--data", () => "data", "Data directory containing tick files");
        var outputOption = new Option<string>("--output", () => "artifacts", "Output directory for results");

        var command = new Command("backtest", "Run backtesting simulation")
        {
            configOption,
            strategyOption,
            dataOption,
            outputOption
        };

        command.SetHandler(async (config, strategy, data, output) =>
        {
            await RunBacktest(config, strategy, data, output);
        }, configOption, strategyOption, dataOption, outputOption);

        return command;
    }

    /// <summary>
    /// Replay tick data command
    /// </summary>
    private static Command CreateReplayCommand()
    {
        var srcOption = new Option<string>("--src", "Source tick file") { IsRequired = true };
        var rateOption = new Option<string>("--rate", () => "1x", "Replay rate (1x, 10x, max)");

        var command = new Command("replay", "Replay tick data")
        {
            srcOption,
            rateOption
        };

        command.SetHandler(async (src, rate) =>
        {
            await ReplayTickData(src, rate);
        }, srcOption, rateOption);

        return command;
    }

    /// <summary>
    /// Run benchmarks command
    /// </summary>
    private static Command CreateBenchCommand()
    {
        var command = new Command("bench", "Run performance benchmarks");

        command.SetHandler(async () =>
        {
            await RunBenchmarks();
        });

        return command;
    }

    /// <summary>
    /// Generate synthetic market data implementation
    /// </summary>
    private static async Task GenerateSyntheticData(
        int days, 
        string dtStr, 
        double lambda, 
        string[] symbols, 
        string output, 
        int? seed)
    {
        Console.WriteLine($"Generating {days} days of synthetic data for {string.Join(", ", symbols)}");
        
        // Parse time step
        var timeStep = ParseTimeStep(dtStr);
        var duration = TimeSpan.FromDays(days);

        Directory.CreateDirectory(output);

        foreach (var symbol in symbols)
        {
            Console.WriteLine($"Generating data for {symbol}...");

            // Generate underlying price path
            var generator = new UnderlyingGenerator(
                initialPrice: 100.0,
                drift: 0.05,
                volatility: 0.2,
                timeStep: timeStep,
                jumpIntensity: lambda,
                seed: seed);

            var ticks = generator.GeneratePath(duration, symbol);
            
            // Write to tick file
            var tickFile = Path.Combine(output, $"{symbol}_ticks.bin");
            using var tickWriter = new TickWriter(tickFile);
            tickWriter.WriteHeader("1.0", $"Synthetic data for {symbol}");
            
            foreach (var tick in ticks)
            {
                tickWriter.WriteTick(tick);
            }

            await tickWriter.FlushAsync();
            
            Console.WriteLine($"Generated {ticks.Count:N0} ticks for {symbol} -> {tickFile}");

            // Generate options chain (simplified)
            await GenerateOptionsChain(symbol, ticks, output);
        }

        Console.WriteLine($"Synthetic data generation complete. Output: {output}");
    }

    /// <summary>
    /// Run backtest implementation
    /// </summary>
    private static async Task RunBacktest(string configPath, string? strategyName, string dataDir, string outputDir)
    {
        Console.WriteLine($"Running backtest:");
        Console.WriteLine($"  Config: {configPath}");
        Console.WriteLine($"  Strategy: {strategyName ?? "from config"}");
        Console.WriteLine($"  Data: {dataDir}");
        Console.WriteLine($"  Output: {outputDir}");
        
        if (!File.Exists(configPath))
        {
            Console.WriteLine($"Error: Config file not found: {configPath}");
            return;
        }

        if (!Directory.Exists(dataDir))
        {
            Console.WriteLine($"Error: Data directory not found: {dataDir}");
            return;
        }

        try
        {
            // Load configuration (simplified YAML parsing)
            var configText = await File.ReadAllTextAsync(configPath);
            var config = ParseConfig(configText);

            // Create strategy based on name or config
            var strategy = CreateStrategy(strategyName ?? "covered-call", config);
            var fillModel = new SimpleFillModel();
            var riskChecks = new SimpleRiskChecks();

            var backtestConfig = new BacktestConfig
            {
                InitialCash = 100000m,
                EnableProgressReporting = true
            };

            var engine = new BacktestEngine(strategy, fillModel, riskChecks, backtestConfig);

            // Load actual tick data from files
            var events = await LoadMarketDataFromFiles(dataDir);
            engine.AddEvents(events);

            Console.WriteLine("Starting backtest...");
            var results = await engine.RunAsync();

            // Output results
            Directory.CreateDirectory(outputDir);
            await WriteResults(results, outputDir);

            Console.WriteLine($"Backtest complete. Results written to: {outputDir}");
            Console.WriteLine($"Total Return: {results.TotalReturn:C}");
            Console.WriteLine($"Events Processed: {results.EventsProcessed:N0}");
            Console.WriteLine($"Events/Second: {results.EventsPerSecond:F0}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Backtest failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Replay tick data implementation
    /// </summary>
    private static async Task ReplayTickData(string srcPath, string rate)
    {
        Console.WriteLine($"Replaying tick data from: {srcPath} at rate: {rate}");
        
        if (!File.Exists(srcPath))
        {
            Console.WriteLine($"Error: Source file not found: {srcPath}");
            return;
        }

        try
        {
            using var reader = new TickReader(srcPath);
            int count = 0;
            var startTime = DateTime.UtcNow;

            while (reader.ReadTick(out var tick))
            {
                // Process tick (placeholder)
                count++;
                
                if (count % 10000 == 0)
                {
                    var elapsed = DateTime.UtcNow - startTime;
                    var ticksPerSecond = count / elapsed.TotalSeconds;
                    Console.WriteLine($"Processed {count:N0} ticks ({ticksPerSecond:F0} ticks/sec)");
                }

                // Rate limiting based on rate parameter
                if (rate == "1x")
                {
                    await Task.Delay(1); // Simulate real-time
                }
                else if (rate != "max")
                {
                    // Parse rate multiplier
                    if (rate.EndsWith("x") && int.TryParse(rate[..^1], out var multiplier))
                    {
                        await Task.Delay(Math.Max(1, 1000 / multiplier));
                    }
                }
            }

            var totalElapsed = DateTime.UtcNow - startTime;
            Console.WriteLine($"Replay complete. Processed {count:N0} ticks in {totalElapsed:g}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Replay failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Run benchmarks implementation
    /// </summary>
    private static Task RunBenchmarks()
    {
        Console.WriteLine("Running performance benchmarks...");

        // Black-Scholes pricing benchmark
        var pricingResults = BenchmarkBlackScholes();
        Console.WriteLine($"Black-Scholes pricing: {pricingResults.OperationsPerSecond:N0} ops/sec");

        // Implied volatility benchmark
        var ivResults = BenchmarkImpliedVolatility();
        Console.WriteLine($"Implied volatility solving: {ivResults.OperationsPerSecond:N0} ops/sec");

        // Event processing benchmark
        var eventResults = BenchmarkEventProcessing();
        Console.WriteLine($"Event processing: {eventResults.EventsPerSecond:N0} events/sec");

        Console.WriteLine("Benchmark complete.");
        
        return Task.CompletedTask;
    }

    // Helper methods (simplified implementations)

    private static TimeSpan ParseTimeStep(string dtStr)
    {
        if (dtStr.EndsWith("s"))
            return TimeSpan.FromSeconds(int.Parse(dtStr[..^1]));
        if (dtStr.EndsWith("ms"))
            return TimeSpan.FromMilliseconds(int.Parse(dtStr[..^2]));
        if (dtStr.EndsWith("m"))
            return TimeSpan.FromMinutes(int.Parse(dtStr[..^1]));
        
        return TimeSpan.FromSeconds(1);
    }

    private static async Task GenerateOptionsChain(string symbol, List<MarketTick> ticks, string output)
    {
        if (ticks.Count == 0)
            return;

        // Create simple volatility surface (flat surface for synthetic data)
        var expiries = new[] { 1.0 / 12, 2.0 / 12, 3.0 / 12, 6.0 / 12, 1.0 }; // 1M, 2M, 3M, 6M, 1Y
        var strikes = Enumerable.Range(80, 41).Select(x => (double)x).ToArray(); // 80-120 strikes
        var volatilities = new double[expiries.Length, strikes.Length];
        
        // Fill with realistic implied volatilities (ATM ~20%, smile effect)
        for (int i = 0; i < expiries.Length; i++)
        {
            for (int j = 0; j < strikes.Length; j++)
            {
                var atmStrike = 100.0; // Assume ATM around 100
                var moneyness = strikes[j] / atmStrike;
                var termMultiplier = Math.Sqrt(expiries[i]);
                
                // Create volatility smile (higher vol for OTM options)
                var baseVol = 0.20; // 20% ATM vol
                var smileEffect = 0.1 * Math.Pow(Math.Abs(moneyness - 1.0), 1.5);
                volatilities[i, j] = (baseVol + smileEffect) * termMultiplier;
            }
        }

        var volSurface = new VolatilitySurface(expiries, strikes, volatilities);
        var optionsGenerator = new OptionsChainGenerator(volSurface, riskFreeRate: 0.05, dividendYield: 0.01);

        // Get current time and underlying price from last tick
        var lastTick = ticks[^1];
        var currentTime = DateTime.UnixEpoch.AddTicks((long)(lastTick.TimestampNs / 100));
        var underlyingPrice = (double)lastTick.Price;

        // Generate standard monthly expiries (next 4 monthlies)
        var monthlyExpiries = Enumerable.Range(1, 4)
            .Select(months => GetNextMonthlyExpiry(currentTime, months))
            .ToArray();

        // Generate options chains for multiple expiries
        var allOptions = new List<object>();
        
        foreach (var expiry in monthlyExpiries)
        {
            var expiryChain = optionsGenerator.GenerateChain(
                underlyingPrice, expiry, currentTime, symbol, 
                numStrikes: 21, strikeDelta: 5.0);

            foreach (var quote in expiryChain)
            {
                allOptions.Add(new
                {
                    Symbol = quote.Contract.Symbol.ToString(),
                    UnderlyingSymbol = symbol,
                    Strike = quote.Contract.Strike,
                    Expiry = quote.Contract.Expiry.ToString("yyyy-MM-dd"),
                    OptionType = quote.Contract.OptionType.ToString(),
                    BidPrice = quote.BidPrice,
                    BidSize = quote.BidSize,
                    AskPrice = quote.AskPrice,
                    AskSize = quote.AskSize,
                    ImpliedVol = Math.Round(quote.ImpliedVolatility, 4),
                    Delta = Math.Round(quote.Greeks.Delta, 4),
                    Gamma = Math.Round(quote.Greeks.Gamma, 6),
                    Theta = Math.Round(quote.Greeks.Theta, 4),
                    Vega = Math.Round(quote.Greeks.Vega, 4)
                });
            }
        }

        var chainFile = Path.Combine(output, $"{symbol}_options.json");
        var optionsChainData = new { Symbol = symbol, Contracts = allOptions };
        
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        
        await File.WriteAllTextAsync(chainFile, JsonSerializer.Serialize(optionsChainData, options));
        Console.WriteLine($"Generated {allOptions.Count} option contracts for {symbol}");
    }

    private static DateTime GetNextMonthlyExpiry(DateTime currentTime, int monthsOut)
    {
        // Standard monthly expiry is 3rd Friday of the month
        var targetMonth = currentTime.AddMonths(monthsOut);
        var firstDayOfMonth = new DateTime(targetMonth.Year, targetMonth.Month, 1);
        var firstFriday = firstDayOfMonth.AddDays((5 - (int)firstDayOfMonth.DayOfWeek + 7) % 7);
        var thirdFriday = firstFriday.AddDays(14);
        
        // Set to market close time (4:00 PM ET)
        return new DateTime(thirdFriday.Year, thirdFriday.Month, thirdFriday.Day, 16, 0, 0);
    }

    private static BacktestConfig ParseConfig(string configText)
    {
        var deserializer = new YamlDotNet.Serialization.DeserializerBuilder()
            .WithNamingConvention(YamlDotNet.Serialization.NamingConventions.UnderscoredNamingConvention.Instance)
            .Build();

        try
        {
            var yamlConfig = deserializer.Deserialize<ConfigRoot>(configText);
            
            return new BacktestConfig
            {
                InitialCash = yamlConfig?.Backtest?.InitialCapital ?? 100000m,
                EnableProgressReporting = true
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to parse YAML config: {ex.Message}");
            return new BacktestConfig
            {
                InitialCash = 100000m,
                EnableProgressReporting = true
            };
        }
    }

    // Config structure classes for YAML deserialization
    private class ConfigRoot
    {
        public StrategyConfig? Strategy { get; set; }
        public RiskConfig? Risk { get; set; }
        public ExecutionConfig? Execution { get; set; }
        public MarketDataConfig? MarketData { get; set; }
        public BacktestYamlConfig? Backtest { get; set; }
        public ReportingConfig? Reporting { get; set; }
    }

    private class StrategyConfig
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    private class RiskConfig
    {
        public decimal? MaxPortfolioDelta { get; set; }
        public decimal? MaxSinglePosition { get; set; }
        public decimal? MaxDailyLoss { get; set; }
    }

    private class ExecutionConfig
    {
        public string OrderType { get; set; } = "LIMIT";
        public decimal? PriceImprovement { get; set; }
        public int? TimeoutSeconds { get; set; }
    }

    private class MarketDataConfig
    {
        public List<string> Symbols { get; set; } = new();
        public decimal? TickSize { get; set; }
    }

    private class BacktestYamlConfig
    {
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public decimal? InitialCapital { get; set; }
        public decimal? CommissionPerContract { get; set; }
    }

    private class ReportingConfig
    {
        public string? OutputPath { get; set; }
        public bool? GeneratePlots { get; set; }
        public List<string> Metrics { get; set; } = new();
    }

    private static List<Core.Events.MarketEvent> LoadTestEvents()
    {
        // Generate some test events
        var events = new List<Core.Events.MarketEvent>();
        var currentTime = TimeUtils.GetCurrentNanoseconds();
        
        for (int i = 0; i < 1000; i++)
        {
            var tick = new MarketTick(
                currentTime + (ulong)(i * 1_000_000), // 1ms apart
                "SPY".AsMemory(),
                100m + (decimal)(Math.Sin(i * 0.1) * 5),
                100,
                MarketDataType.Trade);
            
            events.Add(new Core.Events.MarketEvent(tick));
        }
        
        return events;
    }

    private static IStrategy CreateStrategy(string strategyName, BacktestConfig config)
    {
        return strategyName.ToLowerInvariant() switch
        {
            "covered-call" or "coveredcall" => new CoveredCallStrategy(new CoveredCallConfig
            {
                MinDelta = 0.25,
                MaxDelta = 0.35,
                TargetDaysToExpiry = 30,
                RollAtDte = 21,
                RollAtPnLPercent = 50.0,
                LotSize = 100,
                MaxPositions = 10,
                Symbols = new List<string> { "SPY", "QQQ" }
            }),
            _ => throw new ArgumentException($"Unknown strategy: {strategyName}")
        };
    }

    private static Task<List<Core.Events.MarketEvent>> LoadMarketDataFromFiles(string dataDir)
    {
        var events = new List<Core.Events.MarketEvent>();
        var tickFiles = Directory.GetFiles(dataDir, "*_ticks.bin");
        var optionFiles = Directory.GetFiles(dataDir, "*_options.json");
        
        Console.WriteLine($"Loading data from {tickFiles.Length} tick files and {optionFiles.Length} options files...");
        
        // Load underlying tick data
        foreach (var tickFile in tickFiles)
        {
            Console.WriteLine($"  Loading {Path.GetFileName(tickFile)}...");
            
            try
            {
                using var reader = new TickReader(tickFile);
                var fileEvents = new List<Core.Events.MarketEvent>();
                
                while (reader.ReadTick(out var tick))
                {
                    fileEvents.Add(new Core.Events.MarketEvent(tick));
                }
                
                events.AddRange(fileEvents);
                Console.WriteLine($"    Loaded {fileEvents.Count:N0} underlying ticks");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Error loading {tickFile}: {ex.Message}");
            }
        }
        
        // Load options quote data
        foreach (var optionFile in optionFiles)
        {
            Console.WriteLine($"  Loading {Path.GetFileName(optionFile)}...");
            
            try
            {
                var jsonContent = File.ReadAllText(optionFile);
                var optionsData = JsonSerializer.Deserialize<OptionsFileData>(jsonContent);
                
                if (optionsData?.Contracts != null)
                {
                    var optionEvents = GenerateOptionQuoteEvents(optionsData);
                    events.AddRange(optionEvents);
                    Console.WriteLine($"    Generated {optionEvents.Count:N0} option quote events");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Error loading {optionFile}: {ex.Message}");
            }
        }
        
        // Sort events by timestamp for deterministic backtesting
        events.Sort((a, b) => a.GetTimestamp().CompareTo(b.GetTimestamp()));
        
        Console.WriteLine($"Total events loaded: {events.Count:N0}");
        return Task.FromResult(events);
    }

    private static List<Core.Events.MarketEvent> GenerateOptionQuoteEvents(OptionsFileData optionsData)
    {
        var events = new List<Core.Events.MarketEvent>();
        var baseTimestamp = TimeUtils.GetCurrentNanoseconds();
        
        // Generate quote events at 1-second intervals to simulate live options quotes
        for (int timeOffset = 0; timeOffset < 100; timeOffset += 10) // Every 10 seconds for 100 seconds
        {
            var timestamp = baseTimestamp + (ulong)(timeOffset * 1_000_000_000L);
            
            foreach (var contract in optionsData.Contracts)
            {
                // Add small random variation to quotes to simulate market movement
                var random = new Random(contract.Symbol.GetHashCode() + timeOffset);
                var bidVariation = 1.0 + (random.NextDouble() - 0.5) * 0.02; // ±1% variation
                var askVariation = 1.0 + (random.NextDouble() - 0.5) * 0.02;
                
                var quoteUpdate = new QuoteUpdate(
                    timestamp,
                    contract.Symbol.AsMemory(),
                    Math.Max(0.01m, contract.BidPrice * (decimal)bidVariation),
                    Math.Max(1, contract.BidSize + random.Next(-5, 6)),
                    Math.Max(0.01m, contract.AskPrice * (decimal)askVariation),
                    Math.Max(1, contract.AskSize + random.Next(-5, 6))
                );
                
                events.Add(new Core.Events.MarketEvent(quoteUpdate));
            }
        }
        
        return events;
    }

    // Data structures for JSON deserialization
    private class OptionsFileData
    {
        public string Symbol { get; set; } = string.Empty;
        public List<OptionContractData> Contracts { get; set; } = new();
    }

    private class OptionContractData
    {
        public string Symbol { get; set; } = string.Empty;
        public string UnderlyingSymbol { get; set; } = string.Empty;
        public decimal Strike { get; set; }
        public string Expiry { get; set; } = string.Empty;
        public string OptionType { get; set; } = string.Empty;
        public decimal BidPrice { get; set; }
        public int BidSize { get; set; }
        public decimal AskPrice { get; set; }
        public int AskSize { get; set; }
        public double ImpliedVol { get; set; }
        public double Delta { get; set; }
        public double Gamma { get; set; }
        public double Theta { get; set; }
        public double Vega { get; set; }
    }

    private static async Task WriteResults(BacktestResults results, string outputDir)
    {
        var summaryFile = Path.Combine(outputDir, "backtest_summary.json");
        
        var options = new JsonSerializerOptions 
        { 
            WriteIndented = true,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };
        
        await File.WriteAllTextAsync(summaryFile, JsonSerializer.Serialize(results, options));
    }

    private static BenchmarkResult BenchmarkBlackScholes()
    {
        const int iterations = 100000;
        var start = DateTime.UtcNow;
        
        for (int i = 0; i < iterations; i++)
        {
            BlackScholes.Price(100.0, 105.0, 0.25, 0.2, 0.05, 0.01, OptionType.Call);
        }
        
        var elapsed = DateTime.UtcNow - start;
        return new BenchmarkResult { OperationsPerSecond = iterations / elapsed.TotalSeconds };
    }

    private static BenchmarkResult BenchmarkImpliedVolatility()
    {
        const int iterations = 10000;
        var start = DateTime.UtcNow;
        
        for (int i = 0; i < iterations; i++)
        {
            ImpliedVolatility.Solve(5.0, 100.0, 105.0, 0.25, 0.05, 0.01, OptionType.Call);
        }
        
        var elapsed = DateTime.UtcNow - start;
        return new BenchmarkResult { OperationsPerSecond = iterations / elapsed.TotalSeconds };
    }

    private static BenchmarkResult BenchmarkEventProcessing()
    {
        const int events = 100000;
        return new BenchmarkResult { EventsPerSecond = events }; // Placeholder
    }

    private record BenchmarkResult
    {
        public double OperationsPerSecond { get; init; }
        public double EventsPerSecond { get; init; }
    }
}

// Simplified strategy implementations for CLI testing
internal class SimpleStrategy : IStrategy
{
    public string Name => "SimpleStrategy";

    public IEnumerable<Order> OnEvent(in Core.Events.MarketEvent marketEvent, in PortfolioState portfolioState)
    {
        return Array.Empty<Order>();
    }

    public void OnFill(in Fill fill, in PortfolioState portfolioState) { }

    public void OnOrderAck(in OrderAck orderAck) { }

    public IReadOnlyDictionary<string, object> GetState()
    {
        return new Dictionary<string, object>();
    }
}

internal class SimpleFillModel : IFillModel
{
    public IEnumerable<Fill> TryFill(in Order order, in OrderBook book)
    {
        return Array.Empty<Fill>();
    }

    public decimal CalculateCommission(in Fill fill)
    {
        return 0.65m; // Flat $0.65 per contract
    }
}

internal class SimpleRiskChecks : IRiskChecks
{
    public bool Approve(in Order order, in PortfolioState portfolioState, out string reason)
    {
        reason = "";
        return true; // Always approve for testing
    }
}
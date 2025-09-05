using Microsoft.AspNetCore.Mvc;
using Optx.Web.Services;
using Optx.Web.Models;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Optx.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StrategyController : ControllerBase
{
    private readonly ILogger<StrategyController> _logger;
    private readonly IOptionsCalculationService _optionsService;

    public StrategyController(ILogger<StrategyController> logger, IOptionsCalculationService optionsService)
    {
        _logger = logger;
        _optionsService = optionsService;
    }

    [HttpPost("validate")]
    public async Task<IActionResult> ValidateStrategy([FromBody] StrategyConfigRequest request)
    {
        try
        {
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            // Try to deserialize the YAML config
            var config = deserializer.Deserialize<Dictionary<string, object>>(request.ConfigYaml);

            var validationResults = ValidateStrategyConfig(config, request.StrategyType);

            return Ok(new StrategyValidationResponse
            {
                IsValid = validationResults.Count == 0,
                Errors = validationResults,
                Config = config
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating strategy config");
            return BadRequest(new StrategyValidationResponse
            {
                IsValid = false,
                Errors = new[] { ex.Message },
                Config = null
            });
        }
    }

    [HttpGet("templates")]
    public IActionResult GetStrategyTemplates()
    {
        var templates = new Dictionary<string, object>
        {
            ["covered-call"] = new
            {
                Name = "Covered Call",
                Description = "Sell covered calls against long underlying positions",
                Template = GetCoveredCallTemplate()
            },
            ["cash-secured-put"] = new
            {
                Name = "Cash Secured Put",
                Description = "Sell cash-secured puts on underlyings",
                Template = GetCashSecuredPutTemplate()
            },
            ["straddle"] = new
            {
                Name = "Long Straddle",
                Description = "Buy call and put with same strike and expiry",
                Template = GetStraddleTemplate()
            }
        };

        return Ok(templates);
    }

    [HttpPost("analyze")]
    public async Task<IActionResult> AnalyzeStrategy([FromBody] StrategyAnalysisRequest request)
    {
        try
        {
            // This would integrate with your existing strategy analysis
            // For now, return a mock analysis
            var analysis = new StrategyAnalysisResponse
            {
                ExpectedReturn = CalculateExpectedReturn(request),
                MaxRisk = CalculateMaxRisk(request),
                BreakEvenPoints = CalculateBreakEvenPoints(request),
                Greeks = await CalculateStrategyGreeks(request),
                ProbabilityOfProfit = CalculateProbabilityOfProfit(request)
            };

            return Ok(analysis);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing strategy");
            return BadRequest(new { Message = ex.Message });
        }
    }

    private List<string> ValidateStrategyConfig(Dictionary<string, object> config, string strategyType)
    {
        var errors = new List<string>();

        if (!config.ContainsKey("strategy"))
        {
            errors.Add("Strategy configuration must contain a 'strategy' section");
            return errors;
        }

        switch (strategyType.ToLowerInvariant())
        {
            case "covered-call":
                errors.AddRange(ValidateCoveredCallConfig(config));
                break;
            case "cash-secured-put":
                errors.AddRange(ValidateCashSecuredPutConfig(config));
                break;
            default:
                errors.Add($"Unknown strategy type: {strategyType}");
                break;
        }

        return errors;
    }

    private List<string> ValidateCoveredCallConfig(Dictionary<string, object> config)
    {
        var errors = new List<string>();

        try
        {
            var strategy = config["strategy"] as Dictionary<object, object>;
            var parameters = strategy?["parameters"] as Dictionary<object, object>;

            if (parameters == null)
            {
                errors.Add("Strategy must contain parameters section");
                return errors;
            }

            // Validate delta range
            if (parameters.ContainsKey("min_delta") && parameters.ContainsKey("max_delta"))
            {
                var minDelta = Convert.ToDouble(parameters["min_delta"]);
                var maxDelta = Convert.ToDouble(parameters["max_delta"]);

                if (minDelta <= 0 || minDelta >= 1 || maxDelta <= 0 || maxDelta >= 1)
                {
                    errors.Add("Delta values must be between 0 and 1");
                }

                if (minDelta >= maxDelta)
                {
                    errors.Add("min_delta must be less than max_delta");
                }
            }

            // Validate DTE range
            if (parameters.ContainsKey("min_dte") && parameters.ContainsKey("max_dte"))
            {
                var minDte = Convert.ToInt32(parameters["min_dte"]);
                var maxDte = Convert.ToInt32(parameters["max_dte"]);

                if (minDte <= 0 || maxDte <= 0)
                {
                    errors.Add("DTE values must be positive");
                }

                if (minDte >= maxDte)
                {
                    errors.Add("min_dte must be less than max_dte");
                }
            }
        }
        catch (Exception ex)
        {
            errors.Add($"Error validating covered call parameters: {ex.Message}");
        }

        return errors;
    }

    private List<string> ValidateCashSecuredPutConfig(Dictionary<string, object> config)
    {
        // Similar validation for cash secured puts
        return new List<string>();
    }

    private object GetCoveredCallTemplate()
    {
        return new
        {
            strategy = new
            {
                name = "CoveredCall",
                description = "Sell covered calls against long underlying positions",
                parameters = new
                {
                    min_delta = 0.25,
                    max_delta = 0.35,
                    min_dte = 30,
                    max_dte = 45,
                    roll_at_pnl_percent = 50.0,
                    roll_at_dte = 21,
                    lot_size = 100,
                    max_positions = 10
                }
            },
            risk = new
            {
                max_portfolio_delta = 1000,
                max_single_position = 10000,
                max_daily_loss = 5000
            },
            execution = new
            {
                order_type = "LIMIT",
                price_improvement = 0.01,
                timeout_seconds = 30
            }
        };
    }

    private object GetCashSecuredPutTemplate()
    {
        return new
        {
            strategy = new
            {
                name = "CashSecuredPut",
                description = "Sell cash-secured puts on underlyings",
                parameters = new
                {
                    min_delta = -0.35,
                    max_delta = -0.25,
                    min_dte = 30,
                    max_dte = 45,
                    cash_requirement_multiplier = 1.0,
                    max_positions = 5
                }
            }
        };
    }

    private object GetStraddleTemplate()
    {
        return new
        {
            strategy = new
            {
                name = "LongStraddle",
                description = "Buy call and put with same strike and expiry",
                parameters = new
                {
                    target_dte = 30,
                    min_implied_volatility = 0.20,
                    max_implied_volatility = 0.80,
                    profit_target_percent = 100.0
                }
            }
        };
    }

    private decimal CalculateExpectedReturn(StrategyAnalysisRequest request)
    {
        // Simplified expected return calculation
        // In production, this would use Monte Carlo simulation
        return 0.12m; // 12% expected annual return
    }

    private decimal CalculateMaxRisk(StrategyAnalysisRequest request)
    {
        // Calculate maximum potential loss
        return request.Allocation * 0.5m; // Assume max 50% loss for example
    }

    private decimal[] CalculateBreakEvenPoints(StrategyAnalysisRequest request)
    {
        // Calculate break-even points for the strategy
        // This would be strategy-specific
        return new[] { request.UnderlyingPrice * 0.95m, request.UnderlyingPrice * 1.05m };
    }

    private async Task<object> CalculateStrategyGreeks(StrategyAnalysisRequest request)
    {
        // Use your existing Greeks calculation service
        var positions = new Dictionary<string, Core.Types.Position>();
        var underlyingPrices = new Dictionary<string, decimal> { [request.UnderlyingSymbol] = request.UnderlyingPrice };

        var greeks = await _optionsService.CalculatePortfolioGreeksAsync(positions, underlyingPrices);

        return new
        {
            Delta = greeks.GetValueOrDefault("TOTAL").Delta,
            Gamma = greeks.GetValueOrDefault("TOTAL").Gamma,
            Theta = greeks.GetValueOrDefault("TOTAL").Theta,
            Vega = greeks.GetValueOrDefault("TOTAL").Vega
        };
    }

    private double CalculateProbabilityOfProfit(StrategyAnalysisRequest request)
    {
        // Calculate probability of profit using Black-Scholes
        // This would involve complex calculations based on strategy type
        return 0.65; // 65% probability for example
    }
}

public record StrategyConfigRequest(string StrategyType, string ConfigYaml);

public record StrategyValidationResponse
{
    public bool IsValid { get; set; }
    public IEnumerable<string> Errors { get; set; } = Array.Empty<string>();
    public object? Config { get; set; }
}

public record StrategyAnalysisRequest(
    string StrategyType,
    string UnderlyingSymbol,
    decimal UnderlyingPrice,
    decimal Allocation,
    Dictionary<string, object> Parameters);

public record StrategyAnalysisResponse
{
    public decimal ExpectedReturn { get; set; }
    public decimal MaxRisk { get; set; }
    public decimal[] BreakEvenPoints { get; set; } = Array.Empty<decimal>();
    public object Greeks { get; set; } = new();
    public double ProbabilityOfProfit { get; set; }
}
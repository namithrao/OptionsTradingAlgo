using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Optx.Web.Models;

[Table("market_ticks")]
[Index(nameof(Symbol), nameof(Timestamp))]
public class MarketTickEntity
{
    [Key]
    public long Id { get; set; }
    
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [Required]
    [MaxLength(50)]
    [Column("symbol")]
    public string Symbol { get; set; } = string.Empty;
    
    [Column("price")]
    [Precision(18, 8)]
    public decimal Price { get; set; }
    
    [Column("volume")]
    public int Volume { get; set; }
    
    [Column("data_type")]
    public byte DataType { get; set; }
}

[Table("option_quotes")]
[Index(nameof(Symbol), nameof(Timestamp))]
public class OptionQuoteEntity
{
    [Key]
    public long Id { get; set; }
    
    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [Required]
    [MaxLength(50)]
    [Column("symbol")]
    public string Symbol { get; set; } = string.Empty;
    
    [MaxLength(20)]
    [Column("underlying_symbol")]
    public string UnderlyingSymbol { get; set; } = string.Empty;
    
    [Column("strike")]
    [Precision(18, 8)]
    public decimal Strike { get; set; }
    
    [Column("expiry")]
    public DateTime Expiry { get; set; }
    
    [Column("option_type")]
    public byte OptionType { get; set; } // 0 = Call, 1 = Put
    
    [Column("bid_price")]
    [Precision(18, 8)]
    public decimal BidPrice { get; set; }
    
    [Column("bid_size")]
    public int BidSize { get; set; }
    
    [Column("ask_price")]
    [Precision(18, 8)]
    public decimal AskPrice { get; set; }
    
    [Column("ask_size")]
    public int AskSize { get; set; }
    
    [Column("implied_volatility")]
    public double ImpliedVolatility { get; set; }
    
    [Column("delta")]
    public double Delta { get; set; }
    
    [Column("gamma")]
    public double Gamma { get; set; }
    
    [Column("theta")]
    public double Theta { get; set; }
    
    [Column("vega")]
    public double Vega { get; set; }
    
    [Column("rho")]
    public double Rho { get; set; }
}

[Table("strategy_configs")]
public class StrategyConfigEntity
{
    [Key]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;
    
    [Required]
    [MaxLength(50)]
    [Column("strategy_type")]
    public string StrategyType { get; set; } = string.Empty;
    
    [Column("config_yaml", TypeName = "text")]
    public string ConfigYaml { get; set; } = string.Empty;
    
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
    
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
    
    [Column("is_active")]
    public bool IsActive { get; set; }
}

[Table("backtest_runs")]
public class BacktestRunEntity
{
    [Key]
    public Guid Id { get; set; }
    
    [Required]
    [MaxLength(100)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;
    
    [Column("strategy_config_id")]
    public Guid StrategyConfigId { get; set; }
    
    [ForeignKey(nameof(StrategyConfigId))]
    public StrategyConfigEntity StrategyConfig { get; set; } = null!;
    
    [Column("start_date")]
    public DateTime StartDate { get; set; }
    
    [Column("end_date")]
    public DateTime EndDate { get; set; }
    
    [Column("initial_capital")]
    [Precision(18, 2)]
    public decimal InitialCapital { get; set; }
    
    [Column("final_capital")]
    [Precision(18, 2)]
    public decimal? FinalCapital { get; set; }
    
    [Column("total_return")]
    [Precision(8, 4)]
    public decimal? TotalReturn { get; set; }
    
    [Column("sharpe_ratio")]
    public double? SharpeRatio { get; set; }
    
    [Column("max_drawdown")]
    [Precision(8, 4)]
    public decimal? MaxDrawdown { get; set; }
    
    [Column("win_rate")]
    [Precision(5, 4)]
    public decimal? WinRate { get; set; }
    
    [Column("status")]
    public int Status { get; set; } // 0 = Pending, 1 = Running, 2 = Completed, 3 = Failed
    
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }
    
    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }
    
    [Column("error_message")]
    public string? ErrorMessage { get; set; }
    
    [Column("results_json", TypeName = "jsonb")]
    public string? ResultsJson { get; set; }
}

[Table("portfolio_snapshots")]
[Index(nameof(BacktestRunId), nameof(Timestamp))]
public class PortfolioSnapshotEntity
{
    [Key]
    public long Id { get; set; }
    
    [Column("backtest_run_id")]
    public Guid BacktestRunId { get; set; }
    
    [ForeignKey(nameof(BacktestRunId))]
    public BacktestRunEntity BacktestRun { get; set; } = null!;
    
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }
    
    [Column("portfolio_value")]
    [Precision(18, 2)]
    public decimal PortfolioValue { get; set; }
    
    [Column("unrealized_pnl")]
    [Precision(18, 2)]
    public decimal UnrealizedPnL { get; set; }
    
    [Column("realized_pnl")]
    [Precision(18, 2)]
    public decimal RealizedPnL { get; set; }
    
    [Column("net_delta")]
    public double NetDelta { get; set; }
    
    [Column("net_gamma")]
    public double NetGamma { get; set; }
    
    [Column("net_theta")]
    public double NetTheta { get; set; }
    
    [Column("net_vega")]
    public double NetVega { get; set; }
    
    [Column("positions_json", TypeName = "jsonb")]
    public string? PositionsJson { get; set; }
}
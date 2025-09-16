# Real Historical Data Integration Guide

This document explains how to use the new Polygon API integration to download and backtest with real historical market data.

## Overview

The Optx system now supports both synthetic and real historical data for backtesting. Real data comes from Polygon.io's API and includes:

- **Stock price data**: Daily OHLCV bars for any stock symbol
- **Options data**: Historical options contracts, prices, and quotes
- **2-year history**: Full 2 years of data available on free tier
- **Rate limiting**: Intelligent handling of 5 API calls/minute limit

## Setup

### 1. Get Polygon API Key

1. Sign up at [polygon.io](https://polygon.io)
2. Get your free API key from the dashboard
3. Update your configuration:

```json
{
  "Polygon": {
    "ApiKey": "YOUR_ACTUAL_API_KEY_HERE",
    "RestApiUrl": "https://api.polygon.io"
  }
}
```

### 2. Download Historical Data

#### Download Stock Data
```bash
# Download SPY data for the last year
dotnet run --project src/Optx.CLI -- download-stock \
    --symbols SPY \
    --from 2023-01-01 \
    --to 2024-01-01 \
    --timespan day \
    --output data/real/

# Download multiple symbols
dotnet run --project src/Optx.CLI -- download-stock \
    --symbols SPY,QQQ,AAPL \
    --from 2023-01-01 \
    --to 2024-01-01 \
    --output data/real/
```

#### Download Options Data
```bash
# Download SPY options for specific expiration
dotnet run --project src/Optx.CLI -- download-options \
    --underlying SPY \
    --from 2023-01-01 \
    --to 2024-01-01 \
    --expiration 2023-12-15 \
    --strikes "ATM±10" \
    --output data/real/

# Download all available options (limited by rate limits)
dotnet run --project src/Optx.CLI -- download-options \
    --underlying SPY \
    --from 2023-01-01 \
    --to 2024-01-01 \
    --type both \
    --output data/real/
```

### 3. Run Backtests with Real Data

```bash
# Backtest covered call strategy with real data
dotnet run --project src/Optx.CLI -- backtest \
    --strategy covered-call \
    --data data/real/ \
    --config configs/covered_call_real_data.yaml \
    --output results/real_data_backtest/
```

## Data Formats

### Stock Data Files
- **Format**: Binary (`.bin`) files compatible with existing system
- **Naming**: `{SYMBOL}_ticks_real.bin` (e.g., `SPY_ticks_real.bin`)
- **Content**: Market ticks with timestamp, price, volume

### Options Data Files
- **Format**: JSON files with structured options data
- **Naming**: `{SYMBOL}_options_real.json` (e.g., `SPY_options_real.json`)
- **Content**: Options contracts and historical tick data

### Data Detection
The system automatically detects data types:
- Files with `_real` suffix = Real historical data
- Files without `_real` suffix = Synthetic data
- When both exist, real data takes priority

## Rate Limiting Strategy

The Polygon free tier allows **5 API calls per minute**. The system handles this intelligently:

### Smart Caching
- All downloaded data is cached locally
- Cached data expires after 24 hours (configurable)
- Subsequent requests use cache to avoid API calls

### Batch Download Strategy
```bash
# Download data overnight for multiple symbols
for symbol in SPY QQQ AAPL MSFT; do
    dotnet run --project src/Optx.CLI -- download-stock \
        --symbols $symbol \
        --from 2022-01-01 \
        --to 2024-01-01 \
        --output data/real/
    echo "Completed $symbol, waiting 1 minute..."
    sleep 60
done
```

### Rate Limit Monitoring
The system automatically:
- Tracks API call frequency
- Waits when rate limit is reached
- Logs all rate limiting activity
- Resumes automatically when allowed

## Backtesting Differences

### Real Data vs Synthetic Data

| Aspect | Synthetic Data | Real Data |
|--------|----------------|-----------|
| **Market Events** | Smooth mathematical models | Actual market volatility, gaps, crashes |
| **Options Pricing** | Perfect Black-Scholes | Real bid-ask spreads, liquidity issues |
| **Slippage** | Theoretical | Based on actual spreads |
| **Strategy Performance** | Idealized | Realistic market conditions |

### Enhanced Strategy Testing

Real data enables testing for:
- **Market regime changes** (bull/bear/sideways markets)
- **Volatility spikes** (VIX > 30 periods)
- **Earnings reactions** (actual corporate events)
- **Liquidity dry-ups** (wide bid-ask spreads)

## Configuration

### Real Data Configuration
```yaml
# configs/covered_call_real_data.yaml
strategy:
  name: "CoveredCall"
  parameters:
    symbols: ["SPY"]          # Start with liquid underlyings
    maxPositions: 5           # Reduced positions for realistic testing

risk:
  maxPositionValue: 25000     # Conservative position sizing

backtest:
  initialCash: 50000          # Realistic starting capital
  commission: 0.65            # Actual commission rates
  slippage: 0.02             # Higher slippage for real conditions

data:
  useRealData: true
  dataSource: "polygon"
  priority: "real"
```

## Performance Optimization

### Data Download Tips
1. **Download during off-hours** to maximize API usage
2. **Focus on liquid symbols** (SPY, QQQ, AAPL) for better data quality
3. **Use appropriate timeframes** (daily for strategies, minute for scalping)
4. **Cache everything** - avoid re-downloading the same data

### Storage Optimization
```bash
# Check data storage usage
du -sh data/real/

# Clean old cache files
find cache/ -name "*.json" -mtime +7 -delete
```

## Troubleshooting

### Common Issues

#### 1. API Key Errors
```
Error: Polygon API key is required
```
**Solution**: Verify API key in `appsettings.json`

#### 2. Rate Limit Exceeded
```
Rate limit reached, waiting 60 seconds...
```
**Solution**: This is normal - the system will automatically retry

#### 3. No Data Retrieved
```
No data retrieved for SYMBOL
```
**Solutions**:
- Check if symbol exists on Polygon
- Verify date range is within 2-year limit
- Ensure market was open on those dates

#### 4. File Not Found
```
Error: Data directory not found: data/real/
```
**Solution**: Create directory or run download commands first

### Debug Mode
```bash
# Enable detailed logging
export DOTNET_ENVIRONMENT=Development
dotnet run --project src/Optx.CLI -- download-stock --symbols SPY --verbose
```

## Example Workflows

### Workflow 1: Single Symbol Testing
```bash
# 1. Download SPY data
dotnet run --project src/Optx.CLI -- download-stock \
    --symbols SPY --from 2023-01-01 --to 2024-01-01 --output data/real/

# 2. Download SPY options
dotnet run --project src/Optx.CLI -- download-options \
    --underlying SPY --from 2023-01-01 --to 2024-01-01 --output data/real/

# 3. Run backtest
dotnet run --project src/Optx.CLI -- backtest \
    --strategy covered-call --data data/real/ \
    --config configs/covered_call_real_data.yaml
```

### Workflow 2: Multi-Symbol Comparison
```bash
# Download data for multiple symbols
for symbol in SPY QQQ IWM; do
    dotnet run --project src/Optx.CLI -- download-stock \
        --symbols $symbol --from 2023-01-01 --to 2024-01-01 --output data/real/
done

# Test strategy on each symbol
for symbol in SPY QQQ IWM; do
    # Update config for specific symbol
    sed "s/SPY/$symbol/g" configs/covered_call_real_data.yaml > configs/temp_${symbol}.yaml
    
    # Run backtest
    dotnet run --project src/Optx.CLI -- backtest \
        --strategy covered-call --data data/real/ \
        --config configs/temp_${symbol}.yaml \
        --output results/${symbol}_backtest/
done
```

### Workflow 3: Strategy Optimization
```bash
# Download comprehensive dataset
dotnet run --project src/Optx.CLI -- download-stock \
    --symbols SPY --from 2022-01-01 --to 2024-01-01 --output data/real/

# Test different time periods
dotnet run --project src/Optx.CLI -- backtest \
    --data data/real/ --config configs/covered_call_real_data.yaml \
    --output results/bull_market_2023/

# Test during volatile periods (2022 bear market)
# Modify config dates and rerun...
```

## Next Steps

1. **Implement additional data sources** (Alpha Vantage, Yahoo Finance)
2. **Add minute-level data support** for intraday strategies
3. **Enhance options chain reconstruction** for complete historical chains
4. **Add Greeks validation** against market-implied values
5. **Implement walk-forward optimization** across different market regimes

## Support

For issues with real data integration:
1. Check the [troubleshooting section](#troubleshooting)
2. Review Polygon API documentation
3. File issues in the project repository
4. Join discussions in project channels
using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketController : ControllerBase
{
    [HttpGet("prices")]
    public ActionResult<List<PriceTicker>> GetPrices([FromQuery] string? assets = null)
    {
        var tickers = new List<PriceTicker>
        {
            new()
            {
                Asset = "BTC/USD",
                Price = 45200.00m,
                Change24h = 540.00m,
                ChangePercent24h = 1.21m,
                Bid = 45195.00m,
                Ask = 45205.00m,
                High24h = 45800.00m,
                Low24h = 44600.00m,
                Volume24h = 28500000000m,
                LastUpdated = DateTime.UtcNow
            },
            new()
            {
                Asset = "ETH/USD",
                Price = 2890.00m,
                Change24h = 45.00m,
                ChangePercent24h = 1.58m,
                Bid = 2888.00m,
                Ask = 2892.00m,
                High24h = 2920.00m,
                Low24h = 2840.00m,
                Volume24h = 12500000000m,
                LastUpdated = DateTime.UtcNow
            },
            new()
            {
                Asset = "SOL/USD",
                Price = 98.40m,
                Change24h = -2.10m,
                ChangePercent24h = -2.09m,
                Bid = 98.35m,
                Ask = 98.45m,
                High24h = 101.50m,
                Low24h = 97.20m,
                Volume24h = 1800000000m,
                LastUpdated = DateTime.UtcNow
            }
        };
        
        if (!string.IsNullOrEmpty(assets))
        {
            var assetList = assets.Split(',');
            tickers = tickers.Where(t => assetList.Contains(t.Asset)).ToList();
        }
        
        return Ok(tickers);
    }

    [HttpGet("indicators")]
    public ActionResult<List<TechnicalIndicator>> GetIndicators([FromQuery] string asset, [FromQuery] string timeframe = "1h")
    {
        return Ok(new List<TechnicalIndicator>
        {
            new()
            {
                Asset = asset,
                Timeframe = timeframe,
                Rsi = 62.4m,
                Macd = 125.50m,
                MacdSignal = 118.30m,
                MacdHistogram = 7.20m,
                BollingerUpper = 46200.00m,
                BollingerMiddle = 45200.00m,
                BollingerLower = 44200.00m,
                Signal = "neutral",
                LastUpdated = DateTime.UtcNow
            }
        });
    }

    [HttpGet("macro")]
    public ActionResult<MacroSignals> GetMacroSignals()
    {
        return Ok(new MacroSignals
        {
            Trend = "bullish",
            VolatilityRegime = "normal",
            FearGreedIndex = 65,
            Sentiment = "greed",
            BtcDominance = 52.3m,
            TotalMarketCap = 1.72m,
            LastUpdated = DateTime.UtcNow
        });
    }

    [HttpGet("ohlcv")]
    public ActionResult<List<Ohlcv>> GetOhlcv([FromQuery] string asset, [FromQuery] string timeframe = "1h", [FromQuery] int limit = 100)
    {
        var candles = new List<Ohlcv>();
        var basePrice = asset switch
        {
            "BTC/USD" => 45000m,
            "ETH/USD" => 2850m,
            _ => 100m
        };
        
        var random = new Random(42);
        var now = DateTime.UtcNow;
        
        for (int i = limit - 1; i >= 0; i--)
        {
            var timestamp = timeframe switch
            {
                "1m" => now.AddMinutes(-i),
                "5m" => now.AddMinutes(-i * 5),
                "15m" => now.AddMinutes(-i * 15),
                "1h" => now.AddHours(-i),
                "4h" => now.AddHours(-i * 4),
                "1d" => now.AddDays(-i),
                _ => now.AddHours(-i)
            };
            
            var volatility = basePrice * 0.02m;
            var open = basePrice + (decimal)(random.NextDouble() * (double)volatility - (double)volatility / 2);
            var close = open + (decimal)(random.NextDouble() * (double)volatility - (double)volatility / 2);
            var high = Math.Max(open, close) + (decimal)(random.NextDouble() * (double)volatility / 2);
            var low = Math.Min(open, close) - (decimal)(random.NextDouble() * (double)volatility / 2);
            
            candles.Add(new Ohlcv
            {
                Timestamp = timestamp,
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = (decimal)(random.NextDouble() * 1000000)
            });
            
            basePrice = close;
        }
        
        return Ok(candles);
    }
}

using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MdsPriceTicker = MonitoringDashboard.Services.PriceTicker;
using MdsOhlcv = MonitoringDashboard.Services.Ohlcv;
using MdsTechnicalIndicator = MonitoringDashboard.Services.TechnicalIndicator;
using MdsMacroSignals = MonitoringDashboard.Services.MacroSignals;
using IMarketDataServiceClient = MonitoringDashboard.Services.IMarketDataServiceClient;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MarketController : ControllerBase
{
    private readonly IMarketDataServiceClient _marketDataClient;
    private readonly ILogger<MarketController> _logger;

    public MarketController(IMarketDataServiceClient marketDataClient, ILogger<MarketController> logger)
    {
        _marketDataClient = marketDataClient;
        _logger = logger;
    }

    [HttpGet("prices")]
    public async Task<ActionResult<List<MdsPriceTicker>>> GetPrices([FromQuery] string? assets = null)
    {
        try
        {
            var symbols = string.IsNullOrEmpty(assets) 
                ? new[] { "BTC/USD", "ETH/USD", "SOL/USD" }
                : assets.Split(',');
            
            var tickers = await _marketDataClient.GetPricesAsync(symbols);
            return Ok(tickers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get prices from Market Data Service");
            return StatusCode(503, new { error = "Market Data Service unavailable" });
        }
    }

    [HttpGet("indicators")]
    public async Task<ActionResult<MdsTechnicalIndicator>> GetIndicators([FromQuery] string asset, [FromQuery] string timeframe = "1h")
    {
        try
        {
            var symbol = asset.Contains("/") ? asset : $"{asset}/USD";
            var indicator = await _marketDataClient.GetIndicatorsAsync(symbol, timeframe);
            if (indicator != null)
            {
                return Ok(indicator);
            }
            return NotFound();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indicators from Market Data Service");
            return StatusCode(503, new { error = "Market Data Service unavailable" });
        }
    }

    [HttpGet("macro")]
    public async Task<ActionResult<MacroSignals>> GetMacroSignals()
    {
        try
        {
            var result = await _marketDataClient.GetMacroSignalsAsync();
            if (result is null)
                return StatusCode(503, new { error = "Market Data Service unavailable or Kraken data not yet ready." });

            return Ok(new MacroSignals
            {
                Trend            = result.Trend,
                VolatilityRegime = result.VolatilityRegime,
                FearGreedIndex   = result.FearGreedIndex,
                Sentiment        = result.Sentiment,
                BtcDominance     = result.BtcDominance,
                TotalMarketCap   = result.TotalMarketCap,
                LastUpdated      = result.LastUpdated
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get macro signals from Market Data Service");
            return StatusCode(503, new { error = "Market Data Service unavailable" });
        }
    }

    [HttpGet("ohlcv")]
    public async Task<ActionResult<List<MdsOhlcv>>> GetOhlcv([FromQuery] string asset, [FromQuery] string timeframe = "1h", [FromQuery] int limit = 100)
    {
        try
        {
            var symbol = asset.Contains("/") ? asset : $"{asset}/USD";
            var candles = await _marketDataClient.GetOhlcvAsync(symbol, timeframe, limit);
            return Ok(candles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OHLCV from Market Data Service");
            return StatusCode(503, new { error = "Market Data Service unavailable" });
        }
    }
}

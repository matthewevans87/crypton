using MarketDataService.Adapters;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketDataService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TradesController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<TradesController> _logger;

    public TradesController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<TradesController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Trade>>> GetTrades(
        [FromQuery] string symbol = "BTC/USD",
        [FromQuery] int limit = 50)
    {
        try
        {
            var trades = await _exchangeAdapter.GetTradesAsync(symbol, limit);
            return Ok(trades);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get trades for {Symbol}", symbol);
            return StatusCode(500, new { error = "Failed to retrieve trades" });
        }
    }
}

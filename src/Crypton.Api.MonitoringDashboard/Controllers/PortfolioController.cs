using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using MdsPortfolioSummary = MonitoringDashboard.Services.PortfolioSummary;
using MdsPosition = MonitoringDashboard.Services.Position;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfolioController : ControllerBase
{
    private readonly IMarketDataServiceClient _marketDataClient;
    private readonly ILogger<PortfolioController> _logger;

    public PortfolioController(IMarketDataServiceClient marketDataClient, ILogger<PortfolioController> logger)
    {
        _marketDataClient = marketDataClient;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MdsPortfolioSummary>> GetSummary()
    {
        try
        {
            var summary = await _marketDataClient.GetPortfolioSummaryAsync();
            return Ok(summary);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get portfolio summary from Market Data Service");
            return StatusCode(503, new { error = "Market Data Service unavailable" });
        }
    }

    [HttpGet("holdings")]
    public async Task<ActionResult<List<Holding>>> GetHoldings()
    {
        try
        {
            var balances = await _marketDataClient.GetBalanceAsync();
            var prices = await _marketDataClient.GetPricesAsync();
            
            var holdings = new List<Holding>();
            decimal totalValue = 0;
            
            foreach (var balance in balances)
            {
                var price = prices.FirstOrDefault(p => p.Asset.StartsWith(balance.Asset));
                var value = balance.Total * (price?.Price ?? 1);
                totalValue += value;
            }
            
            foreach (var balance in balances)
            {
                var price = prices.FirstOrDefault(p => p.Asset.StartsWith(balance.Asset));
                var value = balance.Total * (price?.Price ?? 1);
                
                holdings.Add(new Holding
                {
                    Asset = balance.Asset,
                    Quantity = balance.Total,
                    CurrentPrice = price?.Price ?? 0,
                    AllocationPercent = totalValue > 0 ? (value / totalValue) * 100 : 0
                });
            }
            
            return Ok(holdings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get holdings from Market Data Service");
            return StatusCode(503, new { error = "Market Data Service unavailable" });
        }
    }

    [HttpGet("positions")]
    public ActionResult<List<MdsPosition>> GetPositions()
    {
        return Ok(new List<MdsPosition>());
    }

    [HttpGet("trades")]
    public ActionResult<List<Trade>> GetTrades([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        return Ok(new List<Trade>());
    }
}

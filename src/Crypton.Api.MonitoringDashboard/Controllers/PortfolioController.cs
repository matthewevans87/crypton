using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using MdsPortfolioSummary = MonitoringDashboard.Services.PortfolioSummary;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfolioController : ControllerBase
{
    private readonly IMarketDataServiceClient _marketDataClient;
    private readonly IExecutionServiceClient _executionServiceClient;
    private readonly ILogger<PortfolioController> _logger;

    public PortfolioController(
        IMarketDataServiceClient marketDataClient,
        IExecutionServiceClient executionServiceClient,
        ILogger<PortfolioController> logger)
    {
        _marketDataClient = marketDataClient;
        _executionServiceClient = executionServiceClient;
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

    /// <summary>
    /// Returns open positions proxied from ExecutionService GET /positions.
    /// JSON shape: ExecutionService.Positions.OpenPosition (snake_case).
    /// </summary>
    [HttpGet("positions")]
    public async Task<IActionResult> GetPositions(CancellationToken ct)
    {
        var (_, body) = await _executionServiceClient.GetPositionsAsync(ct);
        return Content(body, "application/json", System.Text.Encoding.UTF8);
    }

    /// <summary>
    /// Returns closed trades proxied from ExecutionService GET /trades.
    /// JSON shape: ExecutionService.Positions.ClosedTrade (snake_case).
    /// </summary>
    [HttpGet("trades")]
    public async Task<IActionResult> GetTrades(CancellationToken ct)
    {
        var (_, body) = await _executionServiceClient.GetTradesAsync(ct);
        return Content(body, "application/json", System.Text.Encoding.UTF8);
    }
}

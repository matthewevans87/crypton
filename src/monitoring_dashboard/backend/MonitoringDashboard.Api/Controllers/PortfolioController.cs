using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PortfolioController : ControllerBase
{
    [HttpGet("summary")]
    public ActionResult<PortfolioSummary> GetSummary()
    {
        return Ok(new PortfolioSummary
        {
            TotalValue = 127432.00m,
            Change24h = 3000.00m,
            ChangePercent24h = 2.4m,
            UnrealizedPnL = 1500.00m,
            AvailableCapital = 25000.00m,
            LastUpdated = DateTime.UtcNow
        });
    }

    [HttpGet("holdings")]
    public ActionResult<List<Holding>> GetHoldings()
    {
        return Ok(new List<Holding>
        {
            new() { Asset = "BTC", Quantity = 2.5m, CurrentPrice = 45200.00m, AllocationPercent = 88.6m },
            new() { Asset = "ETH", Quantity = 5.0m, CurrentPrice = 2890.00m, AllocationPercent = 11.3m },
            new() { Asset = "USD", Quantity = 25000.00m, CurrentPrice = 1.0m, AllocationPercent = 0.1m }
        });
    }

    [HttpGet("positions")]
    public ActionResult<List<Position>> GetPositions()
    {
        return Ok(new List<Position>
        {
            new()
            {
                Id = "pos-001",
                Asset = "BTC",
                Direction = "long",
                EntryPrice = 44000.00m,
                CurrentPrice = 45200.00m,
                Size = 1.5m,
                StopLoss = 42000.00m,
                TakeProfit = 48000.00m,
                UnrealizedPnL = 1800.00m,
                UnrealizedPnLPercent = 2.73m,
                OpenedAt = DateTime.UtcNow.AddHours(-6)
            },
            new()
            {
                Id = "pos-002",
                Asset = "ETH",
                Direction = "long",
                EntryPrice = 2750.00m,
                CurrentPrice = 2890.00m,
                Size = 3.0m,
                StopLoss = 2600.00m,
                TakeProfit = 3100.00m,
                UnrealizedPnL = 420.00m,
                UnrealizedPnLPercent = 5.09m,
                OpenedAt = DateTime.UtcNow.AddHours(-2)
            }
        });
    }

    [HttpGet("trades")]
    public ActionResult<List<Trade>> GetTrades([FromQuery] int limit = 50, [FromQuery] int offset = 0)
    {
        var trades = new List<Trade>
        {
            new()
            {
                Id = "trade-001",
                Asset = "BTC",
                Direction = "long",
                EntryPrice = 44000.00m,
                ExitPrice = 45200.00m,
                Size = 1.5m,
                PnL = 1800.00m,
                PnLPercent = 2.73m,
                EntryTime = DateTime.UtcNow.AddDays(-3),
                ExitTime = DateTime.UtcNow.AddHours(-6),
                ExitReason = "strategy_update"
            },
            new()
            {
                Id = "trade-002",
                Asset = "ETH",
                Direction = "long",
                EntryPrice = 2600.00m,
                ExitPrice = 2750.00m,
                Size = 2.0m,
                PnL = 300.00m,
                PnLPercent = 5.77m,
                EntryTime = DateTime.UtcNow.AddDays(-5),
                ExitTime = DateTime.UtcNow.AddDays(-2),
                ExitReason = "take_profit"
            }
        };
        
        return Ok(trades.Take(limit).Skip(offset).ToList());
    }
}

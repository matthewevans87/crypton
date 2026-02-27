using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StrategyController : ControllerBase
{
    [HttpGet("current")]
    public ActionResult<Strategy> GetCurrent()
    {
        return Ok(new Strategy
        {
            Overview = new StrategyOverview
            {
                Mode = "paper",
                Posture = "moderate",
                ValidUntil = DateTime.UtcNow.AddHours(4).AddMinutes(32),
                Rationale = "BTC showing bullish divergence on RSI while ETH consolidates. Maintaining moderate exposure with tight stops.",
                MaxDrawdown = 5.0m,
                DailyLossLimit = 2.0m,
                MaxExposure = 50.0m,
                LastUpdated = DateTime.UtcNow
            },
            PositionRules = new List<PositionRule>
            {
                new()
                {
                    Asset = "BTC",
                    EntryCondition = "price > 44000 AND rsi(14) < 70",
                    Allocation = 30.0m,
                    StopLoss = 42000.00m,
                    TakeProfitTargets = new List<TakeProfitTarget>
                    {
                        new() { Price = 48000.00m, ClosePercent = 50 },
                        new() { Price = 52000.00m, ClosePercent = 100 }
                    },
                    InvalidationCondition = "price < 42000"
                },
                new()
                {
                    Asset = "ETH",
                    EntryCondition = "price > 2800 AND macd > 0",
                    Allocation = 20.0m,
                    StopLoss = 2600.00m,
                    TakeProfitTargets = new List<TakeProfitTarget>
                    {
                        new() { Price = 3100.00m, ClosePercent = 100 }
                    },
                    InvalidationCondition = "price < 2600"
                }
            }
        });
    }

    [HttpGet("history")]
    public ActionResult<List<StrategyHistoryItem>> GetHistory([FromQuery] int limit = 20)
    {
        var history = new List<StrategyHistoryItem>
        {
            new() { Id = "strat-001", CreatedAt = DateTime.UtcNow.AddHours(-6), Mode = "paper", Posture = "moderate", PositionCount = 2 },
            new() { Id = "strat-002", CreatedAt = DateTime.UtcNow.AddDays(-1), Mode = "paper", Posture = "defensive", PositionCount = 1 },
            new() { Id = "strat-003", CreatedAt = DateTime.UtcNow.AddDays(-2), Mode = "paper", Posture = "aggressive", PositionCount = 3 }
        };
        
        return Ok(history.Take(limit).ToList());
    }

    [HttpGet("{id}")]
    public ActionResult<Strategy> GetById(string id)
    {
        return Ok(new Strategy
        {
            Overview = new StrategyOverview
            {
                Mode = "paper",
                Posture = "moderate",
                ValidUntil = DateTime.UtcNow.AddHours(-2),
                Rationale = "Sample historical strategy",
                MaxDrawdown = 5.0m,
                DailyLossLimit = 2.0m,
                MaxExposure = 50.0m,
                LastUpdated = DateTime.UtcNow.AddHours(-6)
            },
            PositionRules = []
        });
    }
}

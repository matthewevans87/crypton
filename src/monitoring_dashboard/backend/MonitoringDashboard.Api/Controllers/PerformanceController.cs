using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerformanceController : ControllerBase
{
    [HttpGet("cycle")]
    public ActionResult<CyclePerformance> GetCurrentCycle()
    {
        return Ok(new CyclePerformance
        {
            CycleId = "cycle-007",
            StartDate = DateTime.UtcNow.AddDays(-1),
            RealizedPnL = 450.00m,
            UnrealizedPnL = 2220.00m,
            WinRate = 66.67m,
            AvgWin = 350.00m,
            AvgLoss = -125.00m,
            MaxDrawdown = 1.8m,
            TotalTrades = 6,
            WinningTrades = 4,
            LosingTrades = 2,
            DailyLossLimitBreached = false
        });
    }

    [HttpGet("lifetime")]
    public ActionResult<LifetimePerformance> GetLifetime()
    {
        return Ok(new LifetimePerformance
        {
            TotalPnL = 12450.00m,
            TotalReturn = 12.45m,
            WinRate = 58.5m,
            TotalTrades = 87,
            WinningTrades = 51,
            LosingTrades = 36,
            LongestWinningStreak = 8,
            LongestLosingStreak = 4,
            SharpeRatio = 1.42m
        });
    }

    [HttpGet("cycles")]
    public ActionResult<List<CyclePerformance>> GetCycleHistory([FromQuery] int limit = 20)
    {
        var cycles = new List<CyclePerformance>
        {
            new()
            {
                CycleId = "cycle-007",
                StartDate = DateTime.UtcNow.AddDays(-1),
                RealizedPnL = 450.00m,
                UnrealizedPnL = 2220.00m,
                WinRate = 66.67m,
                MaxDrawdown = 1.8m,
                TotalTrades = 6,
                WinningTrades = 4,
                LosingTrades = 2
            },
            new()
            {
                CycleId = "cycle-006",
                StartDate = DateTime.UtcNow.AddDays(-2),
                EndDate = DateTime.UtcNow.AddDays(-1),
                RealizedPnL = -280.00m,
                UnrealizedPnL = 0,
                WinRate = 40.0m,
                MaxDrawdown = 2.1m,
                TotalTrades = 5,
                WinningTrades = 2,
                LosingTrades = 3
            },
            new()
            {
                CycleId = "cycle-005",
                StartDate = DateTime.UtcNow.AddDays(-3),
                EndDate = DateTime.UtcNow.AddDays(-2),
                RealizedPnL = 1250.00m,
                UnrealizedPnL = 0,
                WinRate = 75.0m,
                MaxDrawdown = 1.2m,
                TotalTrades = 8,
                WinningTrades = 6,
                LosingTrades = 2
            }
        };
        
        return Ok(cycles.Take(limit).ToList());
    }

    [HttpGet("evaluation")]
    public ActionResult<EvaluationSummary> GetLatestEvaluation()
    {
        return Ok(new EvaluationSummary
        {
            CycleId = "cycle-006",
            EvaluatedAt = DateTime.UtcNow.AddDays(-1),
            Rating = "B",
            NetPnL = -280.00m,
            Return = -0.28m,
            MaxDrawdown = 2.1m,
            WinRate = 40.0m,
            TotalTrades = 5,
            Verdict = "Cycle underperformed due to adverse market conditions. Risk management worked as intended.",
            Recommendations = new List<string>
            {
                "Consider reducing position size during high volatility",
                "Review ETH entry criteria - false breakout triggered stop",
                "Maintain current posture for next cycle"
            },
            RatingTrend = "down"
        });
    }
}

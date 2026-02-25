using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AgentController : ControllerBase
{
    [HttpGet("state")]
    public ActionResult<AgentState> GetState()
    {
        return Ok(new AgentState
        {
            CurrentState = "Analyze",
            ActiveAgent = "Analysis Agent",
            StateStartedAt = DateTime.UtcNow.AddMinutes(-12),
            IsRunning = true,
            ProgressPercent = 45.0,
            CurrentTool = "technical_indicators",
            TokensUsed = 2341,
            LastLatencyMs = 1.2
        });
    }

    [HttpGet("loop")]
    public ActionResult<LoopStatus> GetLoopStatus()
    {
        return Ok(new LoopStatus
        {
            AgentState = new AgentState
            {
                CurrentState = "Analyze",
                ActiveAgent = "Analysis Agent",
                StateStartedAt = DateTime.UtcNow.AddMinutes(-12),
                IsRunning = true,
                ProgressPercent = 45.0
            },
            LastCycleCompletedAt = DateTime.UtcNow.AddHours(-6),
            NextCycleExpectedAt = DateTime.UtcNow.AddHours(18),
            CurrentArtifact = "analysis.md",
            CycleNumber = 7
        });
    }

    [HttpGet("toolcalls")]
    public ActionResult<List<ToolCall>> GetToolCalls([FromQuery] int limit = 20)
    {
        return Ok(new List<ToolCall>
        {
            new()
            {
                Id = "tool-001",
                ToolName = "web_search",
                Input = "{\"query\": \"Bitcoin ETF approval news February 2026\"}",
                Output = "{\"results\": [{\"title\": \"SEC Updates\", \"url\": \"...\"}]}",
                CalledAt = DateTime.UtcNow.AddMinutes(-15),
                DurationMs = 1250,
                IsCompleted = true,
                IsError = false
            },
            new()
            {
                Id = "tool-002",
                ToolName = "technical_indicators",
                Input = "{\"asset\": \"BTC\", \"timeframe\": \"4h\"}",
                Output = "{\"rsi\": 62, \"macd\": 125, \"bb_upper\": 46200}",
                CalledAt = DateTime.UtcNow.AddMinutes(-10),
                DurationMs = 45,
                IsCompleted = true,
                IsError = false
            },
            new()
            {
                Id = "tool-003",
                ToolName = "web_fetch",
                Input = "{\"url\": \"https://coindesk.com/market-analysis\"}",
                Output = "...",
                CalledAt = DateTime.UtcNow.AddMinutes(-8),
                DurationMs = 2340,
                IsCompleted = true,
                IsError = false
            }
        });
    }

    [HttpGet("reasoning")]
    public ActionResult<List<ReasoningStep>> GetReasoning()
    {
        return Ok(new List<ReasoningStep>
        {
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-12), Content = "Analyzing BTC price action on the 4h timeframe..." },
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-11), Content = "RSI showing 62 - neutral, neither overbought nor oversold" },
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-10), Content = "MACD histogram turning positive, suggesting bullish momentum building" },
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-9), Content = "Price respecting the 20-period moving average as support" },
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-8), Content = "Volume picking up on the recent move up - confirming the breakout" },
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-7), Content = "Looking at ETH correlation - strong positive correlation with BTC" },
            new() { Timestamp = DateTime.UtcNow.AddMinutes(-6), Content = "Current position: long BTC from 44k, unrealized P&L +2.7%" }
        });
    }
}

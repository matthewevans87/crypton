using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using System.Text.Json;

namespace MonitoringDashboard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PerformanceController : ControllerBase
{
    private readonly IAgentRunnerClient _agentRunnerClient;
    private readonly IExecutionServiceClient _executionServiceClient;
    private readonly ILogger<PerformanceController> _logger;

    public PerformanceController(
        IAgentRunnerClient agentRunnerClient,
        IExecutionServiceClient executionServiceClient,
        ILogger<PerformanceController> logger)
    {
        _agentRunnerClient = agentRunnerClient;
        _executionServiceClient = executionServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns performance stats for the current cycle, derived from ExecutionService closed trades.
    /// </summary>
    [HttpGet("cycle")]
    public async Task<ActionResult<CyclePerformance>> GetCurrentCycle(CancellationToken ct)
    {
        var statusTask = _agentRunnerClient.GetStatusAsync(ct);
        var tradesTask = _executionServiceClient.GetTradesAsync(ct);
        await Task.WhenAll(statusTask, tradesTask);

        var cycleId = "current";
        if (statusTask.Result is { } status &&
            status.TryGetProperty("currentCycleId", out var cid) && cid.ValueKind != JsonValueKind.Null)
            cycleId = cid.GetString() ?? "current";

        var trades = ParseTradeList(tradesTask.Result.Body);
        return Ok(BuildCyclePerformance(cycleId, DateTime.UtcNow.AddHours(-24), null, trades));
    }

    /// <summary>
    /// Returns lifetime aggregated performance from all ExecutionService closed trades.
    /// </summary>
    [HttpGet("lifetime")]
    public async Task<ActionResult<LifetimePerformance>> GetLifetime(CancellationToken ct)
    {
        var (_, body) = await _executionServiceClient.GetTradesAsync(ct);
        var trades = ParseTradeList(body);

        var wins = trades.Where(t => t.Pnl > 0).ToList();
        var losses = trades.Where(t => t.Pnl <= 0).ToList();

        int winStreak = 0, lossStreak = 0, maxWin = 0, maxLoss = 0;
        foreach (var t in trades)
        {
            if (t.Pnl > 0) { winStreak++; lossStreak = 0; maxWin = Math.Max(maxWin, winStreak); }
            else { lossStreak++; winStreak = 0; maxLoss = Math.Max(maxLoss, lossStreak); }
        }

        return Ok(new LifetimePerformance
        {
            TotalPnL = trades.Sum(t => t.Pnl),
            TotalReturn = 0,   // requires initial capital — not available
            WinRate = trades.Count > 0 ? (decimal)wins.Count / trades.Count * 100 : 0,
            TotalTrades = trades.Count,
            WinningTrades = wins.Count,
            LosingTrades = losses.Count,
            LongestWinningStreak = maxWin,
            LongestLosingStreak = maxLoss,
            SharpeRatio = null
        });
    }

    /// <summary>
    /// Returns a list of cycle metadata from AgentRunner; financial stats are not available per-cycle.
    /// </summary>
    [HttpGet("cycles")]
    public async Task<ActionResult<List<CyclePerformance>>> GetCycleHistory(
        [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var cycles = await _agentRunnerClient.GetCyclesAsync(limit, ct);
        if (cycles is null) return Ok(new List<CyclePerformance>());

        var result = new List<CyclePerformance>();
        foreach (var el in cycles.Value.EnumerateArray())
        {
            var cycleId = el.TryGetProperty("cycleId", out var cid) ? cid.GetString() ?? "" : "";
            result.Add(new CyclePerformance
            {
                CycleId = cycleId,
                StartDate = ParseCycleDateFromId(cycleId),
            });
        }
        return Ok(result);
    }

    /// <summary>
    /// Returns the evaluation summary for the latest completed cycle, read from its evaluation.md artifact.
    /// </summary>
    [HttpGet("evaluation")]
    public async Task<ActionResult<EvaluationSummary>> GetLatestEvaluation(CancellationToken ct)
    {
        var cycles = await _agentRunnerClient.GetCyclesAsync(1, ct);
        if (cycles is null || cycles.Value.GetArrayLength() == 0)
            return Ok(new EvaluationSummary { CycleId = "-", Verdict = "No completed cycles yet." });

        var latestCycleId = cycles.Value[0].TryGetProperty("cycleId", out var cid)
            ? cid.GetString() ?? "" : "";

        var details = await _agentRunnerClient.GetCycleDetailsAsync(latestCycleId, ct);
        string? evaluationText = null;
        if (details is not null &&
            details.Value.TryGetProperty("artifacts", out var artifacts) &&
            artifacts.TryGetProperty("evaluation.md", out var evalEl) &&
            evalEl.ValueKind != JsonValueKind.Null)
        {
            evaluationText = evalEl.GetString();
        }

        return Ok(new EvaluationSummary
        {
            CycleId = latestCycleId,
            EvaluatedAt = ParseCycleDateFromId(latestCycleId),
            Verdict = evaluationText ?? "No evaluation.md artifact found for this cycle.",
        });
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private sealed record EsTrade(decimal Pnl, string Asset, string Direction,
        decimal EntryPrice, decimal ExitPrice, decimal Quantity,
        DateTimeOffset OpenedAt, DateTimeOffset ClosedAt, string ExitReason, string Id);

    private static List<EsTrade> ParseTradeList(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];
            var result = new List<EsTrade>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                result.Add(new EsTrade(
                    Pnl: el.TryGetProperty("realized_pnl", out var p) ? p.GetDecimal() : 0m,
                    Asset: el.TryGetProperty("asset", out var a) ? a.GetString() ?? "" : "",
                    Direction: el.TryGetProperty("direction", out var d) ? d.GetString() ?? "" : "",
                    EntryPrice: el.TryGetProperty("entry_price", out var ep) ? ep.GetDecimal() : 0m,
                    ExitPrice: el.TryGetProperty("exit_price", out var xp) ? xp.GetDecimal() : 0m,
                    Quantity: el.TryGetProperty("quantity", out var q) ? q.GetDecimal() : 0m,
                    OpenedAt: el.TryGetProperty("opened_at", out var oa) && oa.ValueKind != JsonValueKind.Null
                        ? oa.GetDateTimeOffset() : DateTimeOffset.MinValue,
                    ClosedAt: el.TryGetProperty("closed_at", out var ca) && ca.ValueKind != JsonValueKind.Null
                        ? ca.GetDateTimeOffset() : DateTimeOffset.MinValue,
                    ExitReason: el.TryGetProperty("exit_reason", out var er) ? er.GetString() ?? "" : "",
                    Id: el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : ""
                ));
            }
            return result;
        }
        catch { return []; }
    }

    private static CyclePerformance BuildCyclePerformance(
        string cycleId, DateTime startDate, DateTime? endDate, List<EsTrade> trades)
    {
        var wins = trades.Where(t => t.Pnl > 0).ToList();
        var losses = trades.Where(t => t.Pnl <= 0).ToList();
        return new CyclePerformance
        {
            CycleId = cycleId,
            StartDate = startDate,
            EndDate = endDate,
            RealizedPnL = trades.Sum(t => t.Pnl),
            WinRate = trades.Count > 0 ? (decimal)wins.Count / trades.Count * 100 : 0,
            AvgWin = wins.Count > 0 ? wins.Average(t => t.Pnl) : 0,
            AvgLoss = losses.Count > 0 ? losses.Average(t => t.Pnl) : 0,
            MaxDrawdown = 0,   // requires equity curve — not computed here
            TotalTrades = trades.Count,
            WinningTrades = wins.Count,
            LosingTrades = losses.Count,
        };
    }

    private static DateTime ParseCycleDateFromId(string cycleId) =>
        DateTime.TryParse(cycleId, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt)
            ? dt.ToUniversalTime()
            : DateTime.MinValue;
}

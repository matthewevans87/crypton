using Microsoft.AspNetCore.Mvc;
using MonitoringDashboard.Models;
using MonitoringDashboard.Services;
using System.Text.Json;

namespace MonitoringDashboard.Controllers;

/// <summary>
/// Aggregates live health + state from every upstream service into a single response
/// for the "System Diagnostics" panel and the status-bar service chips.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SystemController : ControllerBase
{
    private readonly IMarketDataServiceClient _marketData;
    private readonly IExecutionServiceClient _execution;
    private readonly IAgentRunnerClient _agentRunner;
    private readonly ILogger<SystemController> _logger;

    public SystemController(
        IMarketDataServiceClient marketData,
        IExecutionServiceClient execution,
        IAgentRunnerClient agentRunner,
        ILogger<SystemController> logger)
    {
        _marketData = marketData;
        _execution = execution;
        _agentRunner = agentRunner;
        _logger = logger;
    }

    /// <summary>
    /// Returns current status for MarketData, ExecutionService, and AgentRunner.
    /// Each service check runs in parallel with a 4-second timeout.
    /// Never throws to the caller — offline services return status:"offline".
    /// </summary>
    [HttpGet("status")]
    public async Task<ActionResult<SystemStatus>> GetStatus(CancellationToken requestCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(requestCt);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        var ct = cts.Token;

        var mdTask = CheckMarketDataAsync(ct);
        var esTask = CheckExecutionServiceAsync(ct);
        var arTask = CheckAgentRunnerAsync(ct);

        await Task.WhenAll(mdTask, esTask, arTask);

        return Ok(new SystemStatus
        {
            Services = [await mdTask, await esTask, await arTask],
            CheckedAt = DateTime.UtcNow,
        });
    }

    // -----------------------------------------------------------------------
    // Per-service checks
    // -----------------------------------------------------------------------

    private async Task<ServiceHealth> CheckMarketDataAsync(CancellationToken ct)
    {
        try
        {
            var (statusCode, body) = await _marketData.GetRawMetricsAsync(ct);

            if (statusCode >= 500 || statusCode == 503)
                return Offline("MarketData", _marketData.IsConnected);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var isHealthy = root.TryGetProperty("isHealthy", out var ih) && ih.GetBoolean();
            var alerts = root.TryGetProperty("activeAlerts", out var aa) && aa.ValueKind == JsonValueKind.Array
                ? aa.EnumerateArray()
                    .Select(a => a.TryGetProperty("message", out var m) ? m.GetString() : null)
                    .OfType<string>()
                    .ToList()
                : [];

            var metrics = root.TryGetProperty("metrics", out var m2) && m2.ValueKind == JsonValueKind.Object
                ? m2.EnumerateObject().ToDictionary(p => p.Name, p => (object?)p.Value.ToString())
                : new Dictionary<string, object?>();

            // Augment with keys we always want visible
            var wsConnected = metrics.TryGetValue("wsConnected", out var wsc) && wsc?.ToString() == "True";
            var alertCount = alerts.Count;

            string detail;
            string status;
            if (!isHealthy || alertCount > 0)
            {
                status = "degraded";
                var alertSummary = alertCount == 1 ? alerts[0] : $"{alertCount} active alerts";
                detail = wsConnected
                    ? $"Degraded — {alertSummary}"
                    : $"Exchange disconnected — {alertSummary}";
            }
            else
            {
                status = "online";
                detail = wsConnected ? "Streaming — Kraken connected" : "Running (exchange not connected)";
            }

            return new ServiceHealth
            {
                Name = "MarketData",
                Status = status,
                Detail = detail,
                CheckedAt = DateTime.UtcNow,
                SignalRConnected = _marketData.IsConnected,
                Metrics = BuildMetrics(new Dictionary<string, object?>
                {
                    ["wsConnected"] = wsConnected,
                    ["alertCount"] = alertCount,
                    ["alerts"] = alerts.Count > 0 ? (object)alerts : null,
                }, metrics, "avgLatencyMs", "reconnectCount", "pricesStale"),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "MarketData health check failed");
            return Offline("MarketData", false);
        }
    }

    private async Task<ServiceHealth> CheckExecutionServiceAsync(CancellationToken ct)
    {
        try
        {
            var (statusCode, body) = await _execution.GetRawStatusAsync(ct);

            if (statusCode >= 500 || statusCode == 503)
                return Offline("ExecutionService", _execution.IsConnected);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var mode = root.TryGetProperty("mode", out var mv) ? mv.GetString() : "unknown";
            var safeMode = root.TryGetProperty("safe_mode", out var sm) && sm.GetBoolean();
            var strategyState = root.TryGetProperty("strategy_state", out var ss) ? ss.GetString() : "unknown";
            var openPositions = root.TryGetProperty("open_positions", out var op) ? op.GetInt32() : 0;
            var strategyId = root.TryGetProperty("strategy_id", out var si) && si.ValueKind != JsonValueKind.Null
                ? si.GetString() : null;

            string status;
            string detail;

            if (safeMode)
            {
                status = "degraded";
                detail = $"Safe mode active — {openPositions} open position{(openPositions == 1 ? "" : "s")}";
            }
            else if (strategyState is "waiting" or "no_strategy" or null)
            {
                status = "degraded";
                detail = "Waiting for strategy — no active strategy loaded";
            }
            else
            {
                status = "online";
                var posStr = openPositions == 1 ? "1 open position" : $"{openPositions} open positions";
                detail = $"{mode?.ToUpperInvariant() ?? "UNKNOWN"} — {strategyState} — {posStr}";
            }

            return new ServiceHealth
            {
                Name = "ExecutionService",
                Status = status,
                Detail = detail,
                CheckedAt = DateTime.UtcNow,
                SignalRConnected = _execution.IsConnected,
                Metrics = new Dictionary<string, object?>
                {
                    ["mode"] = mode,
                    ["safeMode"] = safeMode,
                    ["strategyState"] = strategyState,
                    ["strategyId"] = strategyId,
                    ["openPositions"] = openPositions,
                },
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ExecutionService health check failed");
            return Offline("ExecutionService", false);
        }
    }

    private async Task<ServiceHealth> CheckAgentRunnerAsync(CancellationToken ct)
    {
        try
        {
            var status = await _agentRunner.GetStatusAsync(ct);

            if (status is null)
                return Offline("AgentRunner", null);

            var root = status.Value;

            var currentState = root.TryGetProperty("currentState", out var cs) ? cs.GetString() : "Unknown";
            var isPaused = root.TryGetProperty("isPaused", out var ip) && ip.GetBoolean();
            var pauseReason = root.TryGetProperty("pauseReason", out var pr) && pr.ValueKind != JsonValueKind.Null
                ? pr.GetString() : null;
            var cycleId = root.TryGetProperty("currentCycleId", out var ci) && ci.ValueKind != JsonValueKind.Null
                ? ci.GetString() : null;

            DateTime? nextRun = null;
            if (root.TryGetProperty("nextScheduledTime", out var ns) && ns.ValueKind != JsonValueKind.Null)
            {
                try { nextRun = ns.GetDateTimeOffset().UtcDateTime; }
                catch { /* ignore */ }
            }

            string serviceStatus;
            string detail;

            if (isPaused)
            {
                serviceStatus = "degraded";
                detail = $"Paused — {pauseReason ?? "no reason given"}";
            }
            else if (currentState is "Failed")
            {
                serviceStatus = "degraded";
                detail = "Agent loop in failed state";
            }
            else if (currentState is "WaitingForNextCycle")
            {
                serviceStatus = "online";
                detail = nextRun.HasValue
                    ? $"Idle — next cycle at {nextRun.Value:HH:mm:ss} UTC"
                    : "Idle — waiting for next cycle";
            }
            else if (currentState is "Idle" or "Ready")
            {
                serviceStatus = "online";
                detail = "Ready";
            }
            else
            {
                serviceStatus = "online";
                detail = cycleId is not null
                    ? $"Running — {currentState} (cycle {cycleId})"
                    : $"Running — {currentState}";
            }

            return new ServiceHealth
            {
                Name = "AgentRunner",
                Status = serviceStatus,
                Detail = detail,
                CheckedAt = DateTime.UtcNow,
                SignalRConnected = null,  // AgentRunner has no SignalR hub
                Metrics = new Dictionary<string, object?>
                {
                    ["currentState"] = currentState,
                    ["isPaused"] = isPaused,
                    ["pauseReason"] = pauseReason,
                    ["currentCycleId"] = cycleId,
                    ["nextScheduledTime"] = nextRun?.ToString("o"),
                },
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AgentRunner health check failed");
            return Offline("AgentRunner", null);
        }
    }

    private static ServiceHealth Offline(string name, bool? signalRConnected) => new()
    {
        Name = name,
        Status = "offline",
        Detail = "Service unreachable",
        CheckedAt = DateTime.UtcNow,
        SignalRConnected = signalRConnected,
    };

    /// <summary>
    /// Merges <paramref name="base"/> with selected keys from <paramref name="source"/> if present.
    /// </summary>
    private static Dictionary<string, object?> BuildMetrics(
        Dictionary<string, object?> @base,
        Dictionary<string, object?> source,
        params string[] extraKeys)
    {
        foreach (var key in extraKeys)
        {
            if (source.TryGetValue(key, out var val))
                @base[key] = val;
        }
        return @base;
    }
}

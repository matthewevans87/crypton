using MonitoringDashboard.Models;
using System.Text.Json;

namespace MonitoringDashboard.Services;

public interface ISystemHealthChecker
{
    Task<SystemStatus> GetStatusAsync(CancellationToken ct, string correlationId = "internal");
}

/// <summary>
/// Encapsulates all per-service health check logic so it can be shared between
/// <see cref="MonitoringDashboard.Controllers.SystemController"/> (request-scoped) and
/// <see cref="SystemHealthBroadcaster"/> (background timer).
/// </summary>
public class SystemHealthChecker : ISystemHealthChecker
{
    private readonly IMarketDataServiceClient _marketData;
    private readonly IExecutionServiceClient _execution;
    private readonly IAgentRunnerClient _agentRunner;
    private readonly ILogger<SystemHealthChecker> _logger;

    public SystemHealthChecker(
        IMarketDataServiceClient marketData,
        IExecutionServiceClient execution,
        IAgentRunnerClient agentRunner,
        ILogger<SystemHealthChecker> logger)
    {
        _marketData = marketData;
        _execution = execution;
        _agentRunner = agentRunner;
        _logger = logger;
    }

    public async Task<SystemStatus> GetStatusAsync(CancellationToken ct, string correlationId = "internal")
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        var linkedCt = cts.Token;

        var mdTask = CheckMarketDataAsync(correlationId, linkedCt);
        var esTask = CheckExecutionServiceAsync(correlationId, linkedCt);
        var arTask = CheckAgentRunnerAsync(correlationId, linkedCt);

        await Task.WhenAll(mdTask, esTask, arTask);

        return new SystemStatus
        {
            Services = [await mdTask, await esTask, await arTask],
            CheckedAt = DateTime.UtcNow,
        };
    }

    // -----------------------------------------------------------------------
    // Per-service checks (identical logic to SystemController private methods)
    // -----------------------------------------------------------------------

    private async Task<ServiceHealth> CheckMarketDataAsync(string correlationId, CancellationToken ct)
    {
        try
        {
            var (statusCode, body) = await _marketData.GetRawMetricsAsync(ct);

            if (statusCode >= 500 || statusCode == 503)
                return Offline("MarketData", _marketData.IsConnected, correlationId);

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

            var wsConnected = metrics.TryGetValue("exchange.connected", out var wsc) && wsc?.ToString() == "True";
            var alertCount = alerts.Count;

            string detail;
            string status;
            var reasons = new List<ServiceHealthReason>();
            if (!isHealthy || alertCount > 0)
            {
                status = "degraded";
                var alertSummary = alertCount == 1 ? alerts[0] : $"{alertCount} active alerts";
                detail = wsConnected
                    ? $"Degraded — {alertSummary}"
                    : $"Exchange disconnected — {alertSummary}";

                if (!wsConnected)
                {
                    reasons.Add(new ServiceHealthReason
                    {
                        Code = "marketdata.websocket_disconnected",
                        Summary = "Market data WebSocket connection is disconnected.",
                        Severity = "critical",
                        Category = "connectivity",
                        RecommendedAction = "Verify exchange connectivity and credentials, then trigger a reconnect workflow.",
                        IsUserActionable = true,
                    });
                }

                foreach (var alert in alerts)
                    reasons.Add(MapMarketDataAlertToReason(alert));
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
                CorrelationId = correlationId,
                Reasons = reasons,
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
            return Offline("MarketData", false, correlationId);
        }
    }

    private async Task<ServiceHealth> CheckExecutionServiceAsync(string correlationId, CancellationToken ct)
    {
        try
        {
            var (statusCode, body) = await _execution.GetRawStatusAsync(ct);

            if (statusCode >= 500 || statusCode == 503)
                return Offline("ExecutionService", _execution.IsConnected, correlationId);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var mode = root.TryGetProperty("mode", out var mv) ? mv.GetString() : "unknown";
            var safeMode = root.TryGetProperty("safe_mode", out var sm) && sm.GetBoolean();
            var isDegraded = root.TryGetProperty("is_degraded", out var idg) && idg.GetBoolean();
            var safeModeTriggered = root.TryGetProperty("safe_mode_triggered", out var smt) && smt.GetBoolean();
            var safeModeReason = root.TryGetProperty("safe_mode_reason", out var smr) && smr.ValueKind != JsonValueKind.Null
                ? smr.GetString() : null;
            var degradedErrors = root.TryGetProperty("degraded_errors", out var des) && des.ValueKind == JsonValueKind.Array
                ? des.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
                : [];
            var entriesSuspended = root.TryGetProperty("entries_suspended", out var es) && es.GetBoolean();
            var strategyState = root.TryGetProperty("strategy_state", out var ss) ? ss.GetString() : "unknown";
            var openPositions = root.TryGetProperty("open_positions", out var op) ? op.GetInt32() : 0;
            var strategyId = root.TryGetProperty("strategy_id", out var si) && si.ValueKind != JsonValueKind.Null
                ? si.GetString() : null;

            string status;
            string detail;
            var reasons = new List<ServiceHealthReason>();

            if (safeMode)
            {
                status = "degraded";
                detail = $"Safe mode active — {openPositions} open position{(openPositions == 1 ? "" : "s")}";
                reasons.Add(new ServiceHealthReason
                {
                    Code = "execution.safe_mode_active",
                    Summary = safeModeReason is { Length: > 0 }
                        ? $"Execution service entered safe mode: {safeModeReason}."
                        : "Execution service entered safe mode and is suppressing live trading actions.",
                    Severity = "critical",
                    Category = "risk",
                    RecommendedAction = "Review risk thresholds and active exposure before clearing safe mode.",
                    IsUserActionable = true,
                });
            }
            else if (isDegraded)
            {
                status = "degraded";
                detail = degradedErrors.Count > 0
                    ? $"Degraded - {degradedErrors[0]}"
                    : "Degraded - control policy active";
                reasons.AddRange(degradedErrors.Select(error => new ServiceHealthReason
                {
                    Code = "execution.degraded",
                    Summary = error,
                    Severity = "warning",
                    Category = "operator",
                    RecommendedAction = "Review degraded reason and recover service via control plane when prerequisites are restored.",
                    IsUserActionable = true,
                }));
            }
            else if (strategyState is "waiting" or "no_strategy" or null)
            {
                status = "degraded";
                detail = "Waiting for strategy — no active strategy loaded";
                reasons.Add(new ServiceHealthReason
                {
                    Code = "execution.no_active_strategy",
                    Summary = "No strategy is currently active, so execution cannot progress normally.",
                    Severity = "warning",
                    Category = "config",
                    RecommendedAction = "Load or activate a valid strategy, then confirm strategy state transitions to running.",
                    IsUserActionable = true,
                });
            }
            else if (entriesSuspended)
            {
                status = "degraded";
                detail = "Entries suspended by risk controls";
                reasons.Add(new ServiceHealthReason
                {
                    Code = "execution.entries_suspended",
                    Summary = "New entries are suspended because portfolio risk constraints were breached.",
                    Severity = "warning",
                    Category = "risk",
                    RecommendedAction = "Check exposure and daily loss metrics; entries should resume once metrics return within limits.",
                    IsUserActionable = true,
                });
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
                CorrelationId = correlationId,
                Reasons = reasons,
                Metrics = new Dictionary<string, object?>
                {
                    ["mode"] = mode,
                    ["safeMode"] = safeMode,
                    ["isDegraded"] = isDegraded,
                    ["degradedErrors"] = degradedErrors,
                    ["safeModeTriggered"] = safeModeTriggered,
                    ["safeModeReason"] = safeModeReason,
                    ["entriesSuspended"] = entriesSuspended,
                    ["strategyState"] = strategyState,
                    ["strategyId"] = strategyId,
                    ["openPositions"] = openPositions,
                },
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "ExecutionService health check failed");
            return Offline("ExecutionService", false, correlationId);
        }
    }

    private async Task<ServiceHealth> CheckAgentRunnerAsync(string correlationId, CancellationToken ct)
    {
        try
        {
            var status = await _agentRunner.GetStatusAsync(ct);

            if (status is null)
                return Offline("AgentRunner", _agentRunner.IsConnected, correlationId);

            var root = status.Value;

            var currentState = root.TryGetProperty("currentState", out var cs) ? cs.GetString() : "Unknown";
            var isPaused = root.TryGetProperty("isPaused", out var ip) && ip.GetBoolean();
            var isDegraded = root.TryGetProperty("isDegraded", out var idg) && idg.GetBoolean();
            var degradedErrors = root.TryGetProperty("degradedErrors", out var de) && de.ValueKind == JsonValueKind.Array
                ? de.EnumerateArray().Select(e => e.GetString()).OfType<string>().ToList()
                : [];
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
            var reasons = new List<ServiceHealthReason>();

            if (isPaused)
            {
                serviceStatus = "degraded";
                detail = $"Paused — {pauseReason ?? "no reason given"}";
                reasons.Add(new ServiceHealthReason
                {
                    Code = "agentrunner.paused",
                    Summary = pauseReason is { Length: > 0 }
                        ? $"Agent loop is paused: {pauseReason}."
                        : "Agent loop is paused with no explicit reason.",
                    Severity = "warning",
                    Category = "operator",
                    RecommendedAction = "Confirm whether pause is intentional; resume loop when ready.",
                    IsUserActionable = true,
                });
            }
            else if (isDegraded)
            {
                serviceStatus = "degraded";
                detail = degradedErrors.Count > 0
                    ? $"Degraded - {degradedErrors[0]}"
                    : "Degraded - startup prerequisites failed";
                reasons.AddRange(degradedErrors.Select(error => new ServiceHealthReason
                {
                    Code = "agentrunner.degraded",
                    Summary = error,
                    Severity = "warning",
                    Category = "dependency",
                    RecommendedAction = "Restore dependencies and use start/recover control action.",
                    IsUserActionable = true,
                }));
            }
            else if (currentState is "Failed")
            {
                serviceStatus = "degraded";
                detail = "Agent loop in failed state";
                reasons.Add(new ServiceHealthReason
                {
                    Code = "agentrunner.failed_state",
                    Summary = "Agent loop entered a failed state and may require intervention.",
                    Severity = "critical",
                    Category = "bug",
                    RecommendedAction = "Inspect recent error logs and state transitions, then restart loop after root cause review.",
                    IsUserActionable = true,
                    BugSuspected = true,
                });
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
                SignalRConnected = _agentRunner.IsConnected,
                CorrelationId = correlationId,
                Reasons = reasons,
                Metrics = new Dictionary<string, object?>
                {
                    ["currentState"] = currentState,
                    ["isPaused"] = isPaused,
                    ["isDegraded"] = isDegraded,
                    ["degradedErrors"] = degradedErrors,
                    ["pauseReason"] = pauseReason,
                    ["currentCycleId"] = cycleId,
                    ["nextScheduledTime"] = nextRun?.ToString("o"),
                },
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "AgentRunner health check failed");
            return Offline("AgentRunner", null, correlationId);
        }
    }

    internal static ServiceHealth Offline(string name, bool? signalRConnected, string correlationId) => new()
    {
        Name = name,
        Status = "offline",
        Detail = "Service unreachable",
        CheckedAt = DateTime.UtcNow,
        SignalRConnected = signalRConnected,
        CorrelationId = correlationId,
        Reasons =
        [
            new ServiceHealthReason
            {
                Code = $"{name.ToLowerInvariant()}.unreachable",
                Summary = $"{name} did not respond to health checks before timeout.",
                Severity = "critical",
                Category = "connectivity",
                RecommendedAction = "Check service process/container status, network path, and recent startup errors.",
                IsUserActionable = true,
            },
        ],
    };

    internal static ServiceHealthReason MapMarketDataAlertToReason(string alert)
    {
        var lower = alert.ToLowerInvariant();

        if (lower.Contains("stale"))
            return new ServiceHealthReason { Code = "marketdata.price_stale", Summary = alert, Severity = "critical", Category = "data-quality", RecommendedAction = "Validate exchange feed freshness and WebSocket subscription health.", IsUserActionable = true };

        if (lower.Contains("circuit breaker"))
            return new ServiceHealthReason { Code = "marketdata.circuit_breaker_open", Summary = alert, Severity = "critical", Category = "dependency", RecommendedAction = "Review upstream exchange/API failures and wait for circuit reset before resuming normal operation.", IsUserActionable = true };

        if (lower.Contains("rate limit"))
            return new ServiceHealthReason { Code = "marketdata.rate_limit_pressure", Summary = alert, Severity = "warning", Category = "external-provider", RecommendedAction = "Reduce request pressure or widen polling intervals to stay within provider limits.", IsUserActionable = true };

        return new ServiceHealthReason { Code = "marketdata.alert", Summary = alert, Severity = "warning", Category = "unknown", RecommendedAction = "Inspect MarketData metrics and logs for additional context.", IsUserActionable = true };
    }

    internal static Dictionary<string, object?> BuildMetrics(
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

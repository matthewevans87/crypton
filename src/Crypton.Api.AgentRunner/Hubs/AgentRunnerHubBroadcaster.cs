using AgentRunner.Agents;
using AgentRunner.StateMachine;
using AgentRunner.Telemetry;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentRunner.Hubs;

/// <summary>
/// Background service that pushes real-time updates from <see cref="AgentRunnerService"/>
/// to connected SignalR clients:
/// <list type="bullet">
///   <item><description>StatusUpdate every 5 s (group "status")</description></item>
///   <item><description>MetricsUpdate every 30 s (group "metrics")</description></item>
///   <item><description>StepStarted / StepCompleted on each agent step (group "steps")</description></item>
///   <item><description>TokenReceived for each streaming LLM token (group "tokens")</description></item>
///   <item><description>ToolCallStarted / ToolCallCompleted for each tool invocation (group "toolcalls")</description></item>
///   <item><description>StateChanged / CycleCompleted / ErrorOccurred pushed immediately (group "status")</description></item>
/// </list>
/// </summary>
public sealed class AgentRunnerHubBroadcaster : IHostedService, IDisposable
{
    private readonly IHubContext<AgentRunnerHub> _hub;
    private readonly AgentRunnerService _agentRunnerService;
    private readonly MetricsCollector _metrics;
    private readonly ILogger<AgentRunnerHubBroadcaster> _logger;

    private CancellationTokenSource? _cts;
    private Task? _statusLoop;
    private Task? _metricsLoop;

    // Tool calls in-flight for the current step, keyed by tool name.
    private readonly Dictionary<string, (string Id, DateTime StartedAt)> _activeToolCalls = new();
    private readonly object _toolCallLock = new();

    public AgentRunnerHubBroadcaster(
        IHubContext<AgentRunnerHub> hub,
        AgentRunnerService agentRunnerService,
        MetricsCollector metrics,
        ILogger<AgentRunnerHubBroadcaster> logger)
    {
        _hub = hub;
        _agentRunnerService = agentRunnerService;
        _metrics = metrics;
        _logger = logger;
    }

    // ── IHostedService ──────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _statusLoop = RunStatusLoopAsync(_cts.Token);
        _metricsLoop = RunMetricsLoopAsync(_cts.Token);

        _agentRunnerService.StateChanged += OnStateChanged;
        _agentRunnerService.CycleCompleted += OnCycleCompleted;
        _agentRunnerService.ErrorOccurred += OnErrorOccurred;
        _agentRunnerService.StepStarted += OnStepStarted;
        _agentRunnerService.StepCompleted += OnStepCompleted;
        _agentRunnerService.TokenReceived += OnTokenReceived;
        _agentRunnerService.AgentEventReceived += OnAgentEventReceived;
        _agentRunnerService.HealthWarning += OnHealthWarning;
        _agentRunnerService.HealthCritical += OnHealthCritical;

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _agentRunnerService.StateChanged -= OnStateChanged;
        _agentRunnerService.CycleCompleted -= OnCycleCompleted;
        _agentRunnerService.ErrorOccurred -= OnErrorOccurred;
        _agentRunnerService.StepStarted -= OnStepStarted;
        _agentRunnerService.StepCompleted -= OnStepCompleted;
        _agentRunnerService.TokenReceived -= OnTokenReceived;
        _agentRunnerService.AgentEventReceived -= OnAgentEventReceived;
        _agentRunnerService.HealthWarning -= OnHealthWarning;
        _agentRunnerService.HealthCritical -= OnHealthCritical;

        _cts?.Cancel();

        if (_statusLoop is not null) await _statusLoop.ConfigureAwait(false);
        if (_metricsLoop is not null) await _metricsLoop.ConfigureAwait(false);
    }

    public void Dispose() => _cts?.Dispose();

    // ── Polling loops ────────────────────────────────────────────────────────

    private async Task RunStatusLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                await PushStatusSnapshotAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "AgentRunner status broadcast error"); }
        }
    }

    private async Task RunMetricsLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                var payload = new
                {
                    cycle_count = _metrics.GetCycleCount(),
                    step_success = _metrics.GetStepSuccess(),
                    step_failure = _metrics.GetStepFailure(),
                    tool_execution = _metrics.GetToolExecution(),
                    timestamp = DateTimeOffset.UtcNow
                };
                await _hub.Clients.Group(AgentRunnerHub.MetricsGroup)
                    .SendAsync("MetricsUpdate", payload, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "AgentRunner metrics broadcast error"); }
        }
    }

    private async Task PushStatusSnapshotAsync(CancellationToken ct = default)
    {
        var payload = new
        {
            current_state = _agentRunnerService.CurrentState.ToString(),
            is_paused = _agentRunnerService.CurrentState == LoopState.Paused,
            cycle_id = _agentRunnerService.CurrentCycle?.CycleId,
            restart_count = _agentRunnerService.RestartCount,
            next_scheduled_at = _agentRunnerService.NextScheduledRunTime,
            timestamp = DateTimeOffset.UtcNow
        };
        await _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("StatusUpdate", payload, ct);
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, LoopState state)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("StateChanged", new
            {
                state = state.ToString(),
                cycle_id = _agentRunnerService.CurrentCycle?.CycleId,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnCycleCompleted(object? sender, string cycleId)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("CycleCompleted", new
            {
                cycle_id = cycleId,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnErrorOccurred(object? sender, Exception ex)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("ErrorOccurred", new
            {
                message = ex.Message,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnStepStarted(object? sender, StepStartedEventArgs e)
    {
        // Reset active tool calls at the start of each step.
        lock (_toolCallLock) { _activeToolCalls.Clear(); }

        _ = _hub.Clients.Group(AgentRunnerHub.StepsGroup)
            .SendAsync("StepStarted", new
            {
                step_name = e.StepName,
                cycle_id = e.CycleId,
                started_at = e.StartedAt,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnStepCompleted(object? sender, StepCompletedEventArgs e)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.StepsGroup)
            .SendAsync("StepCompleted", new
            {
                step_name = e.StepName,
                cycle_id = e.CycleId,
                success = e.Success,
                error_message = e.ErrorMessage,
                duration_ms = (long)e.Duration.TotalMilliseconds,
                completed_at = e.CompletedAt,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnTokenReceived(object? sender, TokenEventArgs e)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.TokensGroup)
            .SendAsync("TokenReceived", new
            {
                token = e.Token,
                step_name = e.StepName,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnAgentEventReceived(object? sender, AgentEventArgs e)
    {
        var msg = e.EventMessage;

        if (msg.StartsWith("[tool] \u2192 "))
        {
            // "[tool] → toolName(params)"
            var afterArrow = msg["[tool] \u2192 ".Length..];
            var parenIdx = afterArrow.IndexOf('(');
            var toolName = parenIdx >= 0 ? afterArrow[..parenIdx].Trim() : afterArrow.Trim();
            var input = parenIdx >= 0 && afterArrow.EndsWith(')')
                ? afterArrow[(parenIdx + 1)..^1].Trim()
                : parenIdx >= 0 ? afterArrow[(parenIdx + 1)..].Trim() : string.Empty;

            var id = Guid.NewGuid().ToString("N");
            var calledAt = DateTime.UtcNow;

            lock (_toolCallLock) { _activeToolCalls[toolName] = (id, calledAt); }

            _ = _hub.Clients.Group(AgentRunnerHub.ToolCallsGroup)
                .SendAsync("ToolCallStarted", new
                {
                    id = id,
                    tool_name = toolName,
                    input = input,
                    step_name = e.StepName,
                    called_at = calledAt,
                    timestamp = DateTimeOffset.UtcNow
                });
        }
        else if (msg.StartsWith("[tool] \u2190 "))
        {
            // "[tool] ← toolName OK (1.2s): result"
            // "[tool] ← toolName FAILED (1.2s): error"
            var afterArrow = msg["[tool] \u2190 ".Length..];
            var spaceIdx = afterArrow.IndexOf(' ');
            var toolName = spaceIdx >= 0 ? afterArrow[..spaceIdx].Trim() : afterArrow.Trim();
            var success = afterArrow.Contains(" OK ");
            var durationMs = ParseDurationMs(afterArrow);
            var colonIdx = afterArrow.IndexOf(": ");
            var detail = colonIdx >= 0 ? afterArrow[(colonIdx + 2)..].Trim() : null;

            string callId;
            DateTime calledAt;
            lock (_toolCallLock)
            {
                if (_activeToolCalls.TryGetValue(toolName, out var active))
                {
                    callId = active.Id;
                    calledAt = active.StartedAt;
                    _activeToolCalls.Remove(toolName);
                }
                else
                {
                    callId = Guid.NewGuid().ToString("N");
                    calledAt = DateTime.UtcNow;
                }
            }

            _ = _hub.Clients.Group(AgentRunnerHub.ToolCallsGroup)
                .SendAsync("ToolCallCompleted", new
                {
                    id = callId,
                    tool_name = toolName,
                    success = success,
                    output = success ? detail : null,
                    error_message = success ? null : detail,
                    duration_ms = durationMs,
                    called_at = calledAt,
                    step_name = e.StepName,
                    timestamp = DateTimeOffset.UtcNow
                });
        }
        // [iter N/M] and [LLM] markers are not forwarded to clients.
    }

    private void OnHealthWarning(object? sender, LoopHealthEventArgs e)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("StateChanged", new
            {
                state = e.CurrentState.ToString(),
                cycle_id = _agentRunnerService.CurrentCycle?.CycleId,
                is_stalled = true,
                stall_message = e.Message,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    private void OnHealthCritical(object? sender, LoopHealthEventArgs e)
    {
        _ = _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("StateChanged", new
            {
                state = e.CurrentState.ToString(),
                cycle_id = _agentRunnerService.CurrentCycle?.CycleId,
                is_stalled = true,
                stall_message = e.Message,
                timestamp = DateTimeOffset.UtcNow
            });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Parses "(1.2s)" or "(450ms)" from a tool event message.</summary>
    private static long ParseDurationMs(string text)
    {
        var openIdx = text.IndexOf('(');
        var closeIdx = text.IndexOf(')');
        if (openIdx < 0 || closeIdx <= openIdx) return 0;

        var inner = text[(openIdx + 1)..closeIdx].Trim().ToLowerInvariant();
        if (inner.EndsWith("ms") && double.TryParse(inner[..^2], out var ms))
            return (long)ms;
        if (inner.EndsWith('s') && double.TryParse(inner[..^1], out var secs))
            return (long)(secs * 1000);
        return 0;
    }
}

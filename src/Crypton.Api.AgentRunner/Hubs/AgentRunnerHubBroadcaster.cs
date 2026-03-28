using AgentRunner.Abstractions;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AgentRunner.Hubs;

/// <summary>
/// <see cref="IAgentEventSink"/> implementation that fans out typed <see cref="AgentEvent"/>s
/// from the orchestration and execution layers to connected SignalR clients.
///
/// Event-to-hub mapping:
/// <list type="bullet">
///   <item><description><see cref="StepStartedEvent"/> → StepStarted (group "steps")</description></item>
///   <item><description><see cref="StepCompletedEvent"/> → StepCompleted (group "steps")</description></item>
///   <item><description><see cref="CycleCompletedEvent"/> → CycleCompleted (group "status")</description></item>
///   <item><description><see cref="LoopStateChangedEvent"/> → StateChanged (group "status")</description></item>
///   <item><description><see cref="LoopErrorEvent"/> → ErrorOccurred (group "status")</description></item>
///   <item><description><see cref="LoopHealthEvent"/> → HealthWarning / HealthCritical (group "status")</description></item>
///   <item><description><see cref="TokenReceivedEvent"/> → TokenReceived (group "tokens")</description></item>
///   <item><description><see cref="ToolCallStartedEvent"/> → ToolCallStarted (group "toolcalls")</description></item>
///   <item><description><see cref="ToolCallCompletedEvent"/> → ToolCallCompleted (group "toolcalls")</description></item>
/// </list>
/// Also runs a 5-second status polling loop to keep the dashboard up to date.
/// </summary>
public sealed class AgentRunnerHubBroadcaster : IAgentEventSink, IHostedService, IDisposable
{
    private readonly IHubContext<AgentRunnerHub> _hub;
    private readonly ICycleOrchestrator _orchestrator;
    private readonly ILogger<AgentRunnerHubBroadcaster> _logger;

    private CancellationTokenSource? _cts;
    private Task? _statusLoop;

    public AgentRunnerHubBroadcaster(
        IHubContext<AgentRunnerHub> hub,
        ICycleOrchestrator orchestrator,
        ILogger<AgentRunnerHubBroadcaster> logger)
    {
        _hub = hub;
        _orchestrator = orchestrator;
        _logger = logger;
    }

    // ── IHostedService ──────────────────────────────────────────────────────

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _statusLoop = RunStatusLoopAsync(_cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_statusLoop is not null)
            await _statusLoop.ConfigureAwait(false);
    }

    public void Dispose() => _cts?.Dispose();

    // ── IAgentEventSink ─────────────────────────────────────────────────────

    /// <summary>Routes each typed event to the appropriate SignalR group.</summary>
    public void Publish(AgentEvent evt)
    {
        _ = evt switch
        {
            StepStartedEvent e => BroadcastAsync(
                AgentRunnerHub.StepsGroup,
                "StepStarted",
                new { e.StepName, e.CycleId, StartedAt = e.StartedAt }),

            StepCompletedEvent e => BroadcastAsync(
                AgentRunnerHub.StepsGroup,
                "StepCompleted",
                new { e.StepName, e.CycleId, e.Success, e.ErrorMessage, DurationMs = (long)e.Duration.TotalMilliseconds }),

            CycleCompletedEvent e => BroadcastAsync(
                AgentRunnerHub.StatusGroup,
                "CycleCompleted",
                new { e.CycleId, CompletedAt = e.CompletedAt }),

            LoopStateChangedEvent e => BroadcastAsync(
                AgentRunnerHub.StatusGroup,
                "StateChanged",
                new { State = e.State.ToString(), e.CycleId }),

            LoopErrorEvent e => BroadcastAsync(
                AgentRunnerHub.StatusGroup,
                "ErrorOccurred",
                new { e.Message, e.CycleId }),

            LoopHealthEvent e when e.IsCritical => BroadcastAsync(
                AgentRunnerHub.StatusGroup,
                "HealthCritical",
                new { e.Message, State = e.State.ToString() }),

            LoopHealthEvent e => BroadcastAsync(
                AgentRunnerHub.StatusGroup,
                "HealthWarning",
                new { e.Message, State = e.State.ToString() }),

            TokenReceivedEvent e => BroadcastAsync(
                AgentRunnerHub.TokensGroup,
                "TokenReceived",
                new { e.Token, StepName = e.StepName }),

            ToolCallStartedEvent e => BroadcastAsync(
                AgentRunnerHub.ToolCallsGroup,
                "ToolCallStarted",
                new { Id = e.Id.ToString(), e.ToolName, e.InputJson, StepName = e.StepName, IsCompleted = false }),

            ToolCallCompletedEvent e => BroadcastAsync(
                AgentRunnerHub.ToolCallsGroup,
                "ToolCallCompleted",
                new { Id = e.Id.ToString(), e.ToolName, e.Output, DurationMs = (long)e.Duration.TotalMilliseconds, e.IsError, StepName = e.StepName }),

            _ => Task.CompletedTask
        };
    }

    // ── Polling loop ─────────────────────────────────────────────────────────

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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in status polling loop");
            }
        }
    }

    private Task PushStatusSnapshotAsync(CancellationToken ct)
    {
        var payload = new
        {
            State = _orchestrator.CurrentState.ToString(),
            CycleId = _orchestrator.CurrentCycle?.CycleId,
            NextScheduledAt = _orchestrator.NextScheduledAt,
            RestartCount = _orchestrator.RestartCount,
        };
        return _hub.Clients.Group(AgentRunnerHub.StatusGroup)
            .SendAsync("StatusUpdate", payload, ct);
    }

    private Task BroadcastAsync(string group, string method, object payload)
    {
        return _hub.Clients.Group(group)
            .SendAsync(method, payload, CancellationToken.None);
    }
}

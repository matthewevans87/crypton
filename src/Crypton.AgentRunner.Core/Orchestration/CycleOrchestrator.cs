using System.Text.RegularExpressions;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using AgentRunner.Infrastructure;

namespace AgentRunner.Orchestration;

/// <summary>
/// Drives the full learning loop: Plan → Research → Analyze → Synthesize → WaitingForNextCycle →
/// Evaluate → Plan → … Owns lifecycle (start/stop/pause/resume/abort), step execution,
/// artifact routing, and mailbox dispatch. Publishes all events via <see cref="IAgentEventSink"/>.
/// </summary>
public sealed class CycleOrchestrator : ICycleOrchestrator
{
    private static readonly IReadOnlyDictionary<LoopState, string> ArtifactNames =
        new Dictionary<LoopState, string>
        {
            [LoopState.Plan] = "plan.md",
            [LoopState.Research] = "research.md",
            [LoopState.Analyze] = "analysis.md",
            [LoopState.Synthesize] = "strategy.json",
            [LoopState.Evaluate] = "evaluation.md",
        };

    // Forward/backward mailbox routing table keyed by the producing step.
    private static readonly IReadOnlyDictionary<LoopState, (string? Forward, string? Backward)> MailboxRoutes =
        new Dictionary<LoopState, (string?, string?)>
        {
            [LoopState.Plan] = ("research", null),
            [LoopState.Research] = ("analysis", "plan"),
            [LoopState.Analyze] = ("synthesis", "research"),
            [LoopState.Synthesize] = ("evaluation", "analysis"),
            [LoopState.Evaluate] = (null, null),
        };

    // Next step in the pipeline for each agent-step state.
    private static readonly IReadOnlyDictionary<LoopState, LoopState> NextStep =
        new Dictionary<LoopState, LoopState>
        {
            [LoopState.Plan] = LoopState.Research,
            [LoopState.Research] = LoopState.Analyze,
            [LoopState.Analyze] = LoopState.Synthesize,
            [LoopState.Synthesize] = LoopState.WaitingForNextCycle,
            [LoopState.Evaluate] = LoopState.Plan,
        };

    private readonly ILoopStateMachine _sm;
    private readonly IAgentContextProvider _contextProvider;
    private readonly IAgentExecutor _executor;
    private readonly IArtifactStore _artifacts;
    private readonly IMailboxService _mailbox;
    private readonly CycleScheduler _scheduler;
    private readonly IStatePersistence _persistence;
    private readonly IAgentEventSink _sink;
    private readonly IEventLogger _logger;
    private readonly AgentRunnerConfig _config;
    private readonly LoopRestartManager _restartManager;

    private CycleContext? _currentCycle;
    private string? _latestPreviousCycleId;
    private LoopState _prePauseState = LoopState.Idle;
    private bool _isShuttingDown;

    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _restartCount;

    public LoopState CurrentState => _sm.CurrentState;
    public CycleContext? CurrentCycle => _currentCycle;
    public int RestartCount => _restartCount;
    public DateTimeOffset NextScheduledAt => _scheduler.NextScheduledAt;

    public CycleOrchestrator(
        ILoopStateMachine sm,
        IAgentContextProvider contextProvider,
        IAgentExecutor executor,
        IArtifactStore artifacts,
        IMailboxService mailbox,
        ICycleScheduler scheduler,
        IStatePersistence persistence,
        IAgentEventSink sink,
        IEventLogger logger,
        AgentRunnerConfig config,
        LoopRestartManager restartManager)
    {
        _sm = sm;
        _contextProvider = contextProvider;
        _executor = executor;
        _artifacts = artifacts;
        _mailbox = mailbox;
        _scheduler = (CycleScheduler)scheduler;
        _persistence = persistence;
        _sink = sink;
        _logger = logger;
        _config = config;
        _restartManager = restartManager;

        _sm.StateTransition += (_, args) =>
        {
            _logger.LogStateTransition(args.From, args.To);
            _sink.Publish(new LoopStateChangedEvent(args.To, _currentCycle?.CycleId));
        };
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_runTask is { IsCompleted: false })
        {
            _logger.LogInfo("Orchestrator is already running.");
            return;
        }

        _logger.LogInfo("Orchestrator starting...");

        var saved = await _persistence.LoadAsync(ct);
        if (saved is { } s && s.Cycle is not null && s.State != LoopState.Idle &&
            _sm.CanTransitionTo(s.State))
        {
            _restartCount = s.Cycle.RestartCount;
            _currentCycle = s.Cycle;
            _logger.LogInfo($"Resuming from saved state: {s.State}");
            _sm.TransitionTo(s.State);

            var prevId = _artifacts.GetLatestCompletedCycleId();
            if (prevId != null && prevId != s.Cycle.CycleId)
                _latestPreviousCycleId = prevId;
        }
        else
        {
            TransitionToStartState();
        }

        _isShuttingDown = false;
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runTask = RunLoopAsync(_runCts.Token);
        _logger.LogInfo("Orchestrator started.");
    }

    public async Task StopAsync()
    {
        _logger.LogInfo("Orchestrator stopping...");
        _isShuttingDown = true;
        _runCts?.Cancel();

        if (_runTask is not null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }

        await _persistence.SaveAsync(_sm.CurrentState, _currentCycle);
        _runTask = null;
        _logger.LogInfo("Orchestrator stopped.");
    }

    public async Task PauseAsync(string? reason = null)
    {
        if (!_sm.CanTransitionTo(LoopState.Paused))
            return;

        _prePauseState = _sm.CurrentState;
        _sm.TransitionTo(LoopState.Paused);

        if (_currentCycle is not null)
            _currentCycle = _currentCycle with
            {
                IsPaused = true,
                PausedAt = DateTimeOffset.UtcNow,
                PauseReason = reason
            };

        _logger.LogInfo($"Orchestrator paused: {reason}");
        await _persistence.SaveAsync(_sm.CurrentState, _currentCycle);
    }

    public Task ResumeAsync()
    {
        if (_sm.CurrentState != LoopState.Paused)
            return Task.CompletedTask;

        if (_currentCycle is not null)
            _currentCycle = _currentCycle with { IsPaused = false, PausedAt = null, PauseReason = null };

        _sm.TransitionTo(_prePauseState);
        _prePauseState = LoopState.Idle;
        _logger.LogInfo("Orchestrator resumed.");
        return Task.CompletedTask;
    }

    public async Task AbortAsync()
    {
        _logger.LogInfo("Orchestrator aborting...");
        _isShuttingDown = true;
        _runCts?.Cancel();

        if (_runTask is not null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }

        // Drive back to Idle through valid paths.
        var cur = _sm.CurrentState;
        if (cur != LoopState.Idle)
        {
            if (cur != LoopState.Failed && cur != LoopState.WaitingForNextCycle && cur != LoopState.Paused)
                _sm.TransitionTo(LoopState.Failed);
            _sm.TransitionTo(LoopState.Idle);
        }

        _currentCycle = null;
        _latestPreviousCycleId = null;
        _restartCount = 0;
        _isShuttingDown = false;
        _runTask = null;

        await _persistence.ClearAsync();
        _logger.LogInfo("Orchestrator aborted and reset to Idle.");
    }

    public void ForceNewCycle()
    {
        if (_sm.CurrentState == LoopState.WaitingForNextCycle)
            _scheduler.ForceNext();
    }

    public void InjectContext(string agentName, string content)
    {
        _mailbox.Send(new MailboxMessage("operator", agentName.ToLowerInvariant(), content, DateTimeOffset.UtcNow));
        _logger.LogInfo($"Injected context to {agentName}");
    }

    // ─── Main loop ────────────────────────────────────────────────────────────

    private async Task RunLoopAsync(CancellationToken ct)
    {
        _logger.LogInfo($"RunLoopAsync started, state={_sm.CurrentState}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                switch (_sm.CurrentState)
                {
                    case LoopState.Idle:
                        HandleIdle();
                        break;

                    case LoopState.Plan:
                    case LoopState.Research:
                    case LoopState.Analyze:
                    case LoopState.Synthesize:
                    case LoopState.Evaluate:
                        if (ExceedsMaxDuration())
                        {
                            _logger.LogWarning("Max cycle duration exceeded — forcing Evaluate.");
                            ForceTransitionToEvaluate();
                            break;
                        }
                        await ExecuteCurrentStepAsync(ct);
                        await _persistence.SaveAsync(_sm.CurrentState, _currentCycle, ct);
                        break;

                    case LoopState.WaitingForNextCycle:
                        await HandleWaitingAsync(ct);
                        break;

                    case LoopState.Paused:
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        break;

                    case LoopState.Failed:
                        await HandleFailedAsync(ct);
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown — no restart.
        }

        if (!_isShuttingDown)
            await HandleFailedAsync(ct);
    }

    private void HandleIdle()
    {
        var cycleId = _artifacts.CreateCycleDirectory();
        _currentCycle = new CycleContext
        {
            CycleId = cycleId,
            StartTime = DateTimeOffset.UtcNow,
            RestartCount = _restartCount
        };
        var firstStep = _latestPreviousCycleId is not null ? LoopState.Evaluate : LoopState.Plan;
        _sm.TransitionTo(firstStep);
    }

    private async Task ExecuteCurrentStepAsync(CancellationToken ct)
    {
        var state = _sm.CurrentState;
        var cycleId = _currentCycle!.CycleId;
        var startedAt = DateTimeOffset.UtcNow;

        _logger.LogAgentInvocation(state.ToString(), cycleId);
        _sink.Publish(new StepStartedEvent(state.ToString(), cycleId, startedAt));

        StepRecord stepRecord;
        try
        {
            var input = _contextProvider.BuildContext(state, cycleId, _latestPreviousCycleId);
            _logger.LogPromptSnapshot(state.ToString(), cycleId, input.SystemPrompt, input.UserPrompt);

            var output = await _executor.ExecuteAsync(input, _sink, ct);

            if (!output.Success)
            {
                stepRecord = new StepRecord(state, startedAt, DateTimeOffset.UtcNow, StepOutcome.Failed, output.Error);
                _currentCycle = RecordStep(_currentCycle, stepRecord);
                PublishStepCompleted(state, cycleId, false, output.Error, startedAt);
                _logger.LogAgentCompletion(state.ToString(), cycleId, false);
                await HandleStepFailureAsync(state, output.Error ?? "Unknown failure", ct);
                return;
            }

            // Validate artifact before saving
            var artifactName = ArtifactNames[state];
            var validation = ArtifactValidator.Validate(state, output.Output ?? "");
            if (!validation.IsValid)
            {
                var msg = $"Artifact validation failed for {artifactName}: " +
                          string.Join("; ", validation.Errors);
                _logger.LogWarning(msg);

                stepRecord = new StepRecord(state, startedAt, DateTimeOffset.UtcNow, StepOutcome.Failed, msg);
                _currentCycle = RecordStep(_currentCycle, stepRecord);
                PublishStepCompleted(state, cycleId, false, msg, startedAt);
                await HandleStepFailureAsync(state, msg, ct);
                return;
            }

            _artifacts.Save(cycleId, artifactName, output.Output!);
            RouteMailbox(state, output.Output!);

            stepRecord = new StepRecord(state, startedAt, DateTimeOffset.UtcNow, StepOutcome.Success);
            _currentCycle = RecordStep(_currentCycle, stepRecord);
            PublishStepCompleted(state, cycleId, true, null, startedAt);
            _logger.LogAgentCompletion(state.ToString(), cycleId, true);

            // Advance to the next step.
            _sm.TransitionTo(NextStep[state]);
        }
        catch (OperationCanceledException)
        {
            stepRecord = new StepRecord(state, startedAt, DateTimeOffset.UtcNow, StepOutcome.Timeout, "Timed out");
            _currentCycle = RecordStep(_currentCycle!, stepRecord);
            PublishStepCompleted(state, cycleId, false, "Timed out", startedAt);
            _sm.TransitionTo(LoopState.Failed);
            throw;
        }
    }

    private async Task HandleStepFailureAsync(LoopState state, string error, CancellationToken ct)
    {
        var maxRetries = _config.Resilience.MaxRestartAttempts;
        var retryCount = _currentCycle?.Steps
            .Count(s => s.Value.Step == state && s.Value.Outcome != StepOutcome.Success) ?? 0;

        if (retryCount < maxRetries)
        {
            var backoffSeconds = Math.Pow(2, retryCount) * 30;
            _logger.LogRetryAttempt(state.ToString(), state.ToString(), retryCount + 1, maxRetries);
            await Task.Delay(TimeSpan.FromSeconds(backoffSeconds), ct);
            // State is unchanged, loop will re-execute the same step.
        }
        else
        {
            _logger.LogError($"Step {state} exhausted retries. Transitioning to Failed.");
            _sink.Publish(new LoopErrorEvent($"Step {state} failed: {error}", _currentCycle?.CycleId));
            _sm.TransitionTo(LoopState.Failed);
        }
    }

    private async Task HandleWaitingAsync(CancellationToken ct)
    {
        if (_currentCycle is not null)
        {
            _currentCycle = _currentCycle with { EndTime = DateTimeOffset.UtcNow };
            _latestPreviousCycleId = _currentCycle.CycleId;
            _sink.Publish(new CycleCompletedEvent(_currentCycle.CycleId, DateTimeOffset.UtcNow));
            _logger.LogInfo($"Cycle {_currentCycle.CycleId} completed.");
        }

        var lastEnd = _currentCycle?.EndTime ?? DateTimeOffset.UtcNow;
        var waitTime = _scheduler.TimeUntilNext(lastEnd);

        if (waitTime > TimeSpan.Zero)
        {
            _logger.LogInfo($"Waiting {waitTime.TotalMinutes:F1} min until next cycle.");
            await Task.Delay(waitTime, ct);
        }

        _scheduler.ResetForce();
        ct.ThrowIfCancellationRequested();

        var newCycleId = _artifacts.CreateCycleDirectory();
        _currentCycle = new CycleContext
        {
            CycleId = newCycleId,
            StartTime = DateTimeOffset.UtcNow,
            RestartCount = _restartCount
        };

        var nextState = _latestPreviousCycleId is not null ? LoopState.Evaluate : LoopState.Plan;
        _sm.TransitionTo(nextState);
    }

    private async Task HandleFailedAsync(CancellationToken ct)
    {
        if (_isShuttingDown)
            return;

        _restartCount++;
        var shouldRestart = await _restartManager.ShouldRestartAsync(_restartCount, ct);
        if (!shouldRestart)
        {
            _logger.LogError("Max restart attempts exceeded. Loop permanently failed.");
            return;
        }

        // Reset back to Idle to try again.
        _sm.TransitionTo(LoopState.Idle);
        _runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _runTask = RunLoopAsync(_runCts.Token);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private void TransitionToStartState()
    {
        _latestPreviousCycleId = _artifacts.GetLatestCompletedCycleId();
        _sm.TransitionTo(LoopState.Idle);
    }

    private bool ExceedsMaxDuration()
    {
        if (_currentCycle is null)
            return false;
        var elapsed = DateTimeOffset.UtcNow - _currentCycle.StartTime;
        return elapsed.TotalMinutes > _config.Cycle.MaxDurationMinutes;
    }

    private void ForceTransitionToEvaluate()
    {
        var cur = _sm.CurrentState;
        if (cur == LoopState.Evaluate)
            return;

        if (!_sm.CanTransitionTo(LoopState.Failed))
        {
            if (!_sm.CanTransitionTo(LoopState.Idle))
                _sm.TransitionTo(LoopState.Plan);
        }
        else
        {
            _sm.TransitionTo(LoopState.Failed);
        }

        if (_sm.CurrentState != LoopState.Idle)
            _sm.TransitionTo(LoopState.Idle);

        _sm.TransitionTo(LoopState.Evaluate);
    }

    private static CycleContext RecordStep(CycleContext cycle, StepRecord step)
    {
        var steps = new Dictionary<LoopState, StepRecord>(cycle.Steps) { [step.Step] = step };
        return cycle with { Steps = steps };
    }

    private void PublishStepCompleted(LoopState state, string cycleId, bool success, string? error, DateTimeOffset start)
    {
        _sink.Publish(new StepCompletedEvent(
            state.ToString(), cycleId, success, error, DateTimeOffset.UtcNow - start));
    }

    private void RouteMailbox(LoopState state, string output)
    {
        if (!MailboxRoutes.TryGetValue(state, out var routes))
            return;

        var (forward, backward) = routes;

        if (forward is not null)
        {
            var content = ExtractTag(output, $"mailbox_to_{forward}", "No forward message");
            _mailbox.Send(new MailboxMessage(state.ToString(), forward, content, DateTimeOffset.UtcNow));
            _logger.LogMailboxDelivery(state.ToString(), forward, content);
        }

        if (backward is not null)
        {
            var content = ExtractTag(output, "feedback", "No feedback");
            _mailbox.Send(new MailboxMessage(state.ToString(), backward, content, DateTimeOffset.UtcNow));
            _logger.LogMailboxDelivery(state.ToString(), backward, content);
        }

        if (state == LoopState.Evaluate)
        {
            var broadcast = ExtractTag(output, "broadcast", "Broadcast message");
            foreach (var agent in new[] { "plan", "research", "analysis", "synthesis" })
            {
                _mailbox.Send(new MailboxMessage("evaluation", agent, broadcast, DateTimeOffset.UtcNow));
                _logger.LogMailboxDelivery("evaluation", agent, broadcast);
            }
        }
    }

    private static string ExtractTag(string output, string tag, string fallback)
    {
        var m = Regex.Match(output, $@"<{tag}>(.*?)</{tag}>", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : fallback;
    }
}

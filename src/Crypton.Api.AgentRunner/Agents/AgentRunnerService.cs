using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.Telemetry;
using AgentRunner.StateMachine;

namespace AgentRunner.Agents;

/// <summary>
/// Lifecycle facade for the learning loop. Owns startup, shutdown, pause/resume,
/// and the main dispatch loop. Delegates step execution to <see cref="CycleStepExecutor"/>,
/// inter-cycle waiting to <see cref="CycleScheduler"/>, and restart backoff to
/// <see cref="LoopRestartManager"/>.
/// </summary>
public class AgentRunnerService : IAgentRunnerLifecycle
{
    private readonly LoopStateMachine _stateMachine;
    private readonly StatePersistence _persistence;
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxManager _mailboxManager;
    private readonly AgentRunnerConfig _config;
    private readonly IEventLogger _logger;
    private readonly MetricsCollector _metrics;
    private readonly CycleStepExecutor _stepExecutor;
    private readonly CycleScheduler _scheduler;
    private readonly LoopRestartManager _restartManager;
    private LoopHealthMonitor? _healthMonitor;

    private CycleContext? _currentCycle;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _restartCount;
    private bool _isRestarting;
    private string? _latestPreviousCycleId;
    private LoopState _prePauseState = LoopState.Idle;

    public bool IsRunning => _runTask != null && !_runTask.IsCompleted;

    public event EventHandler<LoopState>? StateChanged;
    public event EventHandler<string>? CycleCompleted;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<LoopHealthEventArgs>? HealthWarning;
    public event EventHandler<LoopHealthEventArgs>? HealthCritical;
    public event EventHandler<StepStartedEventArgs>? StepStarted;
    public event EventHandler<StepCompletedEventArgs>? StepCompleted;
    public event EventHandler<TokenEventArgs>? TokenReceived;
    public event EventHandler<AgentEventArgs>? AgentEventReceived;

    public LoopState CurrentState => _stateMachine.CurrentState;
    public CycleContext? CurrentCycle => _currentCycle;
    public LoopHealthMonitor? HealthMonitor => _healthMonitor;
    public int RestartCount => _restartCount;
    public DateTime NextScheduledRunTime => _scheduler.NextScheduledRunTime;

    public AgentRunnerService(
        AgentRunnerConfig config,
        LoopStateMachine stateMachine,
        StatePersistence persistence,
        ArtifactManager artifactManager,
        MailboxManager mailboxManager,
        IEventLogger logger,
        MetricsCollector metrics,
        CycleStepExecutor stepExecutor,
        CycleScheduler scheduler,
        LoopRestartManager restartManager)
    {
        _config = config;
        _stateMachine = stateMachine;
        _persistence = persistence;
        _artifactManager = artifactManager;
        _mailboxManager = mailboxManager;
        _logger = logger;
        _metrics = metrics;
        _stepExecutor = stepExecutor;
        _scheduler = scheduler;
        _restartManager = restartManager;

        _stateMachine.StateTransition += OnStateTransition;
    }

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            _logger.LogInfo("Agent Runner is already running. Ignoring duplicate start request.");
            return;
        }

        _logger.LogInfo("Agent Runner starting...");

        _healthMonitor = new LoopHealthMonitor(_stateMachine, _config, _logger);
        _healthMonitor.HealthWarning += (_, e) => HealthWarning?.Invoke(this, e);
        _healthMonitor.HealthCritical += (_, e) => HealthCritical?.Invoke(this, e);

        var loadedState = await _persistence.LoadStateAsync();

        if (loadedState.HasValue)
        {
            var (savedState, savedContext) = (loadedState.Value.Item1, loadedState.Value.Item2);

            if (savedContext != null)
            {
                _restartCount = savedContext.RestartCount;
                _currentCycle = savedContext;
                // Reset transient-run counters so a resumed cycle starts fresh.
                // CycleStartTime: the watchdog measures from this startup, not the original start.
                // RetryCount: retries from a previous run are irrelevant; the new run gets the full budget.
                _currentCycle.CycleStartTime = DateTime.UtcNow;
                _currentCycle.RetryCount = 0;
            }

            // Only resume if the saved state is reachable from Idle. States like Failed or
            // WaitingForNextCycle are not directly resumable — treat them as a fresh start.
            if (savedState != LoopState.Idle && savedContext != null &&
                _stateMachine.CanTransitionTo(savedState))
            {
                _logger.LogInfo($"Resuming from state: {savedState}");
                _stateMachine.TransitionTo(savedState);

                // Restore the previous completed cycle reference so the Evaluate agent has context.
                var prevId = _artifactManager.GetLatestCompletedCycleId();
                if (prevId != null && prevId != savedContext.CycleId)
                    _latestPreviousCycleId = prevId;
            }
            else
            {
                _currentCycle = null; // Discard non-resumable context; HandleIdle will create a fresh cycle.
                TransitionToStartState();
            }
        }
        else
        {
            TransitionToStartState();
        }

        _runCts = new CancellationTokenSource();
        _runTask = RunLoopAsync(_runCts.Token);

        _logger.LogInfo("Agent Runner started");
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            _logger.LogInfo("Agent Runner is not running. Ignoring stop request.");
            return;
        }

        _logger.LogInfo("Agent Runner stopping...");

        // Set _isRestarting before cancelling to prevent HandleLoopExitAsync from
        // spawning a new loop task after the token is cancelled.
        _isRestarting = true;
        _runCts?.Cancel();

        if (_runTask != null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }

        _isRestarting = false;
        await _persistence.SaveStateAsync(_stateMachine.CurrentState, _currentCycle);

        _runTask = null;
        _runCts = null;

        _logger.LogInfo("Agent Runner stopped");
    }

    public void Pause(string? reason = null)
    {
        if (!_stateMachine.CanTransitionTo(LoopState.Paused))
            return;

        _prePauseState = _stateMachine.CurrentState;
        _stateMachine.TransitionTo(LoopState.Paused);

        if (_currentCycle != null)
        {
            _currentCycle.IsPaused = true;
            _currentCycle.PausedAt = DateTime.UtcNow;
            _currentCycle.PauseReason = reason;
        }

        _logger.LogInfo($"Agent Runner paused: {reason}");
    }

    public void Resume()
    {
        if (_stateMachine.CurrentState != LoopState.Paused)
            return;

        if (_currentCycle != null)
        {
            _currentCycle.IsPaused = false;
            _currentCycle.PausedAt = null;
            _currentCycle.PauseReason = null;
        }

        // Return to the state that was active when Pause() was called.
        _stateMachine.TransitionTo(_prePauseState);
        _prePauseState = LoopState.Idle;
        _logger.LogInfo("Agent Runner resumed");
    }

    public async Task AbortAsync()
    {
        _logger.LogInfo("Agent Runner aborting...");

        // Prevent HandleLoopExitAsync from spawning a new loop task after cancellation.
        _isRestarting = true;
        _runCts?.Cancel();

        if (_runTask != null)
        {
            try { await _runTask; }
            catch (OperationCanceledException) { }
        }

        // Drive the state machine back to Idle regardless of current state.
        // Active step states must go through Failed first.
        var current = _stateMachine.CurrentState;
        if (current != LoopState.Idle)
        {
            if (current != LoopState.Failed &&
                current != LoopState.WaitingForNextCycle &&
                current != LoopState.Paused)
            {
                _stateMachine.TransitionTo(LoopState.Failed);
            }
            _stateMachine.TransitionTo(LoopState.Idle);
        }

        // Clear all in-memory cycle state so the next StartAsync() begins fresh.
        _currentCycle = null;
        _latestPreviousCycleId = null;
        _restartCount = 0;
        _isRestarting = false;
        _prePauseState = LoopState.Idle;
        _runTask = null;
        _runCts = null;

        await _persistence.ClearStateAsync();

        _logger.LogInfo("Agent Runner aborted and reset to Idle");
    }

    public void ForceNewCycle()
    {
        if (_stateMachine.CurrentState == LoopState.WaitingForNextCycle)
            _scheduler.ForceNextCycle();
    }

    public void InjectContext(string agentName, string content)
    {
        _mailboxManager.Deposit(agentName, new MailboxMessage
        {
            FromAgent = "operator",
            ToAgent = agentName,
            Content = content,
            Type = MessageType.Forward
        });
        _logger.LogInfo($"Injected context to {agentName}");
    }

    // -------------------------------------------------------------------------
    // Main loop
    // -------------------------------------------------------------------------

    private async Task RunLoopAsync(CancellationToken ct)
    {
        _logger.LogInfo($"RunLoopAsync started, current state: {_stateMachine.CurrentState}");

        try
        {
            while (!ct.IsCancellationRequested)
            {
                switch (_stateMachine.CurrentState)
                {
                    case LoopState.Idle:
                        HandleIdle();
                        break;

                    // Agent step states: execute the current step, then advance on success.
                    case LoopState.Plan:
                    case LoopState.Research:
                    case LoopState.Analyze:
                    case LoopState.Synthesize:
                    case LoopState.Evaluate:
                        CheckHealthAndRecoverIfCritical();
                        if (ExceedsMaxCycleDuration(_currentCycle))
                        {
                            ForceTransitionToEvaluate();
                            break;
                        }
                        await ExecuteCurrentStepAsync(ct);
                        await _persistence.SaveStateAsync(_stateMachine.CurrentState, _currentCycle);
                        break;

                    case LoopState.WaitingForNextCycle:
                        await HandleWaitingForNextCycleAsync(ct);
                        break;

                    case LoopState.Paused:
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        break;

                    case LoopState.Failed:
                        await HandleLoopExitAsync(ct);
                        return;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Graceful shutdown via cancellation — do not restart.
            return;
        }

        await HandleLoopExitAsync(ct);
    }

    /// <summary>
    /// Called when the state machine is at Idle (fresh start or post-restart). Creates a new cycle
    /// directory and transitions to the first step: Evaluate if a prior completed cycle exists,
    /// Plan otherwise.
    /// </summary>
    private void HandleIdle()
    {
        var newCycleId = _artifactManager.CreateCycleDirectory();
        _currentCycle = new CycleContext
        {
            CycleId = newCycleId,
            CycleStartTime = DateTime.UtcNow,
            CurrentState = LoopState.Idle,
            RestartCount = _restartCount
        };

        var firstStep = _latestPreviousCycleId != null ? LoopState.Evaluate : LoopState.Plan;
        _stateMachine.TransitionTo(firstStep);
    }

    /// <summary>
    /// Executes the agent step for the current state. On success, advances the state machine
    /// to the next step via <see cref="LoopStateMachine.GetNextRequiredState"/>. On failure,
    /// either retries or transitions to Failed.
    /// </summary>
    private async Task ExecuteCurrentStepAsync(CancellationToken ct)
    {
        var state = _stateMachine.CurrentState;

        StepStarted?.Invoke(this, new StepStartedEventArgs
        {
            StepName = state.ToString(),
            CycleId = _currentCycle?.CycleId,
            StartedAt = DateTime.UtcNow
        });

        var result = await _stepExecutor.ExecuteAsync(
            state,
            _currentCycle!,
            _latestPreviousCycleId,
            token => TokenReceived?.Invoke(this, new TokenEventArgs
            {
                Token = token,
                StepName = state.ToString()
            }),
            evt => AgentEventReceived?.Invoke(this, new AgentEventArgs
            {
                EventMessage = evt,
                StepName = state.ToString()
            }),
            ct);

        if (_currentCycle != null)
        {
            _currentCycle.Steps[state.ToString()] = new StepRecord
            {
                Step = state,
                StartTime = result.StartTime,
                EndTime = result.EndTime,
                Outcome = result.Outcome,
                ErrorMessage = result.ErrorMessage
            };
            _currentCycle.LastStepOutcome = result.Outcome;
        }

        StepCompleted?.Invoke(this, new StepCompletedEventArgs
        {
            StepName = state.ToString(),
            CycleId = _currentCycle?.CycleId,
            Success = result.Outcome == StepOutcome.Success,
            ErrorMessage = result.ErrorMessage,
            Duration = result.EndTime - result.StartTime,
            CompletedAt = result.EndTime
        });

        if (result.Outcome is StepOutcome.Failed or StepOutcome.Timeout)
        {
            var maxRetries = GetMaxRetriesForState(state);

            if (_currentCycle != null && _currentCycle.RetryCount < maxRetries)
            {
                _currentCycle.RetryCount++;
                _logger.LogWarning(
                    $"Step {state} failed, retrying ({_currentCycle.RetryCount}/{maxRetries})");

                var backoffMinutes = Math.Pow(2, _currentCycle.RetryCount) * 5;
                await Task.Delay(TimeSpan.FromMinutes(backoffMinutes), ct);
                return; // State unchanged: same step executes again on the next loop iteration.
            }

            _logger.LogError($"Step {state} exhausted retries");
            _stateMachine.TransitionTo(LoopState.Failed);
            ErrorOccurred?.Invoke(this, new Exception($"Step {state} failed: {result.Outcome}"));
            return;
        }

        // Success: advance to the next step in the pipeline.
        // GetNextRequiredState returns: Plan→Research, Research→Analyze, Analyze→Synthesize,
        // Synthesize→WaitingForNextCycle, Evaluate→Plan.
        var nextState = _stateMachine.GetNextRequiredState();
        _stateMachine.TransitionTo(nextState);
    }

    private async Task HandleWaitingForNextCycleAsync(CancellationToken ct)
    {
        var (nextState, newCycleId) = await _scheduler.WaitAsync(
            _currentCycle,
            _latestPreviousCycleId,
            cycleId => CycleCompleted?.Invoke(this, cycleId),
            ct);

        if (ct.IsCancellationRequested)
            return;

        if (_currentCycle != null)
            _latestPreviousCycleId = _currentCycle.CycleId;

        _currentCycle = new CycleContext
        {
            CycleId = newCycleId,
            CycleStartTime = DateTime.UtcNow,
            CurrentState = nextState,
            RestartCount = _restartCount
        };

        // Use the scheduler's decision: Evaluate if there is a completed prior cycle, Plan otherwise.
        // The loop will then directly EXECUTE this state (not advance past it), which is the
        // correct behaviour — Evaluate runs before Plan on cycles 2+.
        _stateMachine.TransitionTo(nextState);
    }

    /// <summary>
    /// Unconditionally navigates to the Evaluate state via valid state machine paths.
    /// Used by both the cycle-duration timeout and the health-stall recovery.
    /// After this call, the loop will execute Evaluate on its next agent-step iteration.
    /// </summary>
    private void ForceTransitionToEvaluate()
    {
        _logger.LogWarning(
            $"Forcing transition to Evaluate (max duration: {_config.Cycle.MaxDurationMinutes} min).");

        var cur = _stateMachine.CurrentState;
        if (cur == LoopState.Evaluate) return;

        // All agent-step states support → Failed; WaitingForNextCycle and Paused do not.
        if (!_stateMachine.CanTransitionTo(LoopState.Failed))
        {
            if (!_stateMachine.CanTransitionTo(LoopState.Idle))
                _stateMachine.TransitionTo(LoopState.Plan); // Paused → Plan → ... not ideal but safe
        }
        else
        {
            _stateMachine.TransitionTo(LoopState.Failed);
        }

        if (_stateMachine.CurrentState != LoopState.Idle)
            _stateMachine.TransitionTo(LoopState.Idle);

        _stateMachine.TransitionTo(LoopState.Evaluate);
    }

    private void CheckHealthAndRecoverIfCritical()
    {
        var health = _healthMonitor?.CheckHealth();
        if (health?.IsCritical != true || _isRestarting) return;

        _logger.LogError("Critical health stall detected, triggering recovery to Evaluate.");
        ForceTransitionToEvaluate();
    }

    private async Task HandleLoopExitAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || _isRestarting)
            return;

        _isRestarting = true;
        _restartCount++;

        if (_currentCycle != null)
            _currentCycle.RestartCount = _restartCount;

        var shouldRestart = await _restartManager.ShouldRestartAsync(_restartCount, ct);

        if (!shouldRestart)
        {
            if (_stateMachine.CurrentState != LoopState.Failed)
                _stateMachine.TransitionTo(LoopState.Failed);
            ErrorOccurred?.Invoke(this,
                new Exception($"Loop exited after {_restartCount} restart attempts"));
            _isRestarting = false;
            return;
        }

        if (!ct.IsCancellationRequested)
        {
            _healthMonitor?.SetRestartCount(_restartCount);

            // Clear the stale cycle context; HandleIdle will create a fresh one.
            _currentCycle = null;

            // Drive to Idle so the loop restarts cleanly via HandleIdle.
            if (_stateMachine.CurrentState != LoopState.Idle)
            {
                if (_stateMachine.CurrentState != LoopState.Failed)
                    _stateMachine.TransitionTo(LoopState.Failed);
                _stateMachine.TransitionTo(LoopState.Idle);
            }

            _runCts = new CancellationTokenSource();
            _runTask = RunLoopAsync(_runCts.Token);
        }

        _isRestarting = false;
    }

    private static bool ExceedsMaxCycleDuration(CycleContext? cycle, double maxDurationMinutes)
    {
        if (cycle == null || cycle.CycleStartTime == default)
            return false;
        return DateTime.UtcNow - cycle.CycleStartTime >= TimeSpan.FromMinutes(maxDurationMinutes);
    }

    private bool ExceedsMaxCycleDuration(CycleContext? cycle) =>
        ExceedsMaxCycleDuration(cycle, _config.Cycle.MaxDurationMinutes);

    private int GetMaxRetriesForState(LoopState state) => state switch
    {
        LoopState.Plan => _config.Agents.Plan.MaxRetries,
        LoopState.Research => _config.Agents.Research.MaxRetries,
        LoopState.Analyze => _config.Agents.Analyze.MaxRetries,
        LoopState.Synthesize => _config.Agents.Synthesis.MaxRetries,
        LoopState.Evaluate => _config.Agents.Evaluation.MaxRetries,
        _ => 3
    };

    /// <summary>
    /// Loads prior-cycle history from disk into <see cref="_latestPreviousCycleId"/>.
    /// Called on a fresh start (no resumable persisted state). Does NOT create a cycle directory;
    /// that is deferred to <see cref="HandleIdle"/> so restarts also go through the same path.
    /// </summary>
    private void TransitionToStartState()
    {
        var previousCycleId = _artifactManager.GetLatestCompletedCycleId();
        if (previousCycleId != null)
        {
            _latestPreviousCycleId = previousCycleId;
            _logger.LogInfo($"Prior completed cycle found — will Evaluate before Plan: {previousCycleId}");
        }
        // State stays at Idle. RunLoopAsync's HandleIdle call creates the cycle directory
        // and transitions to Evaluate (if history exists) or Plan (if not).
    }

    private void OnStateTransition(object? sender, StateTransitionEventArgs e)
    {
        _logger.LogInfo($"State transition: {e.FromState} -> {e.ToState}");
        StateChanged?.Invoke(this, e.ToState);
    }
}

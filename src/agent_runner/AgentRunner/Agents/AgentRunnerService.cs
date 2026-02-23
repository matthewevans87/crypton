using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.Mailbox;
using AgentRunner.Telemetry;
using AgentRunner.StateMachine;
using AgentRunner.Tools;

namespace AgentRunner.Agents;

public class AgentRunnerService
{
    private readonly LoopStateMachine _stateMachine;
    private readonly StatePersistence _persistence;
    private readonly ArtifactManager _artifactManager;
    private readonly MailboxManager _mailboxManager;
    private readonly AgentContextBuilder _contextBuilder;
    private readonly AgentInvoker _agentInvoker;
    private readonly AgentRunnerConfig _config;
    private readonly IEventLogger _logger;
    private readonly MetricsCollector _metrics;
    private LoopHealthMonitor? _healthMonitor;

    private CycleContext? _currentCycle;
    private CancellationTokenSource? _runCts;
    private Task? _runTask;
    private int _restartCount;
    private bool _isRestarting;

    public event EventHandler<LoopState>? StateChanged;
    public event EventHandler<string>? CycleCompleted;
    public event EventHandler<Exception>? ErrorOccurred;
    public event EventHandler<LoopHealthEventArgs>? HealthWarning;
    public event EventHandler<LoopHealthEventArgs>? HealthCritical;

    public LoopState CurrentState => _stateMachine.CurrentState;
    public CycleContext? CurrentCycle => _currentCycle;
    public LoopHealthMonitor? HealthMonitor => _healthMonitor;
    public int RestartCount => _restartCount;

    public AgentRunnerService(
        AgentRunnerConfig config,
        LoopStateMachine stateMachine,
        StatePersistence persistence,
        ArtifactManager artifactManager,
        MailboxManager mailboxManager,
        AgentContextBuilder contextBuilder,
        AgentInvoker agentInvoker,
        IEventLogger logger,
        MetricsCollector metrics)
    {
        _config = config;
        _stateMachine = stateMachine;
        _persistence = persistence;
        _artifactManager = artifactManager;
        _mailboxManager = mailboxManager;
        _contextBuilder = contextBuilder;
        _agentInvoker = agentInvoker;
        _logger = logger;
        _metrics = metrics;

        _stateMachine.StateTransition += OnStateTransition;
    }

    public async Task StartAsync()
    {
        _logger.LogInfo("Agent Runner starting...");
        
        _healthMonitor = new LoopHealthMonitor(_stateMachine, _config, _logger);
        _healthMonitor.HealthWarning += (s, e) => HealthWarning?.Invoke(this, e);
        _healthMonitor.HealthCritical += (s, e) => HealthCritical?.Invoke(this, e);

        var loadedState = await _persistence.LoadStateAsync();
        
        if (loadedState.HasValue)
        {
            var savedState = loadedState.Value.Item1;
            var savedContext = loadedState.Value.Item2;
            
            if (savedContext != null)
            {
                _restartCount = savedContext.RestartCount;
                _currentCycle = savedContext;
            }
            
            if (savedState != LoopState.Idle && savedContext != null)
            {
                _logger.LogInfo($"Resuming from state: {savedState}");
                _stateMachine.TransitionTo(savedState);
            }
            else
            {
                _stateMachine.TransitionTo(LoopState.Plan);
            }
        }
        else
        {
            _stateMachine.TransitionTo(LoopState.Plan);
        }

        _runCts = new CancellationTokenSource();
        _runTask = RunLoopAsync(_runCts.Token);
        
        _logger.LogInfo("Agent Runner started");
    }

    public async Task StopAsync()
    {
        _logger.LogInfo("Agent Runner stopping...");
        
        _runCts?.Cancel();
        
        if (_runTask != null)
        {
            try
            {
                await _runTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _persistence.SaveStateAsync(_stateMachine.CurrentState, _currentCycle);
        
        _logger.LogInfo("Agent Runner stopped");
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // Check for stalled loop
            var health = _healthMonitor?.CheckHealth();
            
            if (health?.IsCritical == true && !_isRestarting)
            {
                _logger.LogError("Critical stall detected, triggering recovery...");
                _stateMachine.TransitionTo(LoopState.Plan);
            }

            if (_stateMachine.CurrentState == LoopState.WaitingForNextCycle)
            {
                await HandleNextCycleDelayAsync(cancellationToken);
                continue;
            }

            // Check for forced cycle timeout (FR-007)
            if (_currentCycle != null && _currentCycle.CycleStartTime != default)
            {
                var elapsed = DateTime.UtcNow - _currentCycle.CycleStartTime;
                var maxDuration = TimeSpan.FromMinutes(_config.Cycle.MaxDurationMinutes);
                
                if (elapsed >= maxDuration)
                {
                    _logger.LogWarning($"Forced cycle timeout exceeded ({maxDuration.TotalMinutes} min). Transitioning to Evaluation.");
                    
                    // Skip to evaluation if not already there
                    if (_stateMachine.CurrentState != LoopState.Evaluate)
                    {
                        _stateMachine.TransitionTo(LoopState.Evaluate);
                        await ExecuteStepAsync(cancellationToken);
                        await _persistence.SaveStateAsync(_stateMachine.CurrentState, _currentCycle);
                        continue;
                    }
                }
            }

            var nextState = _stateMachine.GetNextRequiredState();
            
            if (!_stateMachine.CanTransitionTo(nextState))
            {
                if (_stateMachine.IsTerminalState())
                {
                    _logger.LogInfo("Agent Runner in terminal state, waiting...");
                    await Task.Delay(TimeSpan.FromMinutes(1), cancellationToken);
                    continue;
                }
                break;
            }

            var success = _stateMachine.TransitionTo(nextState);
            if (!success)
            {
                _logger.LogError($"Failed to transition to {nextState}");
                break;
            }

            // Check for parallel execution (FR-169-170)
            if (_config.Cycle.EnableParallelExecution && nextState == LoopState.Research)
            {
                await ExecuteParallelResearchAndAnalysisAsync(cancellationToken);
            }
            else
            {
                await ExecuteStepAsync(cancellationToken);
            }
            
            await _persistence.SaveStateAsync(_stateMachine.CurrentState, _currentCycle);
        }

        // Loop exited - attempt auto-restart
        await HandleLoopExitAsync(cancellationToken);
    }

    private async Task ExecuteParallelResearchAndAnalysisAsync(CancellationToken cancellationToken)
    {
        // Execute Research step first
        _logger.LogInfo("Executing Research step (parallel mode)...");
        var researchResult = await ExecuteAgentAsync(LoopState.Research, cancellationToken);
        
        // Store research artifact
        if (researchResult.Success)
        {
            _artifactManager.SaveArtifact(_currentCycle!.CycleId, "research.md", researchResult.Output ?? "");
            
            var validationResult = ValidateArtifact(LoopState.Research, researchResult.Output ?? "");
            if (!validationResult.IsValid)
            {
                _logger.LogWarning($"Research validation failed: {string.Join(", ", validationResult.Errors)}");
                researchResult.Success = false;
                researchResult.Error = $"Validation failed: {string.Join("; ", validationResult.Errors)}";
            }
            
            await HandleMailboxMessagesAsync(LoopState.Research, researchResult);
            
            if (_currentCycle != null)
            {
                _currentCycle.Steps[LoopState.Research.ToString()] = new StepRecord
                {
                    Step = LoopState.Research,
                    StartTime = DateTime.UtcNow.AddMinutes(-5),
                    EndTime = DateTime.UtcNow,
                    Outcome = researchResult.Success ? StepOutcome.Success : StepOutcome.Failed,
                    ErrorMessage = researchResult.Error
                };
            }
        }

        // If Research succeeded, run Analysis in parallel
        if (researchResult.Success)
        {
            _logger.LogInfo("Executing Analysis step (parallel mode)...");
            
            // Set state to Analyze
            _stateMachine.TransitionTo(LoopState.Analyze);
            
            var analyzeResult = await ExecuteAgentAsync(LoopState.Analyze, cancellationToken);
            
            if (analyzeResult.Success)
            {
                _artifactManager.SaveArtifact(_currentCycle!.CycleId, "analysis.md", analyzeResult.Output ?? "");
                
                var validationResult = ValidateArtifact(LoopState.Analyze, analyzeResult.Output ?? "");
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"Analysis validation failed: {string.Join(", ", validationResult.Errors)}");
                    analyzeResult.Success = false;
                    analyzeResult.Error = $"Validation failed: {string.Join("; ", validationResult.Errors)}";
                }
                
                await HandleMailboxMessagesAsync(LoopState.Analyze, analyzeResult);
                
                if (_currentCycle != null)
                {
                    _currentCycle.Steps[LoopState.Analyze.ToString()] = new StepRecord
                    {
                        Step = LoopState.Analyze,
                        StartTime = DateTime.UtcNow,
                        EndTime = DateTime.UtcNow,
                        Outcome = analyzeResult.Success ? StepOutcome.Success : StepOutcome.Failed,
                        ErrorMessage = analyzeResult.Error
                    };
                }
            }
            
            // Handle completion for both steps
            if (researchResult.Success && analyzeResult.Success)
            {
                await HandleStepCompletionAsync(LoopState.Analyze, StepOutcome.Success, cancellationToken);
            }
            else
            {
                var failedState = !analyzeResult.Success ? LoopState.Analyze : LoopState.Research;
                var outcome = (!analyzeResult.Success ? analyzeResult : researchResult).Success ? StepOutcome.Success : StepOutcome.Failed;
                await HandleStepCompletionAsync(failedState, outcome, cancellationToken);
            }
        }
        else
        {
            // Research failed, handle failure
            await HandleStepCompletionAsync(LoopState.Research, StepOutcome.Failed, cancellationToken);
        }
    }

    private async Task HandleNextCycleDelayAsync(CancellationToken cancellationToken)
    {
        var schedule = _config.Cycle.Schedule;
        
        if (!string.IsNullOrEmpty(schedule))
        {
            var nextRun = GetNextCronRun(schedule);
            if (nextRun.HasValue)
            {
                var delay = nextRun.Value - DateTime.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogInfo($"Next scheduled run at {nextRun.Value:u}");
                    await Task.Delay(delay, cancellationToken);
                }
            }
            else
            {
                // Fallback to interval
                var interval = TimeSpan.FromMinutes(_config.Cycle.ScheduleIntervalMinutes);
                _logger.LogInfo($"Waiting {interval.TotalMinutes} minutes before next cycle...");
                await Task.Delay(interval, cancellationToken);
            }
        }
        else
        {
            var interval = TimeSpan.FromMinutes(_config.Cycle.ScheduleIntervalMinutes);
            _logger.LogInfo($"Waiting {interval.TotalMinutes} minutes before next cycle...");
            await Task.Delay(interval, cancellationToken);
        }
        
        if (!cancellationToken.IsCancellationRequested)
        {
            _stateMachine.TransitionTo(LoopState.Plan);
        }
    }

    private DateTime? GetNextCronRun(string cronExpression)
    {
        try
        {
            var parts = cronExpression.Split(' ');
            if (parts.Length < 5) return null;
            
            var now = DateTime.UtcNow;
            
            // Parse simple cron: minute hour day month dayofweek
            // Support: "* * * * *" (min hour day month dow)
            if (int.TryParse(parts[0], out var minute) && 
                int.TryParse(parts[1], out var hour) &&
                int.TryParse(parts[2], out var day) &&
                int.TryParse(parts[3], out var month))
            {
                // Simple daily cron: run at specific hour:minute daily
                if (minute >= 0 && minute <= 59 && hour >= 0 && hour <= 23)
                {
                    var next = now.Date.AddHours(hour).AddMinutes(minute);
                    if (next <= now)
                    {
                        next = next.AddDays(1);
                    }
                    return next;
                }
            }
            
            return null;
        }
        catch
        {
            return null;
        }
    }

    private async Task HandleLoopExitAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || _isRestarting)
            return;

        _isRestarting = true;
        _restartCount++;

        if (_currentCycle != null)
        {
            _currentCycle.RestartCount = _restartCount;
        }

        if (_restartCount >= _config.Resilience.MaxRestartAttempts)
        {
            _logger.LogError($"Exceeded max restart attempts ({_config.Resilience.MaxRestartAttempts}). Giving up.");
            _stateMachine.TransitionTo(LoopState.Failed);
            ErrorOccurred?.Invoke(this, new Exception($"Loop exited after {_restartCount} restart attempts"));
            _isRestarting = false;
            return;
        }

        var delay = Math.Min(
            _config.Resilience.BaseRestartDelayMinutes * Math.Pow(2, _restartCount - 1),
            _config.Resilience.MaxRestartDelayMinutes);
        
        _logger.LogWarning($"Loop exited. Restarting in {delay} minutes (attempt {_restartCount}/{_config.Resilience.MaxRestartAttempts})");
        
        await Task.Delay(TimeSpan.FromMinutes(delay), cancellationToken);

        if (!cancellationToken.IsCancellationRequested)
        {
            _healthMonitor?.SetRestartCount(_restartCount);
            _stateMachine.TransitionTo(LoopState.Plan);
            
            _runCts = new CancellationTokenSource();
            _runTask = RunLoopAsync(_runCts.Token);
        }
        
        _isRestarting = false;
    }

    private async Task ExecuteStepAsync(CancellationToken cancellationToken)
    {
        var state = _stateMachine.CurrentState;
        _logger.LogInfo($"Executing step: {state}");

        var stepRecord = new StepRecord
        {
            Step = state,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var result = await ExecuteAgentAsync(state, cancellationToken);
            
            stepRecord.Outcome = result.Success ? StepOutcome.Success : StepOutcome.Failed;
            stepRecord.ErrorMessage = result.Error;

            if (result.Success)
            {
                var artifactName = GetArtifactNameForState(state);
                _artifactManager.SaveArtifact(_currentCycle!.CycleId, artifactName, result.Output ?? "");
                
                // Validate the artifact
                var validationResult = ValidateArtifact(state, result.Output ?? "");
                if (!validationResult.IsValid)
                {
                    _logger.LogWarning($"Artifact validation failed for {artifactName}: {string.Join(", ", validationResult.Errors)}");
                    stepRecord.Outcome = StepOutcome.Failed;
                    stepRecord.ErrorMessage = $"Validation failed: {string.Join("; ", validationResult.Errors)}";
                    result.Success = false;
                    result.Error = stepRecord.ErrorMessage;
                }
                else if (validationResult.Warnings.Any())
                {
                    _logger.LogWarning($"Artifact warnings for {artifactName}: {string.Join(", ", validationResult.Warnings)}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            stepRecord.Outcome = StepOutcome.Timeout;
            stepRecord.ErrorMessage = "Step timed out";
            _logger.LogError($"Step {state} timed out");
        }
        catch (Exception ex)
        {
            stepRecord.Outcome = StepOutcome.Failed;
            stepRecord.ErrorMessage = ex.Message;
            _logger.LogError($"Step {state} failed: {ex.Message}");
        }

        stepRecord.EndTime = DateTime.UtcNow;
        
        if (_currentCycle != null)
        {
            _currentCycle.Steps[state.ToString()] = stepRecord;
            _currentCycle.LastStepOutcome = stepRecord.Outcome;
        }

        await HandleStepCompletionAsync(state, stepRecord.Outcome, cancellationToken);
    }

    private async Task<AgentInvocationResult> ExecuteAgentAsync(LoopState state, CancellationToken cancellationToken)
    {
        if (_currentCycle == null)
        {
            _currentCycle = new CycleContext
            {
                CycleId = _artifactManager.CreateCycleDirectory(),
                CycleStartTime = DateTime.UtcNow,
                CurrentState = state
            };
        }

        _currentCycle.CurrentState = state;

        AgentContext context = state switch
        {
            LoopState.Plan => _contextBuilder.BuildPlanAgentContext(_currentCycle.CycleId),
            LoopState.Research => _contextBuilder.BuildResearchAgentContext(_currentCycle.CycleId),
            LoopState.Analyze => _contextBuilder.BuildAnalysisAgentContext(_currentCycle.CycleId),
            LoopState.Synthesize => _contextBuilder.BuildSynthesisAgentContext(_currentCycle.CycleId),
            LoopState.Evaluate => _contextBuilder.BuildEvaluationAgentContext(_currentCycle.CycleId),
            _ => throw new InvalidOperationException($"No agent context for state: {state}")
        };

        var result = await _agentInvoker.InvokeAsync(context, cancellationToken);

        await HandleMailboxMessagesAsync(state, result);

        return result;
    }

    private async Task HandleMailboxMessagesAsync(LoopState state, AgentInvocationResult result)
    {
        if (!result.Success || string.IsNullOrEmpty(result.Output))
            return;

        var (forwardAgent, backwardAgent) = GetMailboxRouting(state);
        
        if (!string.IsNullOrEmpty(forwardAgent))
        {
            var forwardMessage = new MailboxMessage
            {
                FromAgent = state.ToString(),
                ToAgent = forwardAgent,
                Content = ExtractMailboxContent(result.Output, forwardAgent),
                Type = MessageType.Forward
            };
            _mailboxManager.Deposit(forwardAgent, forwardMessage);
        }

        if (!string.IsNullOrEmpty(backwardAgent))
        {
            var feedbackMessage = new MailboxMessage
            {
                FromAgent = state.ToString(),
                ToAgent = backwardAgent,
                Content = ExtractFeedbackContent(result.Output),
                Type = MessageType.Feedback
            };
            _mailboxManager.Deposit(backwardAgent, feedbackMessage);
        }

        if (state == LoopState.Evaluate)
        {
            var broadcastContent = ExtractBroadcastContent(result.Output);
            _mailboxManager.Broadcast("evaluation", broadcastContent);
        }

        await Task.CompletedTask;
    }

    private (string? Forward, string? Backward) GetMailboxRouting(LoopState state)
    {
        return state switch
        {
            LoopState.Plan => ("research", null),
            LoopState.Research => ("analysis", "plan"),
            LoopState.Analyze => ("synthesis", "research"),
            LoopState.Synthesize => ("evaluation", "analysis"),
            LoopState.Evaluate => (null, null),
            _ => (null, null)
        };
    }

    private string GetArtifactNameForState(LoopState state)
    {
        return state switch
        {
            LoopState.Plan => "plan.md",
            LoopState.Research => "research.md",
            LoopState.Analyze => "analysis.md",
            LoopState.Synthesize => "strategy.json",
            LoopState.Evaluate => "evaluation.md",
            _ => throw new InvalidOperationException($"No artifact for state: {state}")
        };
    }

    private string ExtractMailboxContent(string output, string targetAgent)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            $@"<mailbox_to_{targetAgent}>(.*?)</mailbox_to_{targetAgent}>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var match = pattern.Match(output);
        return match.Success ? match.Groups[1].Value.Trim() : "No forward message";
    }

    private string ExtractFeedbackContent(string output)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            @"<feedback>(.*?)</feedback>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var match = pattern.Match(output);
        return match.Success ? match.Groups[1].Value.Trim() : "No feedback";
    }

    private string ExtractBroadcastContent(string output)
    {
        var pattern = new System.Text.RegularExpressions.Regex(
            @"<broadcast>(.*?)</broadcast>",
            System.Text.RegularExpressions.RegexOptions.Singleline);

        var match = pattern.Match(output);
        return match.Success ? match.Groups[1].Value.Trim() : "Broadcast message";
    }

    private async Task HandleStepCompletionAsync(LoopState state, StepOutcome outcome, CancellationToken cancellationToken)
    {
        if (outcome == StepOutcome.Failed || outcome == StepOutcome.Timeout)
        {
            var maxRetries = GetMaxRetriesForState(state);
            
            if (_currentCycle != null && _currentCycle.RetryCount < maxRetries)
            {
                _currentCycle.RetryCount++;
                _logger.LogWarning($"Step {state} failed, retrying ({_currentCycle.RetryCount}/{maxRetries})");
                
                var backoffMinutes = Math.Pow(2, _currentCycle.RetryCount) * 5;
                await Task.Delay(TimeSpan.FromMinutes(backoffMinutes), cancellationToken);
                
                return;
            }

            _logger.LogError($"Step {state} exhausted retries");
            _stateMachine.TransitionTo(LoopState.Failed);
            ErrorOccurred?.Invoke(this, new Exception($"Step {state} failed: {outcome}"));
        }

        if (state == LoopState.Evaluate)
        {
            _currentCycle!.CycleEndTime = DateTime.UtcNow;
            CycleCompleted?.Invoke(this, _currentCycle.CycleId);
            
            _artifactManager.ArchiveOldCycles();
            
            _stateMachine.TransitionTo(LoopState.WaitingForNextCycle);
        }
    }

    private int GetMaxRetriesForState(LoopState state)
    {
        return state switch
        {
            LoopState.Plan => _config.Agents.Plan.MaxRetries,
            LoopState.Research => _config.Agents.Research.MaxRetries,
            LoopState.Analyze => _config.Agents.Analyze.MaxRetries,
            LoopState.Synthesize => _config.Agents.Synthesis.MaxRetries,
            LoopState.Evaluate => _config.Agents.Evaluation.MaxRetries,
            _ => 3
        };
    }

    private void OnStateTransition(object? sender, StateTransitionEventArgs e)
    {
        _logger.LogInfo($"State transition: {e.FromState} -> {e.ToState}");
        StateChanged?.Invoke(this, e.ToState);
    }

    public void Pause(string? reason = null)
    {
        if (_stateMachine.CanTransitionTo(LoopState.Paused))
        {
            _stateMachine.TransitionTo(LoopState.Paused);
            
            if (_currentCycle != null)
            {
                _currentCycle.IsPaused = true;
                _currentCycle.PausedAt = DateTime.UtcNow;
                _currentCycle.PauseReason = reason;
            }
            
            _logger.LogInfo($"Agent Runner paused: {reason}");
        }
    }

    public void Resume()
    {
        if (_stateMachine.CurrentState == LoopState.Paused)
        {
            if (_currentCycle != null)
            {
                _currentCycle.IsPaused = false;
                _currentCycle.PausedAt = null;
                _currentCycle.PauseReason = null;
            }
            
            var nextState = _stateMachine.GetNextRequiredState();
            _stateMachine.TransitionTo(nextState);
            
            _logger.LogInfo("Agent Runner resumed");
        }
    }

    public void Abort()
    {
        _logger.LogInfo("Agent Runner aborting current cycle");
        _runCts?.Cancel();
        
        if (_stateMachine.CurrentState != LoopState.Paused)
        {
            _stateMachine.TransitionTo(LoopState.Failed);
        }
    }

    public void ForceNewCycle()
    {
        if (_stateMachine.CurrentState == LoopState.WaitingForNextCycle)
        {
            _stateMachine.TransitionTo(LoopState.Plan);
        }
    }

    public void InjectContext(string agentName, string content)
    {
        var message = new MailboxMessage
        {
            FromAgent = "operator",
            ToAgent = agentName,
            Content = content,
            Type = MessageType.Forward
        };
        
        _mailboxManager.Deposit(agentName, message);
        _logger.LogInfo($"Injected context to {agentName}");
    }

    private ArtifactValidationResult ValidateArtifact(LoopState state, string content)
    {
        try
        {
            var validator = ArtifactValidators.ForState(state);
            return validator.Validate(content);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error validating artifact for {state}: {ex.Message}");
            return ArtifactValidationResult.Failure($"Validation error: {ex.Message}");
        }
    }
}

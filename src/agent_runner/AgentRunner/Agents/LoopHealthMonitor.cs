using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.StateMachine;

namespace AgentRunner.Agents;

public class LoopHealthMonitor
{
    private readonly LoopStateMachine _stateMachine;
    private readonly AgentRunnerConfig _config;
    private readonly IEventLogger _logger;
    private DateTime _lastStateChange;
    private LoopState _lastState;
    private bool _hasWarnedStall;
    private bool _hasCriticalStalled;
    private readonly object _lock = new();

    public event EventHandler<LoopHealthEventArgs>? HealthWarning;
    public event EventHandler<LoopHealthEventArgs>? HealthCritical;
    public event EventHandler? LoopRecovered;

    public bool IsStalled { get; private set; }
    public bool IsCritical { get; private set; }
    public TimeSpan TimeSinceLastChange => DateTime.UtcNow - _lastStateChange;

    public LoopHealthMonitor(
        LoopStateMachine stateMachine, 
        AgentRunnerConfig config,
        IEventLogger logger)
    {
        _stateMachine = stateMachine;
        _config = config;
        _logger = logger;
        _lastStateChange = DateTime.UtcNow;
        _lastState = stateMachine.CurrentState;

        _stateMachine.StateTransition += OnStateTransition;
    }

    private void OnStateTransition(object? sender, StateTransitionEventArgs e)
    {
        lock (_lock)
        {
            var previousState = _lastState;
            _lastState = e.ToState;
            _lastStateChange = DateTime.UtcNow;

            _hasWarnedStall = false;
            _hasCriticalStalled = false;
            IsStalled = false;
            IsCritical = false;

            _logger.LogInfo($"State changed: {previousState} -> {e.ToState}");
        }
    }

    public LoopHealthCheckResult CheckHealth()
    {
        lock (_lock)
        {
            var timeSinceChange = DateTime.UtcNow - _lastStateChange;
            var stallWarningThreshold = TimeSpan.FromMinutes(_config.Resilience.StallWarningMinutes);
            var stallCriticalThreshold = TimeSpan.FromMinutes(_config.Resilience.StallCriticalMinutes);

            var result = new LoopHealthCheckResult
            {
                CurrentState = _stateMachine.CurrentState,
                LastStateChange = _lastStateChange,
                TimeSinceLastChange = timeSinceChange,
                IsStalled = timeSinceChange > stallWarningThreshold,
                IsCritical = timeSinceChange > stallCriticalThreshold,
                RestartCount = 0
            };

            if (result.IsStalled && !_hasWarnedStall)
            {
                _hasWarnedStall = true;
                IsStalled = true;
                _logger.LogWarning($"Loop stalled: no state change for {timeSinceChange.TotalMinutes:F1} minutes");
                HealthWarning?.Invoke(this, new LoopHealthEventArgs
                {
                    CurrentState = _stateMachine.CurrentState,
                    TimeSinceLastChange = timeSinceChange,
                    Message = $"Loop has not changed state in {timeSinceChange.TotalMinutes:F1} minutes"
                });
            }

            if (result.IsCritical && !_hasCriticalStalled)
            {
                _hasCriticalStalled = true;
                IsCritical = true;
                _logger.LogError($"Loop critically stalled: no state change for {timeSinceChange.TotalMinutes:F1} minutes");
                HealthCritical?.Invoke(this, new LoopHealthEventArgs
                {
                    CurrentState = _stateMachine.CurrentState,
                    TimeSinceLastChange = timeSinceChange,
                    Message = $"CRITICAL: Loop stalled for {timeSinceChange.TotalMinutes:F1} minutes"
                });
            }

            return result;
        }
    }

    public void RecordRecovery()
    {
        lock (_lock)
        {
            _hasWarnedStall = false;
            _hasCriticalStalled = false;
            IsStalled = false;
            IsCritical = false;
            _logger.LogInfo("Loop recovered from stalled state");
            LoopRecovered?.Invoke(this, EventArgs.Empty);
        }
    }

    public void SetRestartCount(int count)
    {
        lock (_lock)
        {
            _lastStateChange = DateTime.UtcNow;
        }
    }
}

public class LoopHealthCheckResult
{
    public LoopState CurrentState { get; set; }
    public DateTime LastStateChange { get; set; }
    public TimeSpan TimeSinceLastChange { get; set; }
    public bool IsStalled { get; set; }
    public bool IsCritical { get; set; }
    public int RestartCount { get; set; }
}

public class LoopHealthEventArgs : EventArgs
{
    public LoopState CurrentState { get; set; }
    public TimeSpan TimeSinceLastChange { get; set; }
    public string Message { get; set; } = string.Empty;
}

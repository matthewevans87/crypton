using AgentRunner.StateMachine;

namespace AgentRunner.StateMachine;

public class LoopStateMachine
{
    private LoopState _currentState = LoopState.Idle;
    private readonly object _lock = new();
    private readonly Dictionary<LoopState, List<LoopState>> _validTransitions;

    public LoopState CurrentState
    {
        get { lock (_lock) return _currentState; }
    }

    public event EventHandler<StateTransitionEventArgs>? StateTransition;
    public event EventHandler<LoopState>? StateEntered;
    public event EventHandler<LoopState>? StateExited;

    public LoopStateMachine()
    {
        _validTransitions = new Dictionary<LoopState, List<LoopState>>
        {
            { LoopState.Idle, new List<LoopState> { LoopState.Plan } },
            { LoopState.Plan, new List<LoopState> { LoopState.Research, LoopState.Failed, LoopState.Paused } },
            { LoopState.Research, new List<LoopState> { LoopState.Analyze, LoopState.Failed, LoopState.Paused } },
            { LoopState.Analyze, new List<LoopState> { LoopState.Synthesize, LoopState.Failed, LoopState.Paused } },
            { LoopState.Synthesize, new List<LoopState> { LoopState.Execute, LoopState.Failed, LoopState.Paused } },
            { LoopState.Execute, new List<LoopState> { LoopState.Evaluate, LoopState.Failed, LoopState.Paused } },
            { LoopState.Evaluate, new List<LoopState> { LoopState.WaitingForNextCycle, LoopState.Failed } },
            { LoopState.WaitingForNextCycle, new List<LoopState> { LoopState.Plan, LoopState.Idle, LoopState.Paused } },
            { LoopState.Paused, new List<LoopState> { LoopState.Plan, LoopState.Idle } },
            { LoopState.Failed, new List<LoopState> { LoopState.Idle } }
        };
    }

    public bool CanTransitionTo(LoopState newState)
    {
        lock (_lock)
        {
            if (!_validTransitions.TryGetValue(_currentState, out var validStates))
                return false;
            return validStates.Contains(newState);
        }
    }

    public bool TransitionTo(LoopState newState)
    {
        lock (_lock)
        {
            if (!CanTransitionTo(newState))
                return false;

            var previousState = _currentState;
            var eventArgs = new StateTransitionEventArgs(previousState, newState);

            StateExited?.Invoke(this, previousState);
            _currentState = newState;
            StateEntered?.Invoke(this, newState);
            StateTransition?.Invoke(this, eventArgs);

            return true;
        }
    }

    public bool IsTerminalState()
    {
        lock (_lock)
        {
            return _currentState == LoopState.Idle || 
                   _currentState == LoopState.Failed ||
                   _currentState == LoopState.Paused;
        }
    }

    public LoopState GetNextRequiredState()
    {
        lock (_lock)
        {
            return _currentState switch
            {
                LoopState.Idle => LoopState.Plan,
                LoopState.Plan => LoopState.Research,
                LoopState.Research => LoopState.Analyze,
                LoopState.Analyze => LoopState.Synthesize,
                LoopState.Synthesize => LoopState.Execute,
                LoopState.Execute => LoopState.Evaluate,
                LoopState.Evaluate => LoopState.WaitingForNextCycle,
                LoopState.WaitingForNextCycle => LoopState.Plan,
                _ => _currentState
            };
        }
    }
}

public class StateTransitionEventArgs : EventArgs
{
    public LoopState FromState { get; }
    public LoopState ToState { get; }
    public DateTime Timestamp { get; }

    public StateTransitionEventArgs(LoopState fromState, LoopState toState)
    {
        FromState = fromState;
        ToState = toState;
        Timestamp = DateTime.UtcNow;
    }
}

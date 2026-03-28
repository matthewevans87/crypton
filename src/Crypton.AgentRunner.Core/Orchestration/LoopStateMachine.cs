using AgentRunner.Abstractions;
using AgentRunner.Domain;

namespace AgentRunner.Orchestration;

/// <summary>
/// Thread-safe state machine governing valid loop state transitions.
/// Fires C# events on every transition.
/// </summary>
public sealed class LoopStateMachine : ILoopStateMachine
{
    private LoopState _current = LoopState.Idle;
    private readonly object _lock = new();

    private static readonly IReadOnlyDictionary<LoopState, LoopState[]> ValidTransitions =
        new Dictionary<LoopState, LoopState[]>
        {
            [LoopState.Idle] = [LoopState.Plan, LoopState.Evaluate],
            [LoopState.Plan] = [LoopState.Research, LoopState.Failed, LoopState.Paused],
            [LoopState.Research] = [LoopState.Analyze, LoopState.Failed, LoopState.Paused],
            [LoopState.Analyze] = [LoopState.Synthesize, LoopState.Failed, LoopState.Paused],
            [LoopState.Synthesize] = [LoopState.WaitingForNextCycle, LoopState.Failed, LoopState.Paused],
            [LoopState.Evaluate] = [LoopState.Plan, LoopState.Failed],
            [LoopState.WaitingForNextCycle] = [LoopState.Evaluate, LoopState.Plan, LoopState.Idle, LoopState.Paused],
            [LoopState.Paused] = [LoopState.Plan, LoopState.Research, LoopState.Analyze, LoopState.Synthesize, LoopState.Evaluate, LoopState.WaitingForNextCycle, LoopState.Idle],
            [LoopState.Failed] = [LoopState.Idle],
        };

    public LoopState CurrentState
    {
        get { lock (_lock) return _current; }
    }

    public event EventHandler<StateTransitionEventArgs>? StateTransition;
    public event EventHandler<LoopState>? StateEntered;
    public event EventHandler<LoopState>? StateExited;

    public bool CanTransitionTo(LoopState newState)
    {
        lock (_lock)
            return ValidTransitions.TryGetValue(_current, out var allowed) && allowed.Contains(newState);
    }

    public bool TransitionTo(LoopState newState)
    {
        LoopState previous;
        lock (_lock)
        {
            if (!CanTransitionTo(newState))
                return false;
            previous = _current;
            _current = newState;
        }

        StateExited?.Invoke(this, previous);
        StateEntered?.Invoke(this, newState);
        StateTransition?.Invoke(this, new StateTransitionEventArgs(previous, newState, DateTimeOffset.UtcNow));
        return true;
    }
}

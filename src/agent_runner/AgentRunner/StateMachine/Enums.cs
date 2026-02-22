namespace AgentRunner.StateMachine;

public enum LoopState
{
    Idle,
    Plan,
    Research,
    Analyze,
    Synthesize,
    Execute,
    Evaluate,
    WaitingForNextCycle,
    Paused,
    Failed
}

public enum StepOutcome
{
    Success,
    Failed,
    Timeout,
    Skipped
}

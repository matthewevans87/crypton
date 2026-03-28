namespace AgentRunner.Domain;

public enum LoopState
{
    Idle,
    Plan,
    Research,
    Analyze,
    Synthesize,
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

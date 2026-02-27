using AgentRunner.StateMachine;

namespace AgentRunner.StateMachine;

public class CycleContext
{
    public string CycleId { get; set; } = string.Empty;
    public DateTime CycleStartTime { get; set; }
    public DateTime? CycleEndTime { get; set; }
    public LoopState CurrentState { get; set; }
    public StepOutcome LastStepOutcome { get; set; }
    public string? LastError { get; set; }
    public int RetryCount { get; set; }
    public Dictionary<string, StepRecord> Steps { get; set; } = new();
    public Dictionary<string, string> Artifacts { get; set; } = new();
    public bool IsPaused { get; set; }
    public DateTime? PausedAt { get; set; }
    public string? PauseReason { get; set; }
    
    // Health tracking
    public int RestartCount { get; set; }
    public DateTime LastStateChange { get; set; }
    public string? LastSuccessfulCycle { get; set; }
    public bool IsStalled { get; set; }
    public DateTime? StallDetectedAt { get; set; }
}

public class StepRecord
{
    public LoopState Step { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public StepOutcome Outcome { get; set; }
    public string? ErrorMessage { get; set; }
    public int RetryCount { get; set; }
    public TimeSpan Duration => EndTime.HasValue ? EndTime.Value - StartTime : DateTime.UtcNow - StartTime;
}

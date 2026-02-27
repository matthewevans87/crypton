namespace MonitoringDashboard.Models;

public class AgentState
{
    public string CurrentState { get; set; } = "Idle"; // "Plan", "Research", "Analyze", "Synthesize", "Execute", "Evaluate", "Idle", "WaitingForNextCycle"
    public string? ActiveAgent { get; set; }
    public DateTime StateStartedAt { get; set; }
    public bool IsRunning { get; set; }
    public TimeSpan TimeInState => DateTime.UtcNow - StateStartedAt;
    public double ProgressPercent { get; set; }
    public string? CurrentTool { get; set; }
    public int TokensUsed { get; set; }
    public double? LastLatencyMs { get; set; }
}

public class LoopStatus
{
    public AgentState AgentState { get; set; } = new();
    public DateTime? LastCycleCompletedAt { get; set; }
    public DateTime? NextCycleExpectedAt { get; set; }
    public string? CurrentArtifact { get; set; } // "plan.md", "research.md", "analysis.md", "strategy.json", "evaluation.md"
    public int CycleNumber { get; set; }
}

public class ToolCall
{
    public string Id { get; set; } = string.Empty;
    public string ToolName { get; set; } = string.Empty;
    public string Input { get; set; } = string.Empty;
    public string? Output { get; set; }
    public DateTime CalledAt { get; set; }
    public long DurationMs { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ReasoningStep
{
    public DateTime Timestamp { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? Token { get; set; }
}

public class EvaluationSummary
{
    public string CycleId { get; set; } = string.Empty;
    public DateTime EvaluatedAt { get; set; }
    public string Rating { get; set; } = "F"; // "A", "B", "C", "D", "F"
    public decimal NetPnL { get; set; }
    public decimal Return { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public string Verdict { get; set; } = string.Empty;
    public List<string> Recommendations { get; set; } = [];
    public string? RatingTrend { get; set; } // "up", "down", "stable"
}

public class CyclePerformance
{
    public string CycleId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public decimal RealizedPnL { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal TotalPnL => RealizedPnL + UnrealizedPnL;
    public decimal WinRate { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }
    public decimal MaxDrawdown { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public bool DailyLossLimitBreached { get; set; }
}

public class LifetimePerformance
{
    public decimal TotalPnL { get; set; }
    public decimal TotalReturn { get; set; }
    public decimal WinRate { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public int LosingTrades { get; set; }
    public int LongestWinningStreak { get; set; }
    public int LongestLosingStreak { get; set; }
    public decimal? SharpeRatio { get; set; }
}

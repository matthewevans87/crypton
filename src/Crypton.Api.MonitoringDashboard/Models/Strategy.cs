namespace MonitoringDashboard.Models;

public class StrategyOverview
{
    public string Mode { get; set; } = "paper"; // "paper" or "live"
    public string Posture { get; set; } = "moderate"; // "aggressive", "moderate", "defensive", "flat", "exit_all"
    public DateTime ValidUntil { get; set; }
    public string Rationale { get; set; } = string.Empty;
    public decimal MaxDrawdown { get; set; }
    public decimal DailyLossLimit { get; set; }
    public decimal MaxExposure { get; set; }
    public DateTime LastUpdated { get; set; }
    
    public TimeSpan TimeRemaining => ValidUntil - DateTime.UtcNow;
    public bool IsExpired => DateTime.UtcNow > ValidUntil;
}

public class PositionRule
{
    public string Asset { get; set; } = string.Empty;
    public string EntryCondition { get; set; } = string.Empty;
    public decimal Allocation { get; set; }
    public decimal? StopLoss { get; set; }
    public decimal? TakeProfit { get; set; }
    public List<TakeProfitTarget> TakeProfitTargets { get; set; } = [];
    public string? InvalidationCondition { get; set; }
    public string? TimeBasedExit { get; set; }
}

public class TakeProfitTarget
{
    public decimal Price { get; set; }
    public decimal ClosePercent { get; set; }
}

public class Strategy
{
    public StrategyOverview Overview { get; set; } = new();
    public List<PositionRule> PositionRules { get; set; } = [];
}

public class StrategyHistoryItem
{
    public string Id { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string Mode { get; set; } = string.Empty;
    public string Posture { get; set; } = string.Empty;
    public int PositionCount { get; set; }
}

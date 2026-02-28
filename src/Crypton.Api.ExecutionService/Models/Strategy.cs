using System.Text.Json.Serialization;

namespace Crypton.Api.ExecutionService.Models;

/// <summary>The formal contract between the Learning Loop and the Execution Service.</summary>
public sealed class StrategyDocument
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }  // "paper" | "live"

    [JsonPropertyName("validity_window")]
    public required DateTimeOffset ValidityWindow { get; init; }

    [JsonPropertyName("posture")]
    public required string Posture { get; init; }  // "aggressive"|"moderate"|"defensive"|"flat"|"exit_all"

    [JsonPropertyName("posture_rationale")]
    public string PostureRationale { get; init; } = string.Empty;

    [JsonPropertyName("portfolio_risk")]
    public required PortfolioRisk PortfolioRisk { get; init; }

    [JsonPropertyName("positions")]
    public IReadOnlyList<StrategyPosition> Positions { get; init; } = [];

    [JsonPropertyName("strategy_rationale")]
    public string StrategyRationale { get; init; } = string.Empty;

    // Computed: a stable identifier for this strategy (SHA256 of the raw JSON).
    public string? Id { get; set; }
}

public sealed class PortfolioRisk
{
    [JsonPropertyName("max_drawdown_pct")]
    public required decimal MaxDrawdownPct { get; init; }

    [JsonPropertyName("daily_loss_limit_usd")]
    public required decimal DailyLossLimitUsd { get; init; }

    [JsonPropertyName("max_total_exposure_pct")]
    public required decimal MaxTotalExposurePct { get; init; }

    [JsonPropertyName("max_per_position_pct")]
    public required decimal MaxPerPositionPct { get; init; }

    [JsonPropertyName("safe_mode_triggers")]
    public IReadOnlyList<string> SafeModeTriggers { get; init; } = [];
}

public sealed class StrategyPosition
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    [JsonPropertyName("direction")]
    public required string Direction { get; init; }  // "long" | "short"

    [JsonPropertyName("allocation_pct")]
    public required decimal AllocationPct { get; init; }

    [JsonPropertyName("entry_type")]
    public required string EntryType { get; init; }  // "market" | "limit" | "conditional"

    [JsonPropertyName("entry_condition")]
    public string? EntryCondition { get; init; }

    [JsonPropertyName("entry_limit_price")]
    public decimal? EntryLimitPrice { get; init; }

    [JsonPropertyName("take_profit_targets")]
    public IReadOnlyList<TakeProfitTarget> TakeProfitTargets { get; init; } = [];

    [JsonPropertyName("stop_loss")]
    public StopLoss? StopLoss { get; init; }

    [JsonPropertyName("time_exit_utc")]
    public DateTimeOffset? TimeExitUtc { get; init; }

    [JsonPropertyName("invalidation_condition")]
    public string? InvalidationCondition { get; init; }
}

public sealed class TakeProfitTarget
{
    [JsonPropertyName("price")]
    public required decimal Price { get; init; }

    [JsonPropertyName("close_pct")]
    public required decimal ClosePct { get; init; }
}

public sealed class StopLoss
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }  // "hard" | "trailing"

    [JsonPropertyName("price")]
    public decimal? Price { get; init; }

    [JsonPropertyName("trail_pct")]
    public decimal? TrailPct { get; init; }
}

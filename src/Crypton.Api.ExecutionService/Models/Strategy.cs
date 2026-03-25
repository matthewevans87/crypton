using System.Text.Json.Serialization;

namespace Crypton.Api.ExecutionService.Models;

/// <summary>The formal contract between the Learning Loop and the Execution Service.</summary>
public sealed class StrategyDocument
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }  // "paper" | "live"

    [JsonPropertyName("validityWindow")]
    public required DateTimeOffset ValidityWindow { get; init; }

    [JsonPropertyName("posture")]
    public required string Posture { get; init; }  // "aggressive"|"moderate"|"defensive"|"flat"|"exit_all"

    [JsonPropertyName("postureRationale")]
    public string PostureRationale { get; init; } = string.Empty;

    [JsonPropertyName("portfolioRisk")]
    public required PortfolioRisk PortfolioRisk { get; init; }

    [JsonPropertyName("positions")]
    public IReadOnlyList<StrategyPosition> Positions { get; init; } = [];

    [JsonPropertyName("strategyRationale")]
    public string StrategyRationale { get; init; } = string.Empty;

    // Computed: a stable identifier for this strategy (SHA256 of the raw JSON).
    public string? Id { get; set; }
}

public sealed class PortfolioRisk
{
    [JsonPropertyName("maxDrawdownPct")]
    public required decimal MaxDrawdownPct { get; init; }

    [JsonPropertyName("dailyLossLimitUsd")]
    public required decimal DailyLossLimitUsd { get; init; }

    [JsonPropertyName("maxTotalExposurePct")]
    public required decimal MaxTotalExposurePct { get; init; }

    [JsonPropertyName("maxPerPositionPct")]
    public required decimal MaxPerPositionPct { get; init; }

    [JsonPropertyName("safeModeTriggers")]
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

    [JsonPropertyName("allocationPct")]
    public required decimal AllocationPct { get; init; }

    [JsonPropertyName("entryType")]
    public required string EntryType { get; init; }  // "market" | "limit" | "conditional"

    [JsonPropertyName("entryCondition")]
    public string? EntryCondition { get; init; }

    [JsonPropertyName("entryLimitPrice")]
    public decimal? EntryLimitPrice { get; init; }

    [JsonPropertyName("takeProfitTargets")]
    public IReadOnlyList<TakeProfitTarget> TakeProfitTargets { get; init; } = [];

    [JsonPropertyName("stopLoss")]
    public StopLoss? StopLoss { get; init; }

    [JsonPropertyName("timeExitUtc")]
    public DateTimeOffset? TimeExitUtc { get; init; }

    [JsonPropertyName("invalidationCondition")]
    public string? InvalidationCondition { get; init; }
}

public sealed class TakeProfitTarget
{
    [JsonPropertyName("price")]
    public required decimal Price { get; init; }

    [JsonPropertyName("closePct")]
    public required decimal ClosePct { get; init; }
}

public sealed class StopLoss
{
    [JsonPropertyName("type")]
    public required string Type { get; init; }  // "hard" | "trailing"

    [JsonPropertyName("price")]
    public decimal? Price { get; init; }

    [JsonPropertyName("trailPct")]
    public decimal? TrailPct { get; init; }
}

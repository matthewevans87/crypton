using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Positions;

/// <summary>
/// The authoritative record of a single open position managed by the Execution Service.
/// Persisted to disk on every mutation.
/// </summary>
public sealed class OpenPosition
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }  // Internal position ID (GUID)

    [JsonPropertyName("strategy_position_id")]
    public required string StrategyPositionId { get; init; }

    [JsonPropertyName("strategy_id")]
    public required string StrategyId { get; init; }

    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    [JsonPropertyName("direction")]
    public required string Direction { get; init; }  // "long" | "short"

    [JsonPropertyName("quantity")]
    public required decimal Quantity { get; set; }

    [JsonPropertyName("average_entry_price")]
    public required decimal AverageEntryPrice { get; set; }

    [JsonPropertyName("opened_at")]
    public required DateTimeOffset OpenedAt { get; init; }

    [JsonPropertyName("trailing_stop_price")]
    public decimal? TrailingStopPrice { get; set; }

    [JsonPropertyName("take_profit_targets_hit")]
    public HashSet<int> TakeProfitTargetsHit { get; init; } = [];

    [JsonPropertyName("origin")]
    public string Origin { get; init; } = "strategy";  // "strategy" | "reconciled" | "external"

    [JsonIgnore]
    public decimal UnrealizedPnl { get; set; }

    [JsonIgnore]
    public decimal CurrentPrice { get; set; }
}

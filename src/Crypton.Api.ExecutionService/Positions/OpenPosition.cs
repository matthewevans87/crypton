using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Positions;

/// <summary>
/// The authoritative record of a single open position managed by the Execution Service.
/// Persisted to disk on every mutation.
/// </summary>
public sealed class OpenPosition
{
    public required string Id { get; init; }  // Internal position ID (GUID)
    public required string StrategyPositionId { get; init; }
    public required string StrategyId { get; init; }
    public required string Asset { get; init; }
    public required string Direction { get; init; }  // "long" | "short"
    public required decimal Quantity { get; set; }
    public required decimal AverageEntryPrice { get; set; }
    public required DateTimeOffset OpenedAt { get; init; }
    public decimal? TrailingStopPrice { get; set; }
    public HashSet<int> TakeProfitTargetsHit { get; init; } = [];
    public string Origin { get; init; } = "strategy";  // "strategy" | "reconciled" | "external"

    [JsonIgnore]
    public decimal UnrealizedPnl { get; set; }

    [JsonIgnore]
    public decimal CurrentPrice { get; set; }
}

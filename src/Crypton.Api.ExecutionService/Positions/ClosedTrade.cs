using System.Text.Json.Serialization;

namespace Crypton.Api.ExecutionService.Positions;

/// <summary>A completed trade, written to the trade history when a position is fully closed.</summary>
public sealed class ClosedTrade
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("position_id")]
    public required string PositionId { get; init; }

    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    [JsonPropertyName("direction")]
    public required string Direction { get; init; }

    [JsonPropertyName("quantity")]
    public required decimal Quantity { get; init; }

    [JsonPropertyName("entry_price")]
    public required decimal EntryPrice { get; init; }

    [JsonPropertyName("exit_price")]
    public required decimal ExitPrice { get; init; }

    [JsonPropertyName("opened_at")]
    public required DateTimeOffset OpenedAt { get; init; }

    [JsonPropertyName("closed_at")]
    public required DateTimeOffset ClosedAt { get; init; }

    [JsonPropertyName("exit_reason")]
    public required string ExitReason { get; init; }

    [JsonPropertyName("realized_pnl")]
    public required decimal RealizedPnl { get; init; }

    [JsonPropertyName("strategy_id")]
    public required string StrategyId { get; init; }
}

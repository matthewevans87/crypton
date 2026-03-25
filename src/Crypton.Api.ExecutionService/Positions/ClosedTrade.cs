using System.Text.Json.Serialization;

namespace Crypton.Api.ExecutionService.Positions;

/// <summary>A completed trade, written to the trade history when a position is fully closed.</summary>
public sealed class ClosedTrade
{
    public required string Id { get; init; }
    public required string PositionId { get; init; }
    public required string Asset { get; init; }
    public required string Direction { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal EntryPrice { get; init; }
    public required decimal ExitPrice { get; init; }
    public required DateTimeOffset OpenedAt { get; init; }
    public required DateTimeOffset ClosedAt { get; init; }
    public required string ExitReason { get; init; }
    public required decimal RealizedPnl { get; init; }
    public required string StrategyId { get; init; }
}

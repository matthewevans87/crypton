namespace Crypton.Api.ExecutionService.Models;

/// <summary>
/// A point-in-time snapshot of market data for a single asset,
/// used as the context in which DSL conditions are evaluated.
/// </summary>
public sealed class MarketSnapshot
{
    public required string Asset { get; init; }
    public required decimal Bid { get; init; }
    public required decimal Ask { get; init; }
    public decimal Mid => (Bid + Ask) / 2m;
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Computed indicator values keyed by indicator name (e.g., "RSI_14", "MACD_histogram").</summary>
    public IReadOnlyDictionary<string, decimal> Indicators { get; init; }
        = new Dictionary<string, decimal>();
}

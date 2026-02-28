namespace Crypton.Api.ExecutionService.Metrics;

/// <summary>Snapshot of runtime execution metrics.</summary>
public sealed record MetricsSnapshot
{
    public int TotalOrders { get; init; }
    public int FilledOrders { get; init; }
    public int RejectedOrders { get; init; }
}

/// <summary>Provides a current metrics snapshot.</summary>
public interface IMetricsCollector
{
    MetricsSnapshot GetSnapshot();
}

/// <summary>
/// Minimal metrics collector stub. Extend to track live counters via OrderRouter events.
/// </summary>
public sealed class MetricsCollector : IMetricsCollector
{
    public MetricsSnapshot GetSnapshot() => new();
}

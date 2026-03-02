namespace Crypton.Api.ExecutionService.Metrics;

/// <summary>Snapshot of runtime execution metrics.</summary>
public sealed record MetricsSnapshot
{
    public int TotalOrders { get; init; }
    public int FilledOrders { get; init; }
    public int RejectedOrders { get; init; }
    public int OpenOrders { get; init; }
    public int TotalPositionsOpened { get; init; }
    public int TotalPositionsClosed { get; init; }
}

/// <summary>Provides a current metrics snapshot and recording methods.</summary>
public interface IMetricsCollector
{
    MetricsSnapshot GetSnapshot();
    void RecordOrderPlaced();
    void RecordOrderFilled();
    void RecordOrderRejected();
    void RecordOrderOpened();
    void RecordOrderClosed();
    void RecordPositionOpened();
    void RecordPositionClosed();
}

/// <summary>
/// Thread-safe execution metrics collector.
/// All counters use <see cref="System.Threading.Interlocked"/> for lock-free increments.
/// </summary>
public sealed class MetricsCollector : IMetricsCollector
{
    private int _totalOrders;
    private int _filledOrders;
    private int _rejectedOrders;
    private int _openOrders;
    private int _positionsOpened;
    private int _positionsClosed;

    public void RecordOrderPlaced() => Interlocked.Increment(ref _totalOrders);
    public void RecordOrderOpened() => Interlocked.Increment(ref _openOrders);
    public void RecordOrderFilled() { Interlocked.Increment(ref _filledOrders); Interlocked.Decrement(ref _openOrders); }
    public void RecordOrderRejected() { Interlocked.Increment(ref _rejectedOrders); Interlocked.Decrement(ref _openOrders); }
    public void RecordOrderClosed() => Interlocked.Decrement(ref _openOrders);
    public void RecordPositionOpened() => Interlocked.Increment(ref _positionsOpened);
    public void RecordPositionClosed() { Interlocked.Increment(ref _positionsClosed); Interlocked.Decrement(ref _positionsOpened); }

    public MetricsSnapshot GetSnapshot() => new()
    {
        TotalOrders = Volatile.Read(ref _totalOrders),
        FilledOrders = Volatile.Read(ref _filledOrders),
        RejectedOrders = Volatile.Read(ref _rejectedOrders),
        OpenOrders = Volatile.Read(ref _openOrders),
        TotalPositionsOpened = Volatile.Read(ref _positionsOpened),
        TotalPositionsClosed = Volatile.Read(ref _positionsClosed),
    };
}

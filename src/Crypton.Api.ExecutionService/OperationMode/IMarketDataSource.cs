using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Provides real-time market data snapshots to downstream consumers.
/// The paper trading adapter subscribes to this source so that simulated fills
/// use realistic prices.
/// </summary>
public interface IMarketDataSource
{
    /// <summary>
    /// Subscribe to market snapshots for the given assets.
    /// The <paramref name="callback"/> is invoked for every tick received.
    /// </summary>
    Task SubscribeAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> callback,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// No-op market data source. Never delivers snapshots.
/// Used as the default when no real data feed is wired up.
/// </summary>
public sealed class NullMarketDataSource : IMarketDataSource
{
    public Task SubscribeAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> callback,
        CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Strategy;

/// <summary>
/// Exposes the read/write surface of StrategyService for dependency injection and testing.
/// </summary>
public interface IStrategyService
{
    StrategyDocument? ActiveStrategy { get; }
    StrategyState State { get; }
    string? ActiveStrategyId { get; }
    Task ForceReloadAsync(CancellationToken ct = default);
    /// <summary>
    /// Parses, validates, and activates a strategy document from raw JSON.
    /// Used by the REST push endpoint instead of the file watcher.
    /// Returns null on success, or a human-readable error string on failure.
    /// </summary>
    Task<string?> LoadFromJsonAsync(string json, CancellationToken ct = default);
}

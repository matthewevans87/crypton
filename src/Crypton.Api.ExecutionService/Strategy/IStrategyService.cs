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
}

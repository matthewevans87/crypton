using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Strategy.Conditions;

/// <summary>
/// A compiled, evaluable trading condition. Implementations are immutable.
/// Evaluate() is called on every market tick.
/// </summary>
public interface ICondition
{
    /// <summary>
    /// Evaluate the condition against the current market snapshots.
    /// Returns null if a required indicator value is not yet available
    /// (not enough bars). Returns true/false when the condition can be evaluated.
    /// </summary>
    bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots);

    /// <summary>Human-readable string representation for logging.</summary>
    string ToDisplayString();
}

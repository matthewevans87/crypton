using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy.Conditions;

namespace Crypton.Api.ExecutionService.Strategy;

/// <summary>
/// A StrategyDocument with all DSL conditions pre-compiled into ICondition trees.
/// Created once at strategy load time; used by the Condition Evaluation Engine.
/// </summary>
public sealed class CompiledStrategy
{
    public required StrategyDocument Document { get; init; }
    public required IReadOnlyList<CompiledPosition> Positions { get; init; }

    public static CompiledStrategy Compile(StrategyDocument doc, ConditionParser parser)
    {
        var positions = doc.Positions.Select(p => CompiledPosition.Compile(p, parser)).ToList();
        return new CompiledStrategy { Document = doc, Positions = positions };
    }
}

public sealed class CompiledPosition
{
    public required StrategyPosition Source { get; init; }
    public ICondition? EntryCondition { get; init; }
    public ICondition? InvalidationCondition { get; init; }

    public static CompiledPosition Compile(StrategyPosition p, ConditionParser parser)
    {
        return new CompiledPosition
        {
            Source = p,
            EntryCondition = string.IsNullOrWhiteSpace(p.EntryCondition)
                ? null
                : parser.Parse(p.EntryCondition),
            InvalidationCondition = string.IsNullOrWhiteSpace(p.InvalidationCondition)
                ? null
                : parser.Parse(p.InvalidationCondition)
        };
    }
}

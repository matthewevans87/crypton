using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Strategy.Conditions;

/// <summary>price(ASSET) OP value</summary>
public sealed class PriceComparisonCondition : ICondition
{
    private readonly string _asset;
    private readonly string _operator;
    private readonly decimal _value;

    public PriceComparisonCondition(string asset, string @operator, decimal value)
    {
        _asset = asset;
        _operator = @operator;
        _value = value;
    }

    public bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots)
    {
        if (!snapshots.TryGetValue(_asset, out var snap)) return null;
        return EvaluateOp(snap.Mid, _operator, _value);
    }

    public string ToDisplayString() => $"price({_asset}) {_operator} {_value}";

    internal static bool EvaluateOp(decimal left, string op, decimal right) => op switch
    {
        ">" => left > right,
        ">=" => left >= right,
        "<" => left < right,
        "<=" => left <= right,
        "==" => Math.Abs(left - right) < 1e-10m,
        "!=" => Math.Abs(left - right) >= 1e-10m,
        _ => throw new InvalidOperationException($"Unknown operator '{op}'")
    };
}

/// <summary>INDICATOR(args) OP value</summary>
public sealed class IndicatorComparisonCondition : ICondition
{
    private readonly string _indicatorKey;
    private readonly string _operator;
    private readonly decimal _value;
    private readonly string _asset;

    public IndicatorComparisonCondition(string indicatorKey, string asset, string @operator, decimal value)
    {
        _indicatorKey = indicatorKey;
        _asset = asset;
        _operator = @operator;
        _value = value;
    }

    public bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots)
    {
        if (!snapshots.TryGetValue(_asset, out var snap)) return null;
        if (!snap.Indicators.TryGetValue(_indicatorKey, out var indicatorValue)) return null;
        return PriceComparisonCondition.EvaluateOp(indicatorValue, _operator, _value);
    }

    public string ToDisplayString() => $"{_indicatorKey}({_asset}) {_operator} {_value}";
}

/// <summary>OPERAND crosses_above/crosses_below value (edge-detected)</summary>
public sealed class CrossingCondition : ICondition
{
    private readonly ICondition _underlyingCompare;
    private readonly bool _crossAbove;  // true = crosses_above, false = crosses_below
    private bool? _previousState;

    public CrossingCondition(ICondition underlyingCompare, bool crossAbove)
    {
        _underlyingCompare = underlyingCompare;
        _crossAbove = crossAbove;
    }

    public bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots)
    {
        var current = _underlyingCompare.Evaluate(snapshots);
        if (current is null) { _previousState = null; return null; }

        var prev = _previousState;
        _previousState = current.Value;

        if (prev is null) return false;  // Can't detect a crossing on the first tick

        return _crossAbove
            ? !prev.Value && current.Value   // was below, now above
            : prev.Value && !current.Value;   // was above, now below
    }

    public void ResetState() => _previousState = null;

    public string ToDisplayString()
    {
        var verb = _crossAbove ? "crosses_above" : "crosses_below";
        return $"{_underlyingCompare.ToDisplayString().Split(' ')[0]} {verb} ...";
    }
}

/// <summary>AND(condition1, condition2, ...)</summary>
public sealed class AndCondition : ICondition
{
    private readonly IReadOnlyList<ICondition> _conditions;
    public AndCondition(IReadOnlyList<ICondition> conditions) => _conditions = conditions;

    public bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots)
    {
        var result = true;
        foreach (var c in _conditions)
        {
            var val = c.Evaluate(snapshots);
            if (val is null) return null;
            if (!val.Value) result = false;  // still continue to detect other nulls
        }
        return result;
    }

    public string ToDisplayString() => $"AND({string.Join(", ", _conditions.Select(c => c.ToDisplayString()))})";
}

/// <summary>OR(condition1, condition2, ...)</summary>
public sealed class OrCondition : ICondition
{
    private readonly IReadOnlyList<ICondition> _conditions;
    public OrCondition(IReadOnlyList<ICondition> conditions) => _conditions = conditions;

    public bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots)
    {
        bool hasNull = false;
        foreach (var c in _conditions)
        {
            var val = c.Evaluate(snapshots);
            if (val is null) { hasNull = true; continue; }
            if (val.Value) return true;
        }
        return hasNull ? null : false;
    }

    public string ToDisplayString() => $"OR({string.Join(", ", _conditions.Select(c => c.ToDisplayString()))})";
}

/// <summary>NOT(condition)</summary>
public sealed class NotCondition : ICondition
{
    private readonly ICondition _inner;
    public NotCondition(ICondition inner) => _inner = inner;

    public bool? Evaluate(IReadOnlyDictionary<string, MarketSnapshot> snapshots)
    {
        var val = _inner.Evaluate(snapshots);
        return val is null ? null : !val.Value;
    }

    public string ToDisplayString() => $"NOT({_inner.ToDisplayString()})";
}

using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Strategy;

/// <summary>
/// Validates a StrategyDocument against the schema rules defined in ES-SM-002.
/// Returns a list of validation errors — an empty list means the strategy is valid.
/// All validation is synchronous and completes before the strategy pointer is updated.
/// </summary>
public sealed class StrategyValidator
{
    private static readonly HashSet<string> ValidPostures =
        ["aggressive", "moderate", "defensive", "flat", "exit_all"];

    private static readonly HashSet<string> ValidEntryTypes =
        ["market", "limit", "conditional"];

    private static readonly HashSet<string> ValidDirections =
        ["long", "short"];

    private static readonly HashSet<string> ValidModes =
        ["paper", "live"];

    public IReadOnlyList<StrategyValidationError> Validate(StrategyDocument strategy)
    {
        var errors = new List<StrategyValidationError>();
        ValidateTopLevel(strategy, errors);
        ValidatePortfolioRisk(strategy.PortfolioRisk, errors);
        for (var i = 0; i < strategy.Positions.Count; i++)
            ValidatePosition(strategy.Positions[i], i, errors);
        return errors;
    }

    private static void ValidateTopLevel(StrategyDocument s, List<StrategyValidationError> errors)
    {
        if (!ValidModes.Contains(s.Mode))
            errors.Add(new("mode", $"Invalid mode '{s.Mode}'. Must be 'paper' or 'live'."));

        if (!ValidPostures.Contains(s.Posture))
            errors.Add(new("posture", $"Invalid posture '{s.Posture}'."));

        if (s.ValidityWindow <= DateTimeOffset.UtcNow)
            errors.Add(new("validity_window", "validity_window is in the past; strategy is already expired on load."));
    }

    private static void ValidatePortfolioRisk(PortfolioRisk r, List<StrategyValidationError> errors)
    {
        if (r.MaxDrawdownPct is <= 0 or > 1)
            errors.Add(new("portfolio_risk.max_drawdown_pct", "Must be in range (0, 1]."));

        if (r.MaxTotalExposurePct is < 0 or > 1)
            errors.Add(new("portfolio_risk.max_total_exposure_pct", "Must be in range [0, 1]."));

        if (r.MaxPerPositionPct is <= 0 or > 1)
            errors.Add(new("portfolio_risk.max_per_position_pct", "Must be in range (0, 1]."));

        if (r.DailyLossLimitUsd < 0)
            errors.Add(new("portfolio_risk.daily_loss_limit_usd", "Must be >= 0."));
    }

    private static void ValidatePosition(StrategyPosition p, int index, List<StrategyValidationError> errors)
    {
        var prefix = $"positions[{index}]";

        if (string.IsNullOrWhiteSpace(p.Id))
            errors.Add(new($"{prefix}.id", "Position id is required."));

        if (string.IsNullOrWhiteSpace(p.Asset))
            errors.Add(new($"{prefix}.asset", "Asset is required."));

        if (!ValidDirections.Contains(p.Direction))
            errors.Add(new($"{prefix}.direction", $"Invalid direction '{p.Direction}'."));

        if (p.AllocationPct is <= 0 or > 1)
            errors.Add(new($"{prefix}.allocation_pct", "Must be in range (0, 1]."));

        if (!ValidEntryTypes.Contains(p.EntryType))
            errors.Add(new($"{prefix}.entry_type", $"Invalid entry_type '{p.EntryType}'."));

        if (p.EntryType == "conditional" && string.IsNullOrWhiteSpace(p.EntryCondition))
            errors.Add(new($"{prefix}.entry_condition", "entry_condition is required for conditional entry type."));

        if (p.EntryType == "limit" && p.EntryLimitPrice is null)
            errors.Add(new($"{prefix}.entry_limit_price", "entry_limit_price is required for limit entry type."));

        ValidateTakeProfitTargets(p.TakeProfitTargets, prefix, errors);
        ValidateStopLoss(p.StopLoss, prefix, errors);
    }

    private static void ValidateTakeProfitTargets(
        IReadOnlyList<TakeProfitTarget> targets, string prefix, List<StrategyValidationError> errors)
    {
        var totalClosePct = 0m;
        for (var i = 0; i < targets.Count; i++)
        {
            var t = targets[i];
            if (t.Price <= 0)
                errors.Add(new($"{prefix}.take_profit_targets[{i}].price", "Price must be > 0."));
            if (t.ClosePct is <= 0 or > 1)
                errors.Add(new($"{prefix}.take_profit_targets[{i}].close_pct", "close_pct must be in range (0, 1]."));
            totalClosePct += t.ClosePct;
        }

        if (targets.Count > 0 && totalClosePct > 1.0001m)
            errors.Add(new($"{prefix}.take_profit_targets", $"Sum of close_pct ({totalClosePct:P}) exceeds 100%."));
    }

    private static void ValidateStopLoss(StopLoss? sl, string prefix, List<StrategyValidationError> errors)
    {
        if (sl is null) return;

        if (sl.Type is not ("hard" or "trailing"))
            errors.Add(new($"{prefix}.stop_loss.type", $"Invalid stop_loss type '{sl.Type}'. Must be 'hard' or 'trailing'."));

        if (sl.Type == "hard" && sl.Price is null)
            errors.Add(new($"{prefix}.stop_loss.price", "price is required for hard stop-loss."));

        if (sl.Type == "trailing" && sl.TrailPct is null)
            errors.Add(new($"{prefix}.stop_loss.trail_pct", "trail_pct is required for trailing stop-loss."));
    }
}

public sealed record StrategyValidationError(string Field, string Message);

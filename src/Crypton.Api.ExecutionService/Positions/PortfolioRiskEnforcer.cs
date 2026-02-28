using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Positions;

/// <summary>
/// Evaluates portfolio-level risk constraints from the active strategy.
/// Called before every entry dispatch and on every evaluation tick.
/// </summary>
public sealed class PortfolioRiskEnforcer
{
    private readonly IEventLogger _eventLogger;
    private decimal _peakEquity;
    private decimal _dailyStartEquity;
    private DateOnly _dailyStartDate;

    public bool EntriesSuspended { get; private set; }
    public bool SafeModeTriggered { get; private set; }
    public string? SafeModeTriggerReason { get; private set; }

    public PortfolioRiskEnforcer(IEventLogger eventLogger)
    {
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// Evaluate all portfolio risk limits. Returns true if entries are still permitted.
    /// Sets SafeModeTriggered = true if a drawdown threshold is breached.
    /// </summary>
    public async Task<bool> EvaluateAsync(
        PortfolioRisk limits,
        IReadOnlyList<OpenPosition> positions,
        decimal accountEquityUsd,
        string currentMode,
        CancellationToken token = default)
    {
        if (_peakEquity == 0) _peakEquity = accountEquityUsd;
        if (_peakEquity < accountEquityUsd) _peakEquity = accountEquityUsd;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_dailyStartDate != today) { _dailyStartDate = today; _dailyStartEquity = accountEquityUsd; }

        var totalNotional = positions.Sum(p => p.Quantity * p.CurrentPrice);
        var exposurePct = accountEquityUsd > 0 ? totalNotional / accountEquityUsd : 0;
        var drawdownPct = _peakEquity > 0 ? (_peakEquity - accountEquityUsd) / _peakEquity : 0;
        var dailyLossUsd = _dailyStartEquity - accountEquityUsd;

        // Max drawdown — triggers safe mode
        if (drawdownPct >= limits.MaxDrawdownPct && !SafeModeTriggered)
        {
            SafeModeTriggered = true;
            SafeModeTriggerReason = $"max_drawdown_breached ({drawdownPct:P2} >= {limits.MaxDrawdownPct:P2})";
            await _eventLogger.LogAsync(EventTypes.RiskLimitBreached, currentMode, new Dictionary<string, object?>
            {
                ["limit"] = "max_drawdown_pct",
                ["value"] = (double)drawdownPct,
                ["threshold"] = (double)limits.MaxDrawdownPct,
                ["action"] = "safe_mode"
            }, token);
        }

        // Max exposure — suspends entries
        if (exposurePct >= limits.MaxTotalExposurePct && !EntriesSuspended)
        {
            EntriesSuspended = true;
            await _eventLogger.LogAsync(EventTypes.RiskLimitBreached, currentMode, new Dictionary<string, object?>
            {
                ["limit"] = "max_total_exposure_pct",
                ["value"] = (double)exposurePct,
                ["threshold"] = (double)limits.MaxTotalExposurePct,
                ["action"] = "suspend_entries"
            }, token);
        }
        else if (exposurePct < limits.MaxTotalExposurePct * 0.95m && EntriesSuspended && !SafeModeTriggered)
        {
            // Hysteresis: resume when 5% below cap
            EntriesSuspended = false;
        }

        // Daily loss limit — suspends entries for rest of UTC day
        if (dailyLossUsd >= limits.DailyLossLimitUsd && !EntriesSuspended)
        {
            EntriesSuspended = true;
            await _eventLogger.LogAsync(EventTypes.RiskLimitBreached, currentMode, new Dictionary<string, object?>
            {
                ["limit"] = "daily_loss_limit_usd",
                ["value"] = (double)dailyLossUsd,
                ["threshold"] = (double)limits.DailyLossLimitUsd,
                ["action"] = "suspend_entries_until_utc_midnight"
            }, token);
        }

        return !EntriesSuspended;
    }

    /// <summary>Reset all state for a new strategy load or new day.</summary>
    public void Reset(decimal currentEquity)
    {
        _peakEquity = currentEquity;
        _dailyStartEquity = currentEquity;
        _dailyStartDate = DateOnly.FromDateTime(DateTime.UtcNow);
        EntriesSuspended = false;
        SafeModeTriggered = false;
        SafeModeTriggerReason = null;
    }
}

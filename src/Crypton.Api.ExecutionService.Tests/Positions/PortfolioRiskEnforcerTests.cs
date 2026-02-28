using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Positions;
using FluentAssertions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Positions;

public sealed class PortfolioRiskEnforcerTests
{
    private readonly InMemoryEventLogger _eventLogger = new();

    private PortfolioRiskEnforcer CreateEnforcer() => new(_eventLogger);

    private static PortfolioRisk DefaultLimits(
        decimal maxDrawdownPct = 0.10m,
        decimal dailyLossUsd = 500m,
        decimal maxTotalExposurePct = 0.80m,
        decimal maxPerPositionPct = 0.20m) => new()
        {
            MaxDrawdownPct = maxDrawdownPct,
            DailyLossLimitUsd = dailyLossUsd,
            MaxTotalExposurePct = maxTotalExposurePct,
            MaxPerPositionPct = maxPerPositionPct
        };

    private static OpenPosition MakePosition(string asset, decimal quantity, decimal currentPrice) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            StrategyPositionId = "sp1",
            StrategyId = "strat1",
            Asset = asset,
            Direction = "long",
            Quantity = quantity,
            AverageEntryPrice = currentPrice,
            OpenedAt = DateTimeOffset.UtcNow,
            CurrentPrice = currentPrice
        };

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BelowAllLimits_ReturnsTrue_EntriesPermitted()
    {
        var enforcer = CreateEnforcer();
        var limits = DefaultLimits();
        var positions = new List<OpenPosition> { MakePosition("BTC/USD", 0.01m, 50_000m) }; // $500 notional

        var result = await enforcer.EvaluateAsync(limits, positions, 10_000m, "paper");

        result.Should().BeTrue();
        enforcer.EntriesSuspended.Should().BeFalse();
        enforcer.SafeModeTriggered.Should().BeFalse();
    }

    [Fact]
    public async Task ExposureAtMax_SuspendsEntries_AndLogsEvent()
    {
        var enforcer = CreateEnforcer();
        var limits = DefaultLimits(maxTotalExposurePct: 0.80m);
        // 80% of $10000 = $8000 notional → exactly at limit
        var positions = new List<OpenPosition> { MakePosition("BTC/USD", 0.16m, 50_000m) }; // $8000 notional

        var result = await enforcer.EvaluateAsync(limits, positions, 10_000m, "paper");

        result.Should().BeFalse();
        enforcer.EntriesSuspended.Should().BeTrue();
        _eventLogger.Events.Should().ContainSingle(e =>
            e.EventType == EventTypes.RiskLimitBreached &&
            e.Data!["limit"]!.ToString() == "max_total_exposure_pct");
    }

    [Fact]
    public async Task Hysteresis_ExposureDrops6PctBelowCap_ReEnablesEntries()
    {
        var enforcer = CreateEnforcer();
        var limits = DefaultLimits(maxTotalExposurePct: 0.80m);

        // First evaluation: exactly at limit → suspend
        var highPositions = new List<OpenPosition> { MakePosition("BTC/USD", 0.16m, 50_000m) }; // $8000
        await enforcer.EvaluateAsync(limits, highPositions, 10_000m, "paper");
        enforcer.EntriesSuspended.Should().BeTrue();

        // Second evaluation: exposure drops to 73% (< 95% of 80% = 76%) → resume
        var lowPositions = new List<OpenPosition> { MakePosition("BTC/USD", 0.146m, 50_000m) }; // $7300
        var result = await enforcer.EvaluateAsync(limits, lowPositions, 10_000m, "paper");

        result.Should().BeTrue();
        enforcer.EntriesSuspended.Should().BeFalse();
    }

    [Fact]
    public async Task DrawdownBreach_TriggersSafeMode_LogsWithActionSafeMode()
    {
        var enforcer = CreateEnforcer();
        var limits = DefaultLimits(maxDrawdownPct: 0.10m);

        // Peak equity = $10000, current = $8900 → 11% drawdown
        await enforcer.EvaluateAsync(limits, [], 10_000m, "paper");  // sets peak
        var result = await enforcer.EvaluateAsync(limits, [], 8_900m, "paper");

        result.Should().BeFalse();
        enforcer.SafeModeTriggered.Should().BeTrue();
        enforcer.SafeModeTriggerReason.Should().Contain("max_drawdown_breached");
        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.RiskLimitBreached &&
            e.Data!["action"]!.ToString() == "safe_mode");
    }

    [Fact]
    public async Task DailyLossLimit_SuspendsEntries_LogsCorrectly()
    {
        var enforcer = CreateEnforcer();
        var limits = DefaultLimits(dailyLossUsd: 500m);

        // Start of day: sets daily baseline at $10000
        await enforcer.EvaluateAsync(limits, [], 10_000m, "paper");
        // Now equity drops $501
        var result = await enforcer.EvaluateAsync(limits, [], 9_499m, "paper");

        result.Should().BeFalse();
        enforcer.EntriesSuspended.Should().BeTrue();
        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.RiskLimitBreached &&
            e.Data!["limit"]!.ToString() == "daily_loss_limit_usd" &&
            e.Data["action"]!.ToString() == "suspend_entries_until_utc_midnight");
    }

    [Fact]
    public async Task MultipleLimitsBreached_AllLogged_SafeModeTakesPrecedence()
    {
        var enforcer = CreateEnforcer();
        // Low thresholds so both trigger simultaneously
        var limits = DefaultLimits(maxDrawdownPct: 0.05m, maxTotalExposurePct: 0.50m);

        // Set peak equity
        await enforcer.EvaluateAsync(limits, [], 10_000m, "paper");

        // 60% exposure + 6% drawdown → both triggered
        var positions = new List<OpenPosition> { MakePosition("BTC/USD", 0.12m, 50_000m) }; // $6000 = 60%
        var result = await enforcer.EvaluateAsync(limits, positions, 9_400m, "paper");

        result.Should().BeFalse();
        enforcer.SafeModeTriggered.Should().BeTrue();
        enforcer.EntriesSuspended.Should().BeTrue();

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.RiskLimitBreached &&
            e.Data!["limit"]!.ToString() == "max_drawdown_pct");
        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.RiskLimitBreached &&
            e.Data!["limit"]!.ToString() == "max_total_exposure_pct");
    }

    [Fact]
    public async Task Reset_ClearsAllState_RecalculatesFromNewEquity()
    {
        var enforcer = CreateEnforcer();
        var limits = DefaultLimits(maxDrawdownPct: 0.05m);

        // Trigger safe mode
        await enforcer.EvaluateAsync(limits, [], 10_000m, "paper");
        await enforcer.EvaluateAsync(limits, [], 9_400m, "paper");
        enforcer.SafeModeTriggered.Should().BeTrue();

        // Reset with new baseline
        enforcer.Reset(8_000m);

        enforcer.SafeModeTriggered.Should().BeFalse();
        enforcer.EntriesSuspended.Should().BeFalse();
        enforcer.SafeModeTriggerReason.Should().BeNull();

        // Should now evaluate cleanly from $8000 baseline
        var result = await enforcer.EvaluateAsync(limits, [], 8_000m, "paper");
        result.Should().BeTrue();
    }
}

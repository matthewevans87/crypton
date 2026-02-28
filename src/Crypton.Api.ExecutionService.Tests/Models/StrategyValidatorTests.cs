using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy;
using FluentAssertions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Models;

public sealed class StrategyValidatorTests
{
    private readonly StrategyValidator _sut = new();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static StrategyDocument ValidStrategy(
        string mode = "paper",
        string posture = "moderate",
        DateTimeOffset? validityWindow = null,
        IReadOnlyList<StrategyPosition>? positions = null,
        PortfolioRisk? risk = null) => new()
        {
            Mode = mode,
            Posture = posture,
            ValidityWindow = validityWindow ?? DateTimeOffset.UtcNow.AddHours(1),
            PortfolioRisk = risk ?? ValidRisk(),
            Positions = positions ?? [ValidPosition()]
        };

    private static PortfolioRisk ValidRisk(
        decimal maxDrawdown = 0.1m,
        decimal dailyLoss = 500m,
        decimal maxExposure = 0.8m,
        decimal maxPerPosition = 0.2m) => new()
        {
            MaxDrawdownPct = maxDrawdown,
            DailyLossLimitUsd = dailyLoss,
            MaxTotalExposurePct = maxExposure,
            MaxPerPositionPct = maxPerPosition
        };

    private static StrategyPosition ValidPosition(
        string id = "pos-1",
        string asset = "BTC/USD",
        string direction = "long",
        decimal allocationPct = 0.1m,
        string entryType = "market",
        string? entryCondition = null,
        decimal? entryLimitPrice = null,
        IReadOnlyList<TakeProfitTarget>? tpTargets = null,
        StopLoss? stopLoss = null) => new()
        {
            Id = id,
            Asset = asset,
            Direction = direction,
            AllocationPct = allocationPct,
            EntryType = entryType,
            EntryCondition = entryCondition,
            EntryLimitPrice = entryLimitPrice,
            TakeProfitTargets = tpTargets ?? [],
            StopLoss = stopLoss
        };

    // ── happy path ────────────────────────────────────────────────────────────

    [Fact]
    public void ValidStrategy_ReturnsNoErrors()
    {
        var errors = _sut.Validate(ValidStrategy());
        errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("paper")]
    [InlineData("live")]
    public void ValidMode_ReturnsNoModeError(string mode)
    {
        var errors = _sut.Validate(ValidStrategy(mode: mode));
        errors.Should().NotContain(e => e.Field == "mode");
    }

    [Theory]
    [InlineData("aggressive")]
    [InlineData("moderate")]
    [InlineData("defensive")]
    [InlineData("flat")]
    [InlineData("exit_all")]
    public void ValidPosture_ReturnsNoPostureError(string posture)
    {
        var strategy = ValidStrategy(posture: posture, positions: []);
        var errors = _sut.Validate(strategy);
        errors.Should().NotContain(e => e.Field == "posture");
    }

    // ── mode ──────────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidMode_ProducesErrorOnModeField()
    {
        var errors = _sut.Validate(ValidStrategy(mode: "sim"));
        errors.Should().ContainSingle(e => e.Field == "mode");
    }

    [Fact]
    public void EmptyMode_ProducesErrorOnModeField()
    {
        var errors = _sut.Validate(ValidStrategy(mode: ""));
        errors.Should().ContainSingle(e => e.Field == "mode");
    }

    // ── posture ───────────────────────────────────────────────────────────────

    [Fact]
    public void InvalidPosture_ProducesError()
    {
        var errors = _sut.Validate(ValidStrategy(posture: "yolo"));
        errors.Should().ContainSingle(e => e.Field == "posture");
    }

    // ── validity_window ───────────────────────────────────────────────────────

    [Fact]
    public void PastValidityWindow_ProducesError()
    {
        var errors = _sut.Validate(ValidStrategy(validityWindow: DateTimeOffset.UtcNow.AddMinutes(-1)));
        errors.Should().ContainSingle(e => e.Field == "validity_window");
    }

    [Fact]
    public void FutureValidityWindow_NoError()
    {
        var errors = _sut.Validate(ValidStrategy(validityWindow: DateTimeOffset.UtcNow.AddDays(1)));
        errors.Should().NotContain(e => e.Field == "validity_window");
    }

    // ── portfolio risk ────────────────────────────────────────────────────────

    [Fact]
    public void MaxDrawdownPct_Zero_Rejected()
    {
        var errors = _sut.Validate(ValidStrategy(risk: ValidRisk(maxDrawdown: 0m)));
        errors.Should().ContainSingle(e => e.Field == "portfolio_risk.max_drawdown_pct");
    }

    [Fact]
    public void MaxDrawdownPct_ExceedsOne_Rejected()
    {
        var errors = _sut.Validate(ValidStrategy(risk: ValidRisk(maxDrawdown: 1.5m)));
        errors.Should().ContainSingle(e => e.Field == "portfolio_risk.max_drawdown_pct");
    }

    [Fact]
    public void MaxDrawdownPct_ExactlyOne_Accepted()
    {
        var errors = _sut.Validate(ValidStrategy(risk: ValidRisk(maxDrawdown: 1m)));
        errors.Should().NotContain(e => e.Field == "portfolio_risk.max_drawdown_pct");
    }

    [Fact]
    public void MaxDrawdownPct_PointOne_Accepted()
    {
        var errors = _sut.Validate(ValidStrategy(risk: ValidRisk(maxDrawdown: 0.1m)));
        errors.Should().NotContain(e => e.Field == "portfolio_risk.max_drawdown_pct");
    }

    [Fact]
    public void DailyLossLimitUsd_Negative_Rejected()
    {
        var errors = _sut.Validate(ValidStrategy(risk: ValidRisk(dailyLoss: -1m)));
        errors.Should().ContainSingle(e => e.Field == "portfolio_risk.daily_loss_limit_usd");
    }

    [Fact]
    public void DailyLossLimitUsd_Zero_Accepted()
    {
        var errors = _sut.Validate(ValidStrategy(risk: ValidRisk(dailyLoss: 0m)));
        errors.Should().NotContain(e => e.Field == "portfolio_risk.daily_loss_limit_usd");
    }

    // ── positions ─────────────────────────────────────────────────────────────

    [Fact]
    public void Position_UnknownEntryType_Rejected()
    {
        var pos = ValidPosition(entryType: "oco");
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].entry_type");
    }

    [Fact]
    public void Position_ConditionalEntry_MissingCondition_Rejected()
    {
        var pos = ValidPosition(entryType: "conditional", entryCondition: null);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].entry_condition");
    }

    [Fact]
    public void Position_ConditionalEntry_WithCondition_Accepted()
    {
        var pos = ValidPosition(entryType: "conditional", entryCondition: "RSI_14 < 30");
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().NotContain(e => e.Field == "positions[0].entry_condition");
    }

    [Fact]
    public void Position_LimitEntry_MissingLimitPrice_Rejected()
    {
        var pos = ValidPosition(entryType: "limit", entryLimitPrice: null);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].entry_limit_price");
    }

    [Fact]
    public void Position_LimitEntry_WithLimitPrice_Accepted()
    {
        var pos = ValidPosition(entryType: "limit", entryLimitPrice: 50000m);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().NotContain(e => e.Field == "positions[0].entry_limit_price");
    }

    [Fact]
    public void Position_AllocationPct_Zero_Rejected()
    {
        var pos = ValidPosition(allocationPct: 0m);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].allocation_pct");
    }

    [Fact]
    public void Position_AllocationPct_GreaterThanOne_Rejected()
    {
        var pos = ValidPosition(allocationPct: 1.1m);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].allocation_pct");
    }

    [Fact]
    public void Position_AllocationPct_ExactlyOne_Accepted()
    {
        var pos = ValidPosition(allocationPct: 1.0m);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().NotContain(e => e.Field == "positions[0].allocation_pct");
    }

    [Fact]
    public void Position_InvalidDirection_Rejected()
    {
        var pos = ValidPosition(direction: "sideways");
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].direction");
    }

    [Fact]
    public void Position_EmptyId_Rejected()
    {
        var pos = ValidPosition(id: "");
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].id");
    }

    [Fact]
    public void Position_EmptyAsset_Rejected()
    {
        var pos = ValidPosition(asset: "");
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].asset");
    }

    // ── take-profit targets ───────────────────────────────────────────────────

    [Fact]
    public void TakeProfitTargets_SumExceedsOne_Rejected()
    {
        var pos = ValidPosition(tpTargets:
        [
            new TakeProfitTarget { Price = 60000m, ClosePct = 0.6m },
            new TakeProfitTarget { Price = 65000m, ClosePct = 0.6m }
        ]);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].take_profit_targets");
    }

    [Fact]
    public void TakeProfitTargets_SumExactlyOne_Accepted()
    {
        var pos = ValidPosition(tpTargets:
        [
            new TakeProfitTarget { Price = 60000m, ClosePct = 0.5m },
            new TakeProfitTarget { Price = 65000m, ClosePct = 0.5m }
        ]);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().NotContain(e => e.Field == "positions[0].take_profit_targets");
    }

    [Fact]
    public void TakeProfitTarget_ZeroPrice_Rejected()
    {
        var pos = ValidPosition(tpTargets:
        [
            new TakeProfitTarget { Price = 0m, ClosePct = 0.5m }
        ]);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].take_profit_targets[0].price");
    }

    [Fact]
    public void TakeProfitTarget_ZeroClosePct_Rejected()
    {
        var pos = ValidPosition(tpTargets:
        [
            new TakeProfitTarget { Price = 60000m, ClosePct = 0m }
        ]);
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].take_profit_targets[0].close_pct");
    }

    // ── stop-loss ─────────────────────────────────────────────────────────────

    [Fact]
    public void HardStopLoss_MissingPrice_Rejected()
    {
        var pos = ValidPosition(stopLoss: new StopLoss { Type = "hard", Price = null });
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].stop_loss.price");
    }

    [Fact]
    public void HardStopLoss_WithPrice_Accepted()
    {
        var pos = ValidPosition(stopLoss: new StopLoss { Type = "hard", Price = 40000m });
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().NotContain(e => e.Field == "positions[0].stop_loss.price");
    }

    [Fact]
    public void TrailingStopLoss_MissingTrailPct_Rejected()
    {
        var pos = ValidPosition(stopLoss: new StopLoss { Type = "trailing", TrailPct = null });
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].stop_loss.trail_pct");
    }

    [Fact]
    public void TrailingStopLoss_WithTrailPct_Accepted()
    {
        var pos = ValidPosition(stopLoss: new StopLoss { Type = "trailing", TrailPct = 0.02m });
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().NotContain(e => e.Field == "positions[0].stop_loss.trail_pct");
    }

    [Fact]
    public void StopLoss_InvalidType_Rejected()
    {
        var pos = ValidPosition(stopLoss: new StopLoss { Type = "soft", Price = 40000m });
        var errors = _sut.Validate(ValidStrategy(positions: [pos]));
        errors.Should().ContainSingle(e => e.Field == "positions[0].stop_loss.type");
    }

    // ── flat / exit_all with empty positions ──────────────────────────────────

    [Fact]
    public void FlatPosture_EmptyPositions_IsValid()
    {
        var strategy = ValidStrategy(posture: "flat", positions: []);
        var errors = _sut.Validate(strategy);
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ExitAll_EmptyPositions_IsValid()
    {
        var strategy = ValidStrategy(posture: "exit_all", positions: []);
        var errors = _sut.Validate(strategy);
        errors.Should().BeEmpty();
    }
}

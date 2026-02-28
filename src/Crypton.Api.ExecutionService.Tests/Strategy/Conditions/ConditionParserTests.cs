using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy.Conditions;
using FluentAssertions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Strategy.Conditions;

public sealed class ConditionParserTests
{
    private readonly ConditionParser _sut = new();

    // ── helpers ──────────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, MarketSnapshot> Snap(
        string asset, decimal bid, decimal ask,
        Dictionary<string, decimal>? indicators = null) =>
        new Dictionary<string, MarketSnapshot>
        {
            [asset] = new MarketSnapshot
            {
                Asset = asset,
                Bid = bid,
                Ask = ask,
                Timestamp = DateTimeOffset.UtcNow,
                Indicators = indicators ?? new Dictionary<string, decimal>()
            }
        };

    // ── price comparisons ─────────────────────────────────────────────────────

    [Fact]
    public void Price_GreaterThan_TrueWhenAbove()
    {
        var cond = _sut.Parse("price(BTC/USD) > 45000");
        var snapshots = Snap("BTC/USD", 46000m, 46001m);
        cond.Evaluate(snapshots).Should().BeTrue();
    }

    [Fact]
    public void Price_GreaterThan_FalseWhenBelow()
    {
        var cond = _sut.Parse("price(BTC/USD) > 45000");
        var snapshots = Snap("BTC/USD", 43999m, 44001m);  // mid = 44000
        cond.Evaluate(snapshots).Should().BeFalse();
    }

    [Fact]
    public void Price_Equality_WithEpsilonTolerance()
    {
        var cond = _sut.Parse("price(BTC/USD) == 45000");
        // mid = (44999.5 + 45000.5) / 2 = 45000
        var snapshots = Snap("BTC/USD", 44999.5m, 45000.5m);
        cond.Evaluate(snapshots).Should().BeTrue();
    }

    [Fact]
    public void Price_ReturnsNull_WhenAssetNotInSnapshot()
    {
        var cond = _sut.Parse("price(BTC/USD) > 45000");
        cond.Evaluate(new Dictionary<string, MarketSnapshot>()).Should().BeNull();
    }

    // ── indicator comparisons ─────────────────────────────────────────────────

    [Fact]
    public void Indicator_RSI_EvaluatesCorrectly_WhenPresent()
    {
        var cond = _sut.Parse("RSI(14, BTC/USD) < 30");
        var snapshots = Snap("BTC/USD", 45000m, 45001m, new Dictionary<string, decimal>
        {
            ["RSI_14"] = 25m
        });
        cond.Evaluate(snapshots).Should().BeTrue();
    }

    [Fact]
    public void Indicator_RSI_ReturnsFalse_WhenAboveThreshold()
    {
        var cond = _sut.Parse("RSI(14, BTC/USD) < 30");
        var snapshots = Snap("BTC/USD", 45000m, 45001m, new Dictionary<string, decimal>
        {
            ["RSI_14"] = 55m
        });
        cond.Evaluate(snapshots).Should().BeFalse();
    }

    [Fact]
    public void Indicator_ReturnsNull_WhenIndicatorNotInSnapshot()
    {
        var cond = _sut.Parse("RSI(14, BTC/USD) < 30");
        var snapshots = Snap("BTC/USD", 45000m, 45001m);  // no indicators
        cond.Evaluate(snapshots).Should().BeNull();
    }

    // ── indicator key construction ────────────────────────────────────────────

    [Fact]
    public void IndicatorKey_ForRSI14_IsRSI_14()
    {
        var key = ConditionParser.BuildIndicatorKey("RSI", ["14"]);
        key.Should().Be("RSI_14");
    }

    [Fact]
    public void IndicatorKey_NoParams_IsUpperCaseName()
    {
        var key = ConditionParser.BuildIndicatorKey("volume", []);
        key.Should().Be("VOLUME");
    }

    // ── crossing conditions ───────────────────────────────────────────────────

    [Fact]
    public void Crossing_ReturnsNull_WhenIndicatorMissing()
    {
        var cond = _sut.Parse("MACD_histogram(BTC/USD) crosses_above 0");
        var snapshots = Snap("BTC/USD", 45000m, 45001m);
        cond.Evaluate(snapshots).Should().BeNull();
    }

    [Fact]
    public void Crossing_ReturnsFalse_OnFirstTick()
    {
        var cond = _sut.Parse("MACD_histogram(BTC/USD) crosses_above 0");
        var snapshots = Snap("BTC/USD", 45000m, 45001m, new Dictionary<string, decimal>
        {
            ["MACD_HISTOGRAM"] = 0.5m   // above 0 on first tick
        });
        cond.Evaluate(snapshots).Should().BeFalse();
    }

    [Fact]
    public void Crossing_ReturnsTrueOnly_WhenTransitionFromBelowToAbove()
    {
        var cond = _sut.Parse("MACD_histogram(BTC/USD) crosses_above 0");

        // Tick 1: below 0 → sets previous state, returns false
        var tick1 = Snap("BTC/USD", 45000m, 45001m, new Dictionary<string, decimal>
        {
            ["MACD_HISTOGRAM"] = -0.5m
        });
        cond.Evaluate(tick1).Should().BeFalse();

        // Tick 2: above 0 → crossing detected → true
        var tick2 = Snap("BTC/USD", 45000m, 45001m, new Dictionary<string, decimal>
        {
            ["MACD_HISTOGRAM"] = 0.5m
        });
        cond.Evaluate(tick2).Should().BeTrue();

        // Tick 3: still above 0 → no crossing → false
        var tick3 = Snap("BTC/USD", 45000m, 45001m, new Dictionary<string, decimal>
        {
            ["MACD_HISTOGRAM"] = 1.0m
        });
        cond.Evaluate(tick3).Should().BeFalse();
    }

    // ── composite conditions ──────────────────────────────────────────────────

    [Fact]
    public void And_BothTrue_ReturnsTrue()
    {
        var cond = _sut.Parse("AND(price(BTC/USD) > 40000, RSI(14, BTC/USD) < 35)");
        var snapshots = Snap("BTC/USD", 41000m, 41001m, new Dictionary<string, decimal>
        {
            ["RSI_14"] = 30m
        });
        cond.Evaluate(snapshots).Should().BeTrue();
    }

    [Fact]
    public void And_OneFalse_ReturnsFalse()
    {
        var cond = _sut.Parse("AND(price(BTC/USD) > 40000, RSI(14, BTC/USD) < 35)");
        var snapshots = Snap("BTC/USD", 41000m, 41001m, new Dictionary<string, decimal>
        {
            ["RSI_14"] = 50m   // RSI not below 35 → false
        });
        cond.Evaluate(snapshots).Should().BeFalse();
    }

    [Fact]
    public void Or_OneTrue_ReturnsTrue()
    {
        var cond = _sut.Parse("OR(price(BTC/USD) > 40000, RSI(14, BTC/USD) < 35)");
        var snapshots = Snap("BTC/USD", 41000m, 41001m, new Dictionary<string, decimal>
        {
            ["RSI_14"] = 50m   // RSI false, price true
        });
        cond.Evaluate(snapshots).Should().BeTrue();
    }

    [Fact]
    public void Or_BothFalse_ReturnsFalse()
    {
        var cond = _sut.Parse("OR(price(BTC/USD) > 40000, RSI(14, BTC/USD) < 35)");
        var snapshots = Snap("BTC/USD", 39000m, 39001m, new Dictionary<string, decimal>
        {
            ["RSI_14"] = 50m
        });
        cond.Evaluate(snapshots).Should().BeFalse();
    }

    [Fact]
    public void Not_InvertsCorrectly_TrueToFalse()
    {
        var cond = _sut.Parse("NOT(price(ETH/USD) > 3000)");
        var snapshots = Snap("ETH/USD", 3100m, 3101m);
        cond.Evaluate(snapshots).Should().BeFalse();
    }

    [Fact]
    public void Not_InvertsCorrectly_FalseToTrue()
    {
        var cond = _sut.Parse("NOT(price(ETH/USD) > 3000)");
        var snapshots = Snap("ETH/USD", 2900m, 2901m);
        cond.Evaluate(snapshots).Should().BeTrue();
    }

    [Fact]
    public void Not_ReturnsNull_WhenInnerIsNull()
    {
        var cond = _sut.Parse("NOT(price(ETH/USD) > 3000)");
        cond.Evaluate(new Dictionary<string, MarketSnapshot>()).Should().BeNull();
    }

    // ── error handling ────────────────────────────────────────────────────────

    [Fact]
    public void Parse_EmptyString_ThrowsConditionParseException()
    {
        var act = () => _sut.Parse("");
        act.Should().Throw<ConditionParseException>()
            .WithMessage("*empty*");
    }

    [Fact]
    public void Parse_WhitespaceOnly_ThrowsConditionParseException()
    {
        var act = () => _sut.Parse("   ");
        act.Should().Throw<ConditionParseException>();
    }

    [Fact]
    public void And_SingleChild_ThrowsConditionParseException()
    {
        var act = () => _sut.Parse("AND(price(BTC/USD) > 40000)");
        act.Should().Throw<ConditionParseException>()
            .WithMessage("*at least two*");
    }

    [Fact]
    public void Not_TwoArgs_ThrowsConditionParseException()
    {
        var act = () => _sut.Parse("NOT(price(BTC/USD) > 40000, price(ETH/USD) > 3000)");
        act.Should().Throw<ConditionParseException>()
            .WithMessage("*exactly one*");
    }

    // ── unknown function treated as indicator ─────────────────────────────────

    [Fact]
    public void UnknownFunction_ParsesAsIndicator_WithoutThrowing()
    {
        // Unknown function names are treated as indicators and compile fine
        var cond = _sut.Parse("CUSTOMIND(7, BTC/USD) > 100");
        cond.Should().NotBeNull();
        cond.ToDisplayString().Should().Contain("CUSTOMIND_7");
    }
}

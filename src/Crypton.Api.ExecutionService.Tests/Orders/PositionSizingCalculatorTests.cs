using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Orders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Orders;

public sealed class PositionSizingCalculatorTests
{
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly InMemoryEventLogger _eventLogger = new();

    private PositionSizingCalculator CreateSut() =>
        new(_exchange, _eventLogger, NullLogger<PositionSizingCalculator>.Instance);

    private void SetBalance(decimal availableUsd) =>
        _exchange.GetAccountBalanceAsync(Arg.Any<CancellationToken>())
            .Returns(new AccountBalance { AvailableUsd = availableUsd, Timestamp = DateTimeOffset.UtcNow });

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Allocation10Pct_10kBalance_50kPrice_Returns0_02Btc()
    {
        SetBalance(10_000m);
        var sut = CreateSut();

        var qty = await sut.CalculateAsync("BTC/USD", 0.10m, 0.20m, 50_000m, "paper");

        // notional = 10000 * 0.10 = 1000; raw = 1000/50000 = 0.02; rounded = 0.0200
        qty.Should().Be(0.02m);
    }

    [Fact]
    public async Task AllocationExceedsMaxPerPosition_IsClamped_WarningLogged()
    {
        SetBalance(10_000m);
        var sut = CreateSut();

        // allocationPct=0.30 but maxPerPositionPct=0.20 → effective=0.20
        var qty = await sut.CalculateAsync("BTC/USD", 0.30m, 0.20m, 50_000m, "paper");

        // notional = 10000 * 0.20 = 2000; raw = 2000/50000 = 0.04; rounded = 0.0400
        qty.Should().Be(0.04m);
    }

    [Fact]
    public async Task ZeroCapital_ReturnsNull_EntrySkippedLogged()
    {
        SetBalance(0m);
        var sut = CreateSut();

        var qty = await sut.CalculateAsync("BTC/USD", 0.10m, 0.20m, 50_000m, "paper");

        qty.Should().BeNull();
        _eventLogger.Events.Should().ContainSingle(e =>
            e.EventType == EventTypes.EntrySkipped &&
            e.Data!["reason"]!.ToString() == "no_available_capital");
    }

    [Fact]
    public async Task ComputedQtyBelowMinLot_ReturnsNull_EntrySkippedLogged()
    {
        // $100 capital, $50000 price, 10% allocation → $10 notional → 0.0002 BTC, which is > 0.0001 (min)
        // So use very small capital to go below min
        SetBalance(1m);  // capital $1, 10% = $0.10 / $50000 = 0.000002 → below 0.0001 min
        var sut = CreateSut();

        var qty = await sut.CalculateAsync("BTC/USD", 0.10m, 0.20m, 50_000m, "paper");

        qty.Should().BeNull();
        _eventLogger.Events.Should().ContainSingle(e =>
            e.EventType == EventTypes.EntrySkipped &&
            e.Data!["reason"]!.ToString() == "below_minimum_lot_size");
    }

    [Fact]
    public async Task LotRounding_0_022341BtcRoundsDownTo0_0223()
    {
        // We want rawQuantity = 0.022341
        // rawQuantity = capital * pct / price
        // Let price = 1m, capital = 1m, pct = 0.022341
        // But we need BTC/USD increment = 0.0001
        // 0.022341 / 0.0001 = 223.41 → floor = 223 → 223 * 0.0001 = 0.0223
        SetBalance(1m);
        var sut = CreateSut();

        var qty = await sut.CalculateAsync("BTC/USD", 0.022341m, 1.0m, 1m, "paper");

        qty.Should().Be(0.0223m);
    }
}

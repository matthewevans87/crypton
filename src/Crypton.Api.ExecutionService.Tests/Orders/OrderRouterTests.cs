using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Metrics;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Orders;

public sealed class OrderRouterTests : IDisposable
{
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly InMemoryEventLogger _eventLogger = new();
    private readonly string _tempDir;
    private readonly PositionRegistry _registry;

    public OrderRouterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _eventLogger,
            NullLogger<PositionRegistry>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private OrderRouter CreateSut() =>
        new(_exchange, _registry, _eventLogger, NullLogger<OrderRouter>.Instance);

    private void SetupExchangeAck(string internalId, string exchangeId = "ex-001") =>
        _exchange.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(new OrderAcknowledgement
            {
                InternalId = internalId,
                ExchangeOrderId = exchangeId,
                Timestamp = DateTimeOffset.UtcNow
            });

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceEntryOrder_SendsCorrectRequest_LogsOrderPlaced()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-001",
                Timestamp = DateTimeOffset.UtcNow
            });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper");

        record.Should().NotBeNull();
        record!.Status.Should().Be(OrderStatus.Open);
        record.ExchangeOrderId.Should().Be("ex-001");

        captured.Should().NotBeNull();
        captured!.Asset.Should().Be("BTC/USD");
        captured.Side.Should().Be(OrderSide.Buy);
        captured.Quantity.Should().Be(0.01m);

        _eventLogger.Events.Should().ContainSingle(e => e.EventType == EventTypes.OrderPlaced);
    }

    [Fact]
    public async Task FillEvent_CausesPositionToBeOpenedInRegistry()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-001",
                Timestamp = DateTimeOffset.UtcNow
            });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper");
        record.Should().NotBeNull();

        var fill = new OrderFillEvent
        {
            ExchangeOrderId = "ex-001",
            InternalOrderId = record!.InternalId,
            FilledQuantity = 0.01m,
            FillPrice = 50_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = true
        };

        await sut.ApplyFillAsync(fill, "strat1", "paper");

        _registry.OpenPositions.Should().ContainSingle(p =>
            p.Asset == "BTC/USD" && p.Quantity == 0.01m && p.AverageEntryPrice == 50_000m);
    }

    [Fact]
    public async Task DuplicateEntry_SameStrategyPositionId_ReturnsNull()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-001",
                Timestamp = DateTimeOffset.UtcNow
            });

        var first = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper");
        first.Should().NotBeNull();

        var second = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper");

        second.Should().BeNull();
        await _exchange.Received(1).PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExchangeRejection_LogsOrderRejected_SetsStatusRejected()
    {
        var sut = CreateSut();
        _exchange.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Insufficient funds"));

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper");

        record.Should().NotBeNull();
        record!.Status.Should().Be(OrderStatus.Rejected);
        record.RejectionReason.Should().Contain("Insufficient funds");
        _eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.OrderRejected);
    }

    [Fact]
    public async Task CancelOrder_CallsAdapterCancel_LogsOrderCancelled()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-001",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.CancelOrderAsync("ex-001", Arg.Any<CancellationToken>())
            .Returns(new CancellationResult { ExchangeOrderId = "ex-001", Success = true });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper");

        await sut.CancelOrderAsync(record!.InternalId, "paper");

        await _exchange.Received(1).CancelOrderAsync("ex-001", Arg.Any<CancellationToken>());
        _eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.OrderCancelled);

        var updated = sut.ActiveOrders.Single(o => o.InternalId == record.InternalId);
        updated.Status.Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task PartialFill_UpdatesFilledQuantityAndAverageFillPrice()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-001",
                Timestamp = DateTimeOffset.UtcNow
            });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.02m, null, "sp1", "paper");

        // First partial fill: 0.01 @ 50000
        await sut.ApplyFillAsync(new OrderFillEvent
        {
            ExchangeOrderId = "ex-001",
            InternalOrderId = record!.InternalId,
            FilledQuantity = 0.01m,
            FillPrice = 50_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = false
        }, "strat1", "paper");

        // Second partial fill: 0.01 @ 51000
        await sut.ApplyFillAsync(new OrderFillEvent
        {
            ExchangeOrderId = "ex-001",
            InternalOrderId = record.InternalId,
            FilledQuantity = 0.01m,
            FillPrice = 51_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = false
        }, "strat1", "paper");

        var updated = sut.ActiveOrders.Single(o => o.InternalId == record.InternalId);
        updated.FilledQuantity.Should().Be(0.02m);
        updated.AverageFillPrice.Should().Be(50_500m);  // (50000 + 51000) / 2
    }

    [Fact]
    public async Task FillForUnknownOrder_LogsWarning_DoesNotThrow()
    {
        var sut = CreateSut();

        var fill = new OrderFillEvent
        {
            ExchangeOrderId = "ex-unknown",
            InternalOrderId = "nonexistent",
            FilledQuantity = 0.01m,
            FillPrice = 50_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = true
        };

        // Should not throw
        await sut.Invoking(s => s.ApplyFillAsync(fill, "strat1", "paper")).Should().NotThrowAsync();
        _registry.OpenPositions.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // StrategyId propagation
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceEntryOrder_StrategyId_StoredOnOrderRecord()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-sid",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.GetOrderStatusAsync("ex-sid", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult { ExchangeOrderId = "ex-sid", Status = OrderStatus.Open, FilledQuantity = 0m });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp1", "paper",
            strategyId: "strat-42");

        record.Should().NotBeNull();
        record!.StrategyId.Should().Be("strat-42");
    }

    [Fact]
    public async Task ApplyFill_StrategyId_FlowsFromOrderRecord_WhenNotOverridden()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-strat",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.GetOrderStatusAsync("ex-strat", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult { ExchangeOrderId = "ex-strat", Status = OrderStatus.Open, FilledQuantity = 0m });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-strat", "paper",
            strategyId: "my-strat");

        await sut.ApplyFillAsync(new OrderFillEvent
        {
            ExchangeOrderId = "ex-strat",
            InternalOrderId = record!.InternalId,
            FilledQuantity = 0.01m,
            FillPrice = 50_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = true
        }, strategyId: string.Empty, mode: "paper");

        var pos = _registry.OpenPositions.Single();
        pos.StrategyId.Should().Be("my-strat");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Short-sell position opening
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyFill_SellOrder_OpensShortPosition()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-short",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.GetOrderStatusAsync("ex-short", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult { ExchangeOrderId = "ex-short", Status = OrderStatus.Open, FilledQuantity = 0m });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Sell, OrderType.Market, 0.01m, null, "sp-short", "paper");

        await sut.ApplyFillAsync(new OrderFillEvent
        {
            ExchangeOrderId = "ex-short",
            InternalOrderId = record!.InternalId,
            FilledQuantity = 0.01m,
            FillPrice = 48_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = true
        }, string.Empty, "paper");

        _registry.OpenPositions.Should().ContainSingle();
        var pos = _registry.OpenPositions[0];
        pos.Direction.Should().Be("short");
        pos.Quantity.Should().Be(0.01m);
        pos.AverageEntryPrice.Should().Be(48_000m);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Metrics recording
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Metrics_AreRecorded_OnPlaceAndFill()
    {
        var metrics = Substitute.For<IMetricsCollector>();
        var sut = new OrderRouter(
            _exchange, _registry, _eventLogger,
            NullLogger<OrderRouter>.Instance,
            metrics: metrics);

        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-metrics",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.GetOrderStatusAsync("ex-metrics", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult { ExchangeOrderId = "ex-metrics", Status = OrderStatus.Open, FilledQuantity = 0m });

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-m", "paper");

        await sut.ApplyFillAsync(new OrderFillEvent
        {
            ExchangeOrderId = "ex-metrics",
            InternalOrderId = record!.InternalId,
            FilledQuantity = 0.01m,
            FillPrice = 50_000m,
            Timestamp = DateTimeOffset.UtcNow,
            IsFullFill = true
        }, string.Empty, "paper");

        metrics.Received(1).RecordOrderPlaced();
        metrics.Received(1).RecordOrderOpened();
        metrics.Received(1).RecordOrderFilled();
        metrics.Received(1).RecordPositionOpened();
    }

    [Fact]
    public async Task Metrics_RecordOrderRejected_OnExchangeError()
    {
        var metrics = Substitute.For<IMetricsCollector>();
        var sut = new OrderRouter(
            _exchange, _registry, _eventLogger,
            NullLogger<OrderRouter>.Instance,
            metrics: metrics);

        _exchange.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Insufficient funds"));

        var record = await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-rej", "paper");

        record!.Status.Should().Be(OrderStatus.Rejected);
        metrics.Received(1).RecordOrderPlaced();
        metrics.Received(1).RecordOrderRejected();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // ApplyFillByExchangeOrderIdAsync — reverse lookup
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyFillByExchangeOrderId_CorrelatesAndOpensPosition()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-rev",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.GetOrderStatusAsync("ex-rev", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult { ExchangeOrderId = "ex-rev", Status = OrderStatus.Open, FilledQuantity = 0m });

        await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-rev", "live");

        await sut.ApplyFillByExchangeOrderIdAsync(
            "ex-rev", filledQty: 0.01m, fillPrice: 50_000m, isFullFill: true,
            timestamp: DateTimeOffset.UtcNow, mode: "live");

        _registry.OpenPositions.Should().ContainSingle(p => p.Asset == "BTC/USD");
    }

    [Fact]
    public async Task ApplyFillByExchangeOrderId_UnknownId_DoesNotThrow()
    {
        var sut = CreateSut();

        await sut.Awaiting(s => s.ApplyFillByExchangeOrderIdAsync(
                "UNKNOWN-EX-ID", 0.01m, 50_000m, true,
                DateTimeOffset.UtcNow, "live"))
            .Should().NotThrowAsync();

        _registry.OpenPositions.Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyFillByExchangeOrderId_Idempotent_WhenCalledTwice()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-idem",
                Timestamp = DateTimeOffset.UtcNow
            });
        _exchange.GetOrderStatusAsync("ex-idem", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult { ExchangeOrderId = "ex-idem", Status = OrderStatus.Open, FilledQuantity = 0m });

        await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-idem", "live");

        // Apply the same fill twice.
        await sut.ApplyFillByExchangeOrderIdAsync(
            "ex-idem", 0.01m, 50_000m, true, DateTimeOffset.UtcNow, "live");
        await sut.ApplyFillByExchangeOrderIdAsync(
            "ex-idem", 0.01m, 50_000m, true, DateTimeOffset.UtcNow, "live");

        // Only one position should be opened.
        _registry.OpenPositions.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // TryApplyImmediateFillAsync — paper-mode immediate fill
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PlaceEntryOrder_WhenExchangeFillsImmediately_PositionIsOpenedInSameCall()
    {
        var sut = CreateSut();
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = "ex-instant",
                Timestamp = DateTimeOffset.UtcNow
            });

        // Exchange reports immediate fill on status poll.
        _exchange.GetOrderStatusAsync("ex-instant", Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult
            {
                ExchangeOrderId = "ex-instant",
                Status = OrderStatus.Filled,
                FilledQuantity = 0.01m,
                AverageFillPrice = 50_000m
            });

        await sut.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-instant", "paper");

        // Position should be open thanks to TryApplyImmediateFillAsync.
        _registry.OpenPositions.Should().ContainSingle(p =>
            p.AverageEntryPrice == 50_000m && p.Quantity == 0.01m);
    }
}

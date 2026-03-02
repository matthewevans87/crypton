using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Exchange;

/// <summary>
/// Tests for KrakenWsExecutionAdapter.HandleMessage — the JSON parsing and fill-dispatch
/// logic. Tests use a real OrderRouter (backed by a mock exchange) so that the
/// ExchangeOrderId → InternalOrderId reverse map is populated prior to calling HandleMessage.
/// </summary>
public sealed class KrakenWsExecutionAdapterTests : IDisposable
{
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly InMemoryEventLogger _eventLogger = new();
    private readonly string _tempDir;
    private readonly PositionRegistry _registry;
    private readonly OrderRouter _router;
    private readonly IOperationModeService _modeService = Substitute.For<IOperationModeService>();
    private readonly KrakenRestAdapter _rest;
    private readonly KrakenWsExecutionAdapter _sut;

    public KrakenWsExecutionAdapterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _registry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _eventLogger,
            NullLogger<PositionRegistry>.Instance);

        _router = new OrderRouter(
            _exchange, _registry, _eventLogger,
            NullLogger<OrderRouter>.Instance);

        // KrakenRestAdapter is needed for construction but won't be called in message-parse tests.
        _rest = new KrakenRestAdapter(
            apiKey: "test", apiSecret: "test",
            httpClient: new HttpClient { BaseAddress = new Uri("https://api.kraken.com") },
            logger: NullLogger<KrakenRestAdapter>.Instance);

        _modeService.CurrentMode.Returns("live");

        _sut = new KrakenWsExecutionAdapter(
            _rest, _router, _modeService,
            NullLogger<KrakenWsExecutionAdapter>.Instance);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ─────────────────────────────────────────────────────────────────────────
    // HandleMessage — fill event triggers ApplyFillByExchangeOrderIdAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleMessage_FullFill_OpensPositionInRegistry()
    {
        // Arrange: place an order so the reverse-lookup map is populated.
        const string exchangeOrderId = "KRAKEN-ORDER-001";
        SetupFillableOrder(exchangeOrderId);

        await PlaceOrderAsync();

        // Act: simulate a fill arriving from the executions WS channel.
        var json = BuildFillJson(exchangeOrderId, qty: 0.01m, price: 50_000m,
            execType: "trade", orderStatus: "filled");

        _sut.HandleMessage(json);

        // Give the fire-and-forget Task a moment to complete.
        await Task.Delay(200);

        // Assert: position should now be open in the registry.
        _registry.OpenPositions.Should().ContainSingle(p => p.Asset == "BTC/USD");
        var pos = _registry.OpenPositions[0];
        pos.Direction.Should().Be("long");
        pos.Quantity.Should().Be(0.01m);
        pos.AverageEntryPrice.Should().Be(50_000m);
    }

    [Fact]
    public async Task HandleMessage_ExecTypeFilled_AlsoOpensPosition()
    {
        const string exchangeOrderId = "KRAKEN-ORDER-002";
        SetupFillableOrder(exchangeOrderId);
        await PlaceOrderAsync();

        var json = BuildFillJson(exchangeOrderId, qty: 0.01m, price: 45_000m,
            execType: "filled", orderStatus: "filled");

        _sut.HandleMessage(json);
        await Task.Delay(200);

        _registry.OpenPositions.Should().ContainSingle();
    }

    [Fact]
    public async Task HandleMessage_PartialFill_UpdatesFilledQtyButDoesNotOpenPosition()
    {
        const string exchangeOrderId = "KRAKEN-ORDER-003";
        SetupFillableOrder(exchangeOrderId);
        await PlaceOrderAsync();

        var json = BuildFillJson(exchangeOrderId, qty: 0.005m, price: 50_000m,
            execType: "trade", orderStatus: "open");  // order_status not "filled" → partial

        _sut.HandleMessage(json);
        await Task.Delay(200);

        // No position opened for partial fill.
        _registry.OpenPositions.Should().BeEmpty();

        // But the order should be partially filled.
        var orders = _router.ActiveOrders;
        orders.Should().ContainSingle(o => o.FilledQuantity == 0.005m);
    }

    [Fact]
    public async Task HandleMessage_UnknownExchangeOrderId_IsIgnoredGracefully()
    {
        // Do NOT place an order — exchange order ID not in the reverse map.
        var json = BuildFillJson("UNKNOWN-ORDER", qty: 0.01m, price: 50_000m,
            execType: "trade", orderStatus: "filled");

        _sut.HandleMessage(json);
        await Task.Delay(200);

        _registry.OpenPositions.Should().BeEmpty();
    }

    [Fact]
    public void HandleMessage_NonExecutionsChannel_IsIgnored()
    {
        const string json = """
            {"channel": "ticker", "type": "update", "data": [{"symbol": "BTC/USD", "bid": 50000}]}
            """;

        // Should not throw.
        _sut.Invoking(s => s.HandleMessage(json)).Should().NotThrow();
        _registry.OpenPositions.Should().BeEmpty();
    }

    [Fact]
    public void HandleMessage_MalformedJson_IsIgnoredGracefully()
    {
        _sut.Invoking(s => s.HandleMessage("{not valid json")).Should().NotThrow();
    }

    [Fact]
    public void HandleMessage_NewEventType_IsIgnored()
    {
        // "new" exec_type means order accepted, not a fill — should be ignored.
        var json = BuildFillJson("KRAKEN-ORDER-NEW", qty: 0.01m, price: 50_000m,
            execType: "new", orderStatus: "open");

        _sut.Invoking(s => s.HandleMessage(json)).Should().NotThrow();
        _registry.OpenPositions.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SetupFillableOrder(string exchangeOrderId, OrderStatus statusPollResult = OrderStatus.Open)
    {
        PlaceOrderRequest? captured = null;
        _exchange.PlaceOrderAsync(
            Arg.Do<PlaceOrderRequest>(r => captured = r),
            Arg.Any<CancellationToken>())
            .Returns(_ => new OrderAcknowledgement
            {
                InternalId = captured!.InternalId,
                ExchangeOrderId = exchangeOrderId,
                Timestamp = DateTimeOffset.UtcNow
            });

        // Default: return Open status on poll so TryApplyImmediateFillAsync does NOT fill the
        // order — the WS message (HandleMessage) should be the authoritative fill source.
        _exchange.GetOrderStatusAsync(exchangeOrderId, Arg.Any<CancellationToken>())
            .Returns(new OrderStatusResult
            {
                ExchangeOrderId = exchangeOrderId,
                Status = statusPollResult,
                FilledQuantity = statusPollResult == OrderStatus.Filled ? 0.01m : 0m,
                AverageFillPrice = statusPollResult == OrderStatus.Filled ? 50_000m : null
            });
    }

    private async Task PlaceOrderAsync() =>
        await _router.PlaceEntryOrderAsync(
            "BTC/USD", OrderSide.Buy, OrderType.Market, 0.01m, null, "sp-ws-test", "live");

    private static string BuildFillJson(
        string orderId, decimal qty, decimal price,
        string execType, string orderStatus)
    {
        return $$"""
            {
              "channel": "executions",
              "type": "update",
              "data": [{
                "order_id": "{{orderId}}",
                "exec_id": "exec-{{Guid.NewGuid():N}}",
                "exec_type": "{{execType}}",
                "order_status": "{{orderStatus}}",
                "last_qty": {{qty}},
                "avg_price": {{price}},
                "last_price": {{price}},
                "symbol": "BTC/USD",
                "side": "buy",
                "timestamp": "{{DateTimeOffset.UtcNow:O}}"
              }]
            }
            """;
    }
}

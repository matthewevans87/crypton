using System.Text.Json;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Resilience;

public sealed class SafeModeControllerTests : IDisposable
{
    private readonly string _tempDir;
    private readonly InMemoryEventLogger _eventLogger = new();
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly PositionRegistry _registry;
    private readonly FailureTracker _failureTracker;
    private readonly OrderRouter _orderRouter;

    public SafeModeControllerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var opts = Options.Create(new ExecutionServiceConfig
        {
            Safety = new SafetyConfig { ConsecutiveFailureThreshold = 3, ResilienceStatePath = _tempDir }
        });

        _registry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _eventLogger,
            NullLogger<PositionRegistry>.Instance);

        _failureTracker = new FailureTracker(opts, NullLogger<FailureTracker>.Instance);

        _orderRouter = new OrderRouter(
            _exchange, _registry, _eventLogger,
            NullLogger<OrderRouter>.Instance,
            _failureTracker);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private SafeModeController CreateSut() =>
        new(Options.Create(new ExecutionServiceConfig
        {
            Safety = new SafetyConfig { ConsecutiveFailureThreshold = 3, ResilienceStatePath = _tempDir }
        }), _eventLogger, _registry, _orderRouter, _failureTracker,
            NullLogger<SafeModeController>.Instance);

    private void SetupExchangeAck() =>
        _exchange.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = ((PlaceOrderRequest)ci[0]).InternalId,
                ExchangeOrderId = "ex-safe-001",
                Timestamp = DateTimeOffset.UtcNow
            });

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_ReturnsFalse_WhenNoStateFileExists()
    {
        var sut = CreateSut();
        var result = await sut.LoadAsync();
        result.Should().BeFalse();
        sut.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_ReturnsTrue_AndSetsIsActive_WhenFileHasActiveTrue()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "safe_mode.json"),
            """{"active": true, "triggered_at": "2024-01-01T00:00:00Z", "reason": "consecutive_failures"}""");

        var sut = CreateSut();
        var result = await sut.LoadAsync();

        result.Should().BeTrue();
        sut.IsActive.Should().BeTrue();
        sut.Reason.Should().Be("consecutive_failures");
    }

    [Fact]
    public async Task ActivateAsync_SetsIsActiveTrue()
    {
        SetupExchangeAck();
        var sut = CreateSut();

        await sut.ActivateAsync("consecutive_failures");

        sut.IsActive.Should().BeTrue();
        sut.Reason.Should().Be("consecutive_failures");
    }

    [Fact]
    public async Task ActivateAsync_PersistsStateToDisk()
    {
        SetupExchangeAck();
        var sut = CreateSut();

        await sut.ActivateAsync("consecutive_failures");

        var json = File.ReadAllText(Path.Combine(_tempDir, "safe_mode.json"));
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("active").GetBoolean().Should().BeTrue();
        doc.RootElement.GetProperty("reason").GetString().Should().Be("consecutive_failures");
    }

    [Fact]
    public async Task ActivateAsync_DispatchesMarketSellForEachOpenLongPosition()
    {
        SetupExchangeAck();
        var sut = CreateSut();

        // Add a long position to the registry
        _registry.UpsertPosition(new OpenPosition
        {
            Id = "pos-1",
            StrategyPositionId = "sp-1",
            StrategyId = "strat-1",
            Asset = "BTC/USD",
            Direction = "long",
            Quantity = 0.1m,
            AverageEntryPrice = 50_000m,
            OpenedAt = DateTimeOffset.UtcNow
        });

        await sut.ActivateAsync("consecutive_failures");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r =>
                r.Asset == "BTC/USD" &&
                r.Side == OrderSide.Sell &&
                r.Type == OrderType.Market),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActivateAsync_EmitsSafeModeActivatedEvent()
    {
        SetupExchangeAck();
        var sut = CreateSut();

        await sut.ActivateAsync("test_reason");

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.SafeModeActivated &&
            e.Data != null &&
            e.Data.ContainsKey("reason") &&
            (string?)e.Data["reason"] == "test_reason");
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse_AndPersists()
    {
        SetupExchangeAck();
        var sut = CreateSut();
        await sut.ActivateAsync("consecutive_failures");

        await sut.DeactivateAsync();

        sut.IsActive.Should().BeFalse();
        sut.Reason.Should().BeNull();

        var json = File.ReadAllText(Path.Combine(_tempDir, "safe_mode.json"));
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("active").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateAsync_EmitsSafeModeDeactivatedEvent()
    {
        SetupExchangeAck();
        var sut = CreateSut();
        await sut.ActivateAsync("consecutive_failures");

        await sut.DeactivateAsync();

        _eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.SafeModeDeactivated);
    }

    [Fact]
    public async Task ActivateAsync_WhenAlreadyActive_IsNoOp()
    {
        SetupExchangeAck();
        var sut = CreateSut();

        await sut.ActivateAsync("reason_one");
        await sut.ActivateAsync("reason_two");

        sut.Reason.Should().Be("reason_one");  // second call is a no-op
        _eventLogger.Events.Count(e => e.EventType == EventTypes.SafeModeActivated).Should().Be(1);
        await _exchange.Received(0).PlaceOrderAsync(  // no extra orders from second call
            Arg.Any<PlaceOrderRequest>(),
            Arg.Any<CancellationToken>());
    }
}

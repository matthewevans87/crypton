using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Resilience;

public sealed class ReconciliationServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly InMemoryEventLogger _eventLogger = new();
    private readonly PositionRegistry _registry;
    private FailureTracker _failureTracker;

    public ReconciliationServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _registry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _eventLogger,
            NullLogger<PositionRegistry>.Instance);

        _failureTracker = CreateDefaultFailureTracker();
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FailureTracker CreateDefaultFailureTracker() =>
        new(Options.Create(new ExecutionServiceConfig
        {
            Safety = new SafetyConfig { ConsecutiveFailureThreshold = 3, ResilienceStatePath = _tempDir }
        }), NullLogger<FailureTracker>.Instance);

    private ReconciliationService CreateSut(FailureTracker? failureTracker = null) =>
        new(_exchange, _registry, _eventLogger,
            NullLogger<ReconciliationService>.Instance,
            failureTracker ?? _failureTracker);

    private static OpenPosition MakePosition(string asset, string direction, string? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid().ToString("N"),
            StrategyPositionId = $"sp-{asset}",
            StrategyId = "strat-1",
            Asset = asset,
            Direction = direction,
            Quantity = 0.5m,
            AverageEntryPrice = 40_000m,
            OpenedAt = DateTimeOffset.UtcNow
        };

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OrphanedRegistryPosition_IsClosedWithReconciledMissingReason()
    {
        // Registry has a position; exchange has nothing.
        var pos = MakePosition("BTC/USD", "long");
        _registry.UpsertPosition(pos);
        _exchange.GetOpenPositionsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ExchangePosition>());

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await sut.ReconciliationTask!;

        _registry.OpenPositions.Should().BeEmpty();
        _registry.ClosedTrades.Should().ContainSingle(t =>
            t.PositionId == pos.Id && t.ExitReason == "reconciled_missing");

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.PositionClosed &&
            e.Data != null &&
            (string?)e.Data.GetValueOrDefault("exit_reason") == "reconciled_missing" &&
            (string?)e.Data.GetValueOrDefault("origin") == "reconciliation");
    }

    [Fact]
    public async Task UnknownExchangePosition_IsAddedToRegistryWithOriginReconciled()
    {
        // Registry is empty; exchange has a position.
        _exchange.GetOpenPositionsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ExchangePosition>
            {
                new() { Asset = "ETH/USD", Direction = "long", Quantity = 2m, AverageEntryPrice = 3_000m }
            });

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await sut.ReconciliationTask!;

        var added = _registry.OpenPositions.Should().ContainSingle()
            .Which;
        added.Asset.Should().Be("ETH/USD");
        added.Direction.Should().Be("long");
        added.Origin.Should().Be("reconciled");

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.PositionOpened &&
            e.Data != null &&
            (string?)e.Data["origin"] == "reconciliation");
    }

    [Fact]
    public async Task MatchingPositions_OnBothSides_NoChanges()
    {
        // Registry and exchange both have BTC/USD long.
        var pos = MakePosition("BTC/USD", "long");
        _registry.UpsertPosition(pos);
        _exchange.GetOpenPositionsAsync(Arg.Any<CancellationToken>())
            .Returns(new List<ExchangePosition>
            {
                new() { Asset = "BTC/USD", Direction = "long", Quantity = 0.5m, AverageEntryPrice = 40_000m }
            });

        _eventLogger.Clear();

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await sut.ReconciliationTask!;

        _registry.OpenPositions.Should().ContainSingle(p => p.Id == pos.Id);
        _registry.ClosedTrades.Should().BeEmpty();
        _eventLogger.Events.Should().NotContain(e =>
            e.EventType == EventTypes.PositionClosed || e.EventType == EventTypes.PositionOpened);
    }

    [Fact]
    public async Task ExchangeAdapterThrows_NoCrash_ErrorEventLogged()
    {
        _exchange.GetOpenPositionsAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExchangeAdapterException("connection refused"));

        var sut = CreateSut();

        var act = async () =>
        {
            await sut.StartAsync(CancellationToken.None);
            await sut.ReconciliationTask!;
        };

        await act.Should().NotThrowAsync();

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ReconciliationSummary &&
            e.Data != null &&
            (string?)e.Data["status"] == "error");
    }

    [Fact]
    public async Task SafeModeTriggered_ReconciliationIsSkipped()
    {
        // Pre-write a failure state so FailureTracker loads with SafeModeTriggered = true.
        File.WriteAllText(
            Path.Combine(_tempDir, "failure_count.json"),
            """{"consecutive_failures": 3, "last_failure_utc": "2024-01-01T00:00:00Z"}""");

        var triggeredTracker = new FailureTracker(
            Options.Create(new ExecutionServiceConfig
            {
                Safety = new SafetyConfig { ConsecutiveFailureThreshold = 3, ResilienceStatePath = _tempDir }
            }), NullLogger<FailureTracker>.Instance);

        triggeredTracker.SafeModeTriggered.Should().BeTrue();

        var sut = CreateSut(triggeredTracker);
        await sut.StartAsync(CancellationToken.None);
        await sut.ReconciliationTask!;

        await _exchange.DidNotReceive().GetOpenPositionsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReconciliationSummaryEvent_IsAlwaysLogged_OnSuccessfulRun()
    {
        _exchange.GetOpenPositionsAsync(Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ExchangePosition>());

        _eventLogger.Clear();

        var sut = CreateSut();
        await sut.StartAsync(CancellationToken.None);
        await sut.ReconciliationTask!;

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ReconciliationSummary &&
            e.Data != null &&
            (string?)e.Data["status"] == "ok");
    }
}

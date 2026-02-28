using System.Text.Json;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Execution;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Strategy;
using Crypton.Api.ExecutionService.Strategy.Conditions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Execution;

public sealed class EntryEvaluatorTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly InMemoryEventLogger _eventLogger = new();
    private readonly PositionSizingCalculator _sizingCalc;
    private readonly PositionRegistry _positionRegistry;
    private readonly PortfolioRiskEnforcer _riskEnforcer;
    private readonly ConditionParser _conditionParser = new();
    private StrategyService? _strategyService;

    public EntryEvaluatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cee_entry_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _positionRegistry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _eventLogger,
            NullLogger<PositionRegistry>.Instance);

        _riskEnforcer = new PortfolioRiskEnforcer(_eventLogger);
        _sizingCalc = new PositionSizingCalculator(_exchange, _eventLogger, NullLogger<PositionSizingCalculator>.Instance);

        // Default exchange stubs
        SetBalance(10_000m);
        _exchange.PlaceOrderAsync(Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>())
            .Returns(ci => new OrderAcknowledgement
            {
                InternalId = ((PlaceOrderRequest)ci[0]).InternalId,
                ExchangeOrderId = Guid.NewGuid().ToString("N"),
                Timestamp = DateTimeOffset.UtcNow
            });
    }

    public async ValueTask DisposeAsync()
    {
        if (_strategyService is not null)
        {
            await _strategyService.StopAsync(CancellationToken.None);
            _strategyService.Dispose();
        }
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private void SetBalance(decimal available) =>
        _exchange.GetAccountBalanceAsync(Arg.Any<CancellationToken>())
            .Returns(new AccountBalance { AvailableUsd = available, Timestamp = DateTimeOffset.UtcNow });

    private (StrategyService svc, OrderRouter router, EntryEvaluator sut) Create(string strategyFileName = "strategy.json")
    {
        var watchPath = Path.Combine(_tempDir, strategyFileName);
        var config = Options.Create(new ExecutionServiceConfig
        {
            Strategy = new StrategyConfig
            {
                WatchPath = watchPath,
                ReloadLatencyMs = 0,
                ValidityCheckIntervalMs = 100
            }
        });

        var svc = new StrategyService(config, new StrategyValidator(), _conditionParser,
            _eventLogger, NullLogger<StrategyService>.Instance);
        _strategyService = svc;

        var router = new OrderRouter(_exchange, _positionRegistry, _eventLogger, NullLogger<OrderRouter>.Instance);

        var sut = new EntryEvaluator(
            router, _sizingCalc, _riskEnforcer, svc, _conditionParser,
            _positionRegistry, _exchange, _eventLogger, NullLogger<EntryEvaluator>.Instance);

        return (svc, router, sut);
    }

    private static string MakeStrategyJson(
        string posture = "moderate",
        IReadOnlyList<StrategyPosition>? positions = null,
        decimal maxTotalExposurePct = 0.8m)
    {
        var doc = new StrategyDocument
        {
            Mode = "paper",
            Posture = posture,
            ValidityWindow = DateTimeOffset.UtcNow.AddHours(1),
            PortfolioRisk = new PortfolioRisk
            {
                MaxDrawdownPct = 0.1m,
                DailyLossLimitUsd = 1_000m,
                MaxTotalExposurePct = maxTotalExposurePct,
                MaxPerPositionPct = 0.2m
            },
            Positions = positions ?? [new StrategyPosition
            {
                Id = "sp-1",
                Asset = "BTC/USD",
                Direction = "long",
                AllocationPct = 0.1m,
                EntryType = "market"
            }]
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions
        { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
    }

    private async Task LoadStrategyAsync(StrategyService svc, string json, string fileName = "strategy.json")
    {
        var tcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.OnStrategyLoaded += doc => { tcs.TrySetResult(doc); return Task.CompletedTask; };
        File.WriteAllText(Path.Combine(_tempDir, fileName), json);
        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static IReadOnlyDictionary<string, MarketSnapshot> Snap(
        string asset = "BTC/USD", decimal bid = 50_000m, decimal ask = 50_010m,
        IReadOnlyDictionary<string, decimal>? indicators = null)
    {
        return new Dictionary<string, MarketSnapshot>
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
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MarketEntry_TriggersImmediately_OnFirstEvaluate()
    {
        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson());

        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Asset == "BTC/USD" && r.Side == OrderSide.Buy),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.EntryTriggered);
    }

    [Fact]
    public async Task ConditionalEntry_Triggers_WhenConditionIsTrue()
    {
        var pos = new StrategyPosition
        {
            Id = "sp-cond",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "conditional",
            EntryCondition = "price(BTC/USD) > 40000"
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson(positions: [pos]));

        // Provide snapshot above threshold
        await sut.EvaluateAsync(Snap(bid: 41_000m, ask: 41_010m), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Asset == "BTC/USD"),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e => e.EventType == EventTypes.EntryTriggered);
    }

    [Fact]
    public async Task ConditionalEntry_DoesNotTrigger_WhenConditionIsFalse()
    {
        var pos = new StrategyPosition
        {
            Id = "sp-cond",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "conditional",
            EntryCondition = "price(BTC/USD) > 40000"
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson(positions: [pos]));

        // Snapshot below threshold
        await sut.EvaluateAsync(Snap(bid: 38_000m, ask: 38_010m), "paper");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().NotContain(e => e.EventType == EventTypes.EntryTriggered);
    }

    [Fact]
    public async Task Entry_DispatchedOnlyOnce_EvenIfCalledRepeatedly()
    {
        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson());

        await sut.EvaluateAsync(Snap(), "paper");
        await sut.EvaluateAsync(Snap(), "paper");
        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExitAll_Posture_PreventsAnyEntries()
    {
        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson(posture: "exit_all"));

        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().NotContain(e => e.EventType == EventTypes.EntryTriggered);
    }

    [Fact]
    public async Task FlatPosture_PreventsAnyEntries()
    {
        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson(posture: "flat"));

        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Entry_Skipped_WhenPortfolioRiskEnforcerSuspendsEntries()
    {
        // MaxTotalExposurePct = 0 → 0 exposure >= 0 cap → EntriesSuspended immediately
        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson(maxTotalExposurePct: 0m));

        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().NotContain(e => e.EventType == EventTypes.EntryTriggered);
    }

    [Fact]
    public async Task IndicatorNotReady_LogsEntrySkipped_WithIndicatorNotReadyReason()
    {
        // Use an indicator condition; snapshot doesn't contain the indicator key → null result
        var pos = new StrategyPosition
        {
            Id = "sp-ind",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "conditional",
            EntryCondition = "RSI_14(BTC/USD) < 30"  // indicator not in snapshot
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategyJson(positions: [pos]));

        // Snapshot with no RSI_14 indicator
        await sut.EvaluateAsync(Snap(), "paper");

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.EntrySkipped &&
            e.Data != null &&
            e.Data["reason"]!.ToString() == "indicator_not_ready");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StrategyNotLoaded_NoEntriesDispatched()
    {
        // Don't write or load any strategy file — service remains in None state.
        var (svc, router, sut) = Create();
        await svc.StartAsync(CancellationToken.None);

        // State is None, no strategy
        svc.State.Should().Be(StrategyState.None);

        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }
}

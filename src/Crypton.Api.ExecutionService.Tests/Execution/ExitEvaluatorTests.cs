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

public sealed class ExitEvaluatorTests : IAsyncDisposable
{
    private readonly string _tempDir;
    private readonly IExchangeAdapter _exchange = Substitute.For<IExchangeAdapter>();
    private readonly InMemoryEventLogger _eventLogger = new();
    private readonly PositionRegistry _registry;
    private readonly ConditionParser _conditionParser = new();
    private StrategyService? _strategyService;

    public ExitEvaluatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"cee_exit_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _registry = new PositionRegistry(
            Path.Combine(_tempDir, "positions.json"),
            Path.Combine(_tempDir, "trades.json"),
            _eventLogger,
            NullLogger<PositionRegistry>.Instance);

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

    private (StrategyService svc, OrderRouter router, ExitEvaluator sut) Create(string fileName = "strategy.json")
    {
        var watchPath = Path.Combine(_tempDir, fileName);
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

        var router = new OrderRouter(_exchange, _registry, _eventLogger, NullLogger<OrderRouter>.Instance);

        var sut = new ExitEvaluator(
            _registry, router, svc, _conditionParser,
            _eventLogger, NullLogger<ExitEvaluator>.Instance);

        return (svc, router, sut);
    }

    private static StrategyDocument MakeStrategy(
        string posture = "moderate",
        IReadOnlyList<StrategyPosition>? positions = null)
    {
        return new StrategyDocument
        {
            Mode = "paper",
            Posture = posture,
            ValidityWindow = DateTimeOffset.UtcNow.AddHours(1),
            PortfolioRisk = new PortfolioRisk
            {
                MaxDrawdownPct = 0.2m,
                DailyLossLimitUsd = 1_000m,
                MaxTotalExposurePct = 0.8m,
                MaxPerPositionPct = 0.2m
            },
            Positions = positions ?? []
        };
    }

    private async Task LoadStrategyAsync(StrategyService svc, StrategyDocument strategy)
    {
        var tcs = new TaskCompletionSource<StrategyDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
        svc.OnStrategyLoaded += doc => { tcs.TrySetResult(doc); return Task.CompletedTask; };
        var json = JsonSerializer.Serialize(strategy,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });
        File.WriteAllText(Path.Combine(_tempDir, "strategy.json"), json);
        await svc.StartAsync(CancellationToken.None);
        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private OpenPosition CreateOpenPosition(
        string stratPositionId = "sp-1",
        string asset = "BTC/USD",
        string direction = "long",
        decimal quantity = 0.5m,
        decimal entryPrice = 45_000m,
        decimal? trailingStop = null)
    {
        var pos = new OpenPosition
        {
            Id = Guid.NewGuid().ToString("N"),
            StrategyPositionId = stratPositionId,
            StrategyId = "strat-test",
            Asset = asset,
            Direction = direction,
            Quantity = quantity,
            AverageEntryPrice = entryPrice,
            OpenedAt = DateTimeOffset.UtcNow,
            TrailingStopPrice = trailingStop
        };
        _registry.UpsertPosition(pos);
        return pos;
    }

    private static IReadOnlyDictionary<string, MarketSnapshot> Snap(
        string asset = "BTC/USD",
        decimal bid = 50_000m,
        decimal ask = 50_010m)
    {
        return new Dictionary<string, MarketSnapshot>
        {
            [asset] = new MarketSnapshot
            {
                Asset = asset,
                Bid = bid,
                Ask = ask,
                Timestamp = DateTimeOffset.UtcNow
            }
        };
    }

    // ── stop-loss tests ────────────────────────────────────────────────────

    [Fact]
    public async Task HardStopLoss_Long_Triggers_WhenBidAtOrBelowStopPrice()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            StopLoss = new StopLoss { Type = "hard", Price = 40_000m }
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long");

        // bid = 39900 ≤ stop 40000
        await sut.EvaluateAsync(Snap(bid: 39_900m, ask: 39_910m), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Asset == "BTC/USD" && r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ExitTriggered &&
            e.Data!["reason"]!.ToString() == "stop_loss_hard");
    }

    [Fact]
    public async Task HardStopLoss_Long_DoesNotTrigger_WhenBidAboveStopPrice()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            StopLoss = new StopLoss { Type = "hard", Price = 40_000m }
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long");

        // bid = 41000 > stop 40000
        await sut.EvaluateAsync(Snap(bid: 41_000m, ask: 41_010m), "paper");

        await _exchange.DidNotReceive().PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());
    }

    // ── trailing stop tests ─────────────────────────────────────────────────

    [Fact]
    public async Task TrailingStop_IsInitialised_OnFirstUpwardTick_ForLong()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            StopLoss = new StopLoss { Type = "trailing", TrailPct = 0.05m }
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        var pos = CreateOpenPosition(stratPositionId: "sp-1", direction: "long");

        // No trailing stop yet; bid at 50000 → expected stop = 50000 * (1 - 0.05) = 47500
        await sut.EvaluateAsync(Snap(bid: 50_000m, ask: 50_010m), "paper");

        var updated = _registry.OpenPositions.First(p => p.Id == pos.Id);
        updated.TrailingStopPrice.Should().Be(47_500m);
    }

    [Fact]
    public async Task TrailingStop_DoesNotMoveBackward_WhenPriceDrops()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            StopLoss = new StopLoss { Type = "trailing", TrailPct = 0.05m }
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        // Position starts with a trailing stop already set at 47500
        var pos = CreateOpenPosition(stratPositionId: "sp-1", direction: "long", trailingStop: 47_500m);

        // Price drops to 49000: new candidate = 49000*0.95 = 46550 < 47500 → should NOT update
        await sut.EvaluateAsync(Snap(bid: 49_000m, ask: 49_010m), "paper");

        var updated = _registry.OpenPositions.FirstOrDefault(p => p.Id == pos.Id);
        // Position may have been closed if bid < 47500; bid 49000 > 47500 so not closed
        updated.Should().NotBeNull();
        updated!.TrailingStopPrice.Should().Be(47_500m);
    }

    [Fact]
    public async Task TrailingStop_Triggers_WhenBidDropsToStopLevel()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            StopLoss = new StopLoss { Type = "trailing", TrailPct = 0.05m }
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        // Set trailing stop at 47500 (already established)
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long", trailingStop: 47_500m);

        // bid = 47000 ≤ trailing stop 47500 → should close
        await sut.EvaluateAsync(Snap(bid: 47_000m, ask: 47_010m), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ExitTriggered &&
            e.Data!["reason"]!.ToString() == "stop_loss_trailing");
    }

    // ── take-profit tests ───────────────────────────────────────────────────

    [Fact]
    public async Task TakeProfit_FirstTarget_FiresPartialClose_AtCorrectQuantity()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            TakeProfitTargets =
            [
                new TakeProfitTarget { Price = 55_000m, ClosePct = 0.5m },
                new TakeProfitTarget { Price = 60_000m, ClosePct = 0.5m }
            ]
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        // quantity = 1.0 BTC; half close = 0.5 BTC
        var pos = CreateOpenPosition(stratPositionId: "sp-1", direction: "long", quantity: 1.0m);

        // ask = 55000 ≥ TP1 price 55000
        await sut.EvaluateAsync(Snap(bid: 54_990m, ask: 55_000m), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Quantity == 0.5m && r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ExitTriggered &&
            e.Data!["reason"]!.ToString() == "take_profit_target_0");

        // Position registry should have recorded TP0 hit
        var updatedPos = _registry.OpenPositions.First(p => p.Id == pos.Id);
        updatedPos.TakeProfitTargetsHit.Should().Contain(0);
    }

    [Fact]
    public async Task TakeProfit_SecondTarget_DoesNotFire_BeforeFirstIsHit()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            TakeProfitTargets =
            [
                new TakeProfitTarget { Price = 55_000m, ClosePct = 0.5m },
                new TakeProfitTarget { Price = 60_000m, ClosePct = 0.5m }
            ]
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long", quantity: 1.0m);

        // ask = 61000 ≥ TP2 price 60000, but TP1 not yet hit
        // TP1 check: ask 61000 >= 55000 → yes → fires TP0; then TP1 not checked (break)
        // Actually with one tick, only TP0 fires. Let's provide a snap that skips TP0 somehow.
        // We cannot skip TP0 without having a price below 55000 for TP0 but above 60000 for TP1,
        // which is impossible for the same tick. So instead: position start with TP0 explicitly NOT
        // hit, and price at TP2 only → since the loop checks in order and TP0 is checked first (
        // and triggered), TP1 won't be checked on same tick. 
        // 
        // Better approach: manually craft a position that only starts checking at TP1 by having
        // ask < TP0 price but ask >= TP1 price — impossible since TP1 > TP0.
        //
        // Actually re-reading the code: the loop iterates i=0,1... For i=0: triggered (ask >= 55000).
        // For i=1: i>0 and !TakeProfitTargetsHit.Contains(0) → skip. So on same tick, TP1 never
        // fires if TP0 hasn't been previously recorded.
        //
        // So the real test is: provide a snapshot where ONLY TP2 (TP index 1) price is hit but
        // TP1 (index 0) price is NOT hit. That requires ask < 55000 (not TP0) and ask >= 60000
        // which is contradictory. But we can fabricate the scenario by using a position where
        // the i>0 guard applies — we need a scenario where ask >= TP1 but < TP0. 
        //
        // Given the TP targets in spec are ordered ascending, this scenario (ask between targets)
        // doesn't naturally arise. The guard is most useful when: TP0 was missed while offline
        // and TP1 is now above current price. 
        //
        // The meaningful test the spec asks for: "Second take-profit target does NOT fire before
        // first is hit." The loop ALWAYS hits TP0 first if ask is above both thresholds, but
        // via `break` it won't proceed to check TP1 on the same tick.
        //
        // So we test: on a tick where ask >= TP1 > TP0 (both triggered), only TP0 fires (break).
        await sut.EvaluateAsync(Snap(bid: 60_990m, ask: 61_000m), "paper");

        // Should have fired TP0 partial close, but NOT TP1 on the same tick
        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Any<PlaceOrderRequest>(), Arg.Any<CancellationToken>());

        _eventLogger.Events
            .Where(e => e.EventType == EventTypes.ExitTriggered)
            .Select(e => e.Data!["reason"]!.ToString())
            .Should().ContainSingle()
            .And.Contain("take_profit_target_0");
    }

    // ── time exit tests ─────────────────────────────────────────────────────

    [Fact]
    public async Task TimeExit_Triggers_WhenUtcTimePassedDeadline()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            TimeExitUtc = DateTimeOffset.UtcNow.AddSeconds(-1)  // already in the past
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long");

        await sut.EvaluateAsync(Snap(), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ExitTriggered &&
            e.Data!["reason"]!.ToString() == "time_exit");
    }

    // ── invalidation condition tests ─────────────────────────────────────────

    [Fact]
    public async Task InvalidationCondition_Triggers_WhenDslEvaluatesTrue()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            InvalidationCondition = "price(BTC/USD) > 55000"
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long");

        // Mid = (61000+61010)/2 = 61005 > 55000 → invalidation condition true
        await sut.EvaluateAsync(Snap(bid: 61_000m, ask: 61_010m), "paper");

        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Should().Contain(e =>
            e.EventType == EventTypes.ExitTriggered &&
            e.Data!["reason"]!.ToString() == "invalidation");
    }

    // ── exit_all posture ────────────────────────────────────────────────────

    [Fact]
    public async Task ExitAll_Posture_ClosesAllOpenPositions()
    {
        var stratPos1 = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market"
        };
        var stratPos2 = new StrategyPosition
        {
            Id = "sp-2",
            Asset = "ETH/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market"
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(posture: "exit_all", positions: [stratPos1, stratPos2]));

        CreateOpenPosition(stratPositionId: "sp-1", asset: "BTC/USD", direction: "long");
        CreateOpenPosition(stratPositionId: "sp-2", asset: "ETH/USD", direction: "long");

        var snapshots = new Dictionary<string, MarketSnapshot>
        {
            ["BTC/USD"] = new() { Asset = "BTC/USD", Bid = 50_000m, Ask = 50_010m, Timestamp = DateTimeOffset.UtcNow },
            ["ETH/USD"] = new() { Asset = "ETH/USD", Bid = 3_000m, Ask = 3_001m, Timestamp = DateTimeOffset.UtcNow }
        };

        await sut.EvaluateAsync(snapshots, "paper");

        await _exchange.Received(2).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());

        _eventLogger.Events.Count(e =>
            e.EventType == EventTypes.ExitTriggered &&
            e.Data!["reason"]!.ToString() == "exit_all")
            .Should().Be(2);
    }

    // ── duplicate close prevention ──────────────────────────────────────────

    [Fact]
    public async Task DuplicateClosePrevention_TwoTicksDoNotSubmitTwoCloseOrders()
    {
        var stratPos = new StrategyPosition
        {
            Id = "sp-1",
            Asset = "BTC/USD",
            Direction = "long",
            AllocationPct = 0.1m,
            EntryType = "market",
            StopLoss = new StopLoss { Type = "hard", Price = 40_000m }
        };

        var (svc, router, sut) = Create();
        await LoadStrategyAsync(svc, MakeStrategy(positions: [stratPos]));
        CreateOpenPosition(stratPositionId: "sp-1", direction: "long");

        var snap = Snap(bid: 39_000m, ask: 39_010m);

        // Simulate two rapid ticks
        await Task.WhenAll(
            sut.EvaluateAsync(snap, "paper"),
            sut.EvaluateAsync(snap, "paper"));

        // Close order should be dispatched only once
        await _exchange.Received(1).PlaceOrderAsync(
            Arg.Is<PlaceOrderRequest>(r => r.Side == OrderSide.Sell),
            Arg.Any<CancellationToken>());
    }
}

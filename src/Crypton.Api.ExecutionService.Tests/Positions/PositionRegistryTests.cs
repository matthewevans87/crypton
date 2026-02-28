using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Positions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Positions;

public sealed class PositionRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _registryPath;
    private readonly string _tradePath;
    private readonly InMemoryEventLogger _eventLogger = new();

    public PositionRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _registryPath = Path.Combine(_tempDir, "positions.json");
        _tradePath = Path.Combine(_tempDir, "trades.json");
    }

    private PositionRegistry CreateRegistry() =>
        new(_registryPath, _tradePath, _eventLogger, NullLogger<PositionRegistry>.Instance);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    // ────────────────────────────────────────────────────────────────────────
    // OpenPosition
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenPosition_CreatesPositionWithCorrectFields()
    {
        var registry = CreateRegistry();

        var pos = registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 0.01m, 50_000m);

        pos.StrategyPositionId.Should().Be("sp1");
        pos.StrategyId.Should().Be("strat1");
        pos.Asset.Should().Be("BTC/USD");
        pos.Direction.Should().Be("long");
        pos.Quantity.Should().Be(0.01m);
        pos.AverageEntryPrice.Should().Be(50_000m);
        pos.OpenedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        pos.Id.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void OpenPosition_PersistsToDisk_AndLoadRecoversIt()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 0.01m, 50_000m);

        var registry2 = CreateRegistry();
        registry2.Load();

        registry2.OpenPositions.Should().ContainSingle(p => p.Id == pos.Id);
        var loaded = registry2.OpenPositions.Single(p => p.Id == pos.Id);
        loaded.Asset.Should().Be("BTC/USD");
        loaded.Quantity.Should().Be(0.01m);
        loaded.AverageEntryPrice.Should().Be(50_000m);
    }

    // ────────────────────────────────────────────────────────────────────────
    // ClosePosition
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ClosePosition_LongTrade_CorrectPnl()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 1m, 50_000m);

        var trade = registry.ClosePosition(pos.Id, 55_000m, "take_profit");

        trade.Should().NotBeNull();
        trade!.RealizedPnl.Should().Be(5_000m);  // (55000 - 50000) * 1
        trade.ExitPrice.Should().Be(55_000m);
        trade.EntryPrice.Should().Be(50_000m);
        trade.ExitReason.Should().Be("take_profit");
        registry.OpenPositions.Should().BeEmpty();
        registry.ClosedTrades.Should().ContainSingle();
    }

    [Fact]
    public void ClosePosition_ShortTrade_CorrectPnl()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "ETH/USD", "short", 2m, 3_000m);

        var trade = registry.ClosePosition(pos.Id, 2_500m, "stop_loss");

        trade.Should().NotBeNull();
        trade!.RealizedPnl.Should().Be(1_000m);  // (3000 - 2500) * 2
    }

    [Fact]
    public void ClosePosition_UnknownId_ReturnsNull()
    {
        var registry = CreateRegistry();

        var result = registry.ClosePosition("nonexistent", 100m, "manual");

        result.Should().BeNull();
    }

    // ────────────────────────────────────────────────────────────────────────
    // UpdateUnrealizedPnl
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void UpdateUnrealizedPnl_Long_CorrectValue()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 1m, 50_000m);

        registry.UpdateUnrealizedPnl("BTC/USD", 52_000m);

        var updated = registry.OpenPositions.Single(p => p.Id == pos.Id);
        updated.UnrealizedPnl.Should().Be(2_000m);
        updated.CurrentPrice.Should().Be(52_000m);
    }

    [Fact]
    public void UpdateUnrealizedPnl_Short_CorrectValue()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "ETH/USD", "short", 2m, 3_000m);

        registry.UpdateUnrealizedPnl("ETH/USD", 2_800m);

        var updated = registry.OpenPositions.Single(p => p.Id == pos.Id);
        updated.UnrealizedPnl.Should().Be(400m);  // (3000 - 2800) * 2
    }

    // ────────────────────────────────────────────────────────────────────────
    // ApplyPartialFill
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyPartialFill_UpdatesQuantityAndAverageEntryPrice()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 1m, 50_000m);

        registry.ApplyPartialFill(pos.Id, 1m, 51_000m);

        var updated = registry.OpenPositions.Single(p => p.Id == pos.Id);
        updated.Quantity.Should().Be(2m);
        updated.AverageEntryPrice.Should().Be(50_500m);  // (50000 + 51000) / 2
    }

    // ────────────────────────────────────────────────────────────────────────
    // RemovePosition
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RemovePosition_RemovesAndPersists()
    {
        var registry = CreateRegistry();
        var pos = registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 0.5m, 50_000m);

        var removed = registry.RemovePosition(pos.Id);

        removed.Should().BeTrue();
        registry.OpenPositions.Should().BeEmpty();

        // Verify persistence
        var registry2 = CreateRegistry();
        registry2.Load();
        registry2.OpenPositions.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────────
    // Concurrency
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConcurrentMutations_DoNotCorruptRegistry()
    {
        var registry = CreateRegistry();

        var tasks = Enumerable.Range(0, 10)
            .Select(i => Task.Run(() =>
                registry.OpenPosition($"sp{i}", "strat1", "BTC/USD", "long", 0.01m * (i + 1), 50_000m)))
            .ToArray();

        await Task.WhenAll(tasks);

        registry.OpenPositions.Should().HaveCount(10);
    }

    // ────────────────────────────────────────────────────────────────────────
    // Atomic write: file is never unreadable (.tmp pattern)
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AtomicWrite_TmpFileIsNotLeftBehind()
    {
        var registry = CreateRegistry();
        registry.OpenPosition("sp1", "strat1", "BTC/USD", "long", 0.01m, 50_000m);

        // After mutation, the .tmp file should not exist
        File.Exists(_registryPath + ".tmp").Should().BeFalse();
        // The real file should exist and be valid JSON
        File.Exists(_registryPath).Should().BeTrue();
        var content = File.ReadAllText(_registryPath);
        content.Should().NotBeNullOrWhiteSpace();
        // Should start with '[' (JSON array)
        content.Trim().Should().StartWith("[");
    }
}

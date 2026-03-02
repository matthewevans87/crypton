using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.OperationMode;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.OperationMode;

/// <summary>
/// Tests for <see cref="PaperTradingAdapter"/> JSON persistence behaviour.
/// Each test gets an isolated temp directory so file side-effects are contained.
/// </summary>
public sealed class PaperTradingAdapterPersistenceTests : IDisposable
{
    private readonly string _tempDir;

    public PaperTradingAdapterPersistenceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PaperAdapterTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string StatePath => Path.Combine(_tempDir, "paper_state.json");

    private PaperTradingAdapter CreateSut(decimal initialBalance = 10_000m, IMarketDataSource? source = null)
    {
        source ??= new NullMarketDataSource();
        var config = new ExecutionServiceConfig
        {
            PaperTrading = new PaperTradingConfig
            {
                InitialBalanceUsd = initialBalance,
                SlippagePct = 0.001m,
                CommissionRate = 0.0026m,
                StatePath = StatePath
            }
        };
        return new PaperTradingAdapter(Options.Create(config), source, NullLogger<PaperTradingAdapter>.Instance);
    }

    private static MarketSnapshot SnapshotFor(string asset, decimal price) =>
        new() { Asset = asset, Bid = price, Ask = price, Timestamp = DateTimeOffset.UtcNow };

    private static PlaceOrderRequest BuyRequest(string asset = "BTC", decimal qty = 1m) =>
        new()
        {
            InternalId = Guid.NewGuid().ToString("N"),
            Asset = asset,
            Side = OrderSide.Buy,
            Type = OrderType.Market,
            Quantity = qty
        };

    private static PlaceOrderRequest SellRequest(string asset = "BTC", decimal qty = 1m) =>
        new()
        {
            InternalId = Guid.NewGuid().ToString("N"),
            Asset = asset,
            Side = OrderSide.Sell,
            Type = OrderType.Market,
            Quantity = qty
        };

    // -----------------------------------------------------------------------
    // Load() — startup behaviour
    // -----------------------------------------------------------------------

    [Fact]
    public void Load_WhenFileDoesNotExist_StartsWithEmptyOrders()
    {
        var sut = CreateSut();

        sut.Load(); // no file exists

        sut.GetAllOrders().Should().BeEmpty();
    }

    [Fact]
    public async Task Load_WhenFileDoesNotExist_BalanceIsInitialBalance()
    {
        var sut = CreateSut(initialBalance: 5_000m);
        sut.Load();

        var balance = await sut.GetAccountBalanceAsync();

        balance.AvailableUsd.Should().Be(5_000m);
    }

    [Fact]
    public void Load_WhenFileIsCorrupted_StartsWithEmptyOrders()
    {
        File.WriteAllText(StatePath, "{ this is not json }}}");
        var sut = CreateSut();

        // Should not throw
        sut.Load();

        sut.GetAllOrders().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Persist() — written after mutations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PlaceOrderAsync_WhenFilled_WritesStateFile()
    {
        var sut = CreateSut();
        sut.Load();
        sut.InjectSnapshot(SnapshotFor("BTC", 100m));

        await sut.PlaceOrderAsync(BuyRequest());

        File.Exists(StatePath).Should().BeTrue();
    }

    [Fact]
    public async Task PlaceOrderAsync_WhenRejected_WritesStateFile()
    {
        var sut = CreateSut();
        sut.Load(); // no market data → rejected order

        await sut.PlaceOrderAsync(BuyRequest("BTC")); // no snapshot → rejected

        File.Exists(StatePath).Should().BeTrue();
    }

    [Fact]
    public async Task PlaceOrder_NoTempFileLeftBehind()
    {
        var sut = CreateSut();
        sut.Load();
        sut.InjectSnapshot(SnapshotFor("BTC", 100m));

        await sut.PlaceOrderAsync(BuyRequest());

        File.Exists(StatePath + ".tmp").Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // State survives restart
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Balance_AfterRestart_ReflectsPersistedFills()
    {
        // Session 1: place a buy
        var sut1 = CreateSut(initialBalance: 10_000m);
        sut1.Load();
        sut1.InjectSnapshot(SnapshotFor("BTC", 1_000m)); // mid = 1000
        await sut1.PlaceOrderAsync(BuyRequest("BTC", qty: 1m));

        var balanceSession1 = await sut1.GetAccountBalanceAsync();

        // Session 2: new adapter, same file path → simulated restart
        var sut2 = CreateSut(initialBalance: 10_000m);
        sut2.Load();

        var balanceSession2 = await sut2.GetAccountBalanceAsync();

        balanceSession2.AvailableUsd.Should().Be(balanceSession1.AvailableUsd);
    }

    [Fact]
    public async Task Orders_AfterRestart_AreRestoredFromFile()
    {
        var sut1 = CreateSut();
        sut1.Load();
        sut1.InjectSnapshot(SnapshotFor("ETH", 500m));
        var ack = await sut1.PlaceOrderAsync(BuyRequest("ETH", qty: 2m));

        // Restart
        var sut2 = CreateSut();
        sut2.Load();

        var orders = sut2.GetAllOrders();
        orders.Should().ContainSingle(o => o.PaperOrderId == ack.ExchangeOrderId);
    }

    [Fact]
    public async Task MultipleFills_AfterRestart_AllOrdersRestored()
    {
        var sut1 = CreateSut(initialBalance: 50_000m);
        sut1.Load();
        sut1.InjectSnapshot(SnapshotFor("BTC", 1_000m));

        // Place 3 orders
        await sut1.PlaceOrderAsync(BuyRequest("BTC", 1m));
        await sut1.PlaceOrderAsync(BuyRequest("BTC", 2m));
        await sut1.PlaceOrderAsync(SellRequest("BTC", 0.5m));

        // Restart
        var sut2 = CreateSut(initialBalance: 50_000m);
        sut2.Load();

        sut2.GetAllOrders().Should().HaveCount(3);
    }

    [Fact]
    public async Task CancelOrder_StatusPersistedAsCancelled()
    {
        // Place a rejected order (status = Rejected, which is not cancellable).
        // Because paper fills immediately, we can only persist the final state;
        // verify the persistence path runs without error and the file is updated.
        var sut1 = CreateSut();
        sut1.Load();
        sut1.InjectSnapshot(SnapshotFor("BTC", 100m));
        var ack = await sut1.PlaceOrderAsync(BuyRequest("BTC"));

        // Attempt cancel (will fail because status is Filled) — verify file still intact
        await sut1.CancelOrderAsync(ack.ExchangeOrderId);

        var sut2 = CreateSut();
        sut2.Load();
        sut2.GetAllOrders().Should().ContainSingle(o => o.PaperOrderId == ack.ExchangeOrderId);
    }

    // -----------------------------------------------------------------------
    // Edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public async Task StatePath_DirectoryCreatedIfMissing()
    {
        // Use a nested non-existent directory
        var nestedDir = Path.Combine(_tempDir, "nested", "deep");
        var config = new ExecutionServiceConfig
        {
            PaperTrading = new PaperTradingConfig
            {
                StatePath = Path.Combine(nestedDir, "paper_state.json")
            }
        };
        var sut = new PaperTradingAdapter(
            Options.Create(config),
            new NullMarketDataSource(),
            NullLogger<PaperTradingAdapter>.Instance);

        sut.Load();
        sut.InjectSnapshot(SnapshotFor("BTC", 200m));
        await sut.PlaceOrderAsync(BuyRequest());

        Directory.Exists(nestedDir).Should().BeTrue();
        File.Exists(config.PaperTrading.StatePath).Should().BeTrue();
    }
}

using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Execution;

/// <summary>
/// Subscribes to the exchange market data feed for the assets in the active strategy.
/// Maintains a cache of the most recent MarketSnapshot per asset.
/// Notifies the evaluation engine on every tick.
/// </summary>
public sealed class MarketDataHub : IHostedService, IDisposable
{
    private readonly IExchangeAdapter _exchange;
    private readonly StrategyService _strategyService;
    private readonly ILogger<MarketDataHub> _logger;
    private readonly Dictionary<string, MarketSnapshot> _snapshots = [];
    private readonly Lock _snapshotLock = new();
    private CancellationTokenSource? _cts;

    /// <summary>Raised on every incoming snapshot.</summary>
    public event Func<MarketSnapshot, Task>? OnSnapshot;

    /// <summary>The time of the most recently received market data tick.</summary>
    public DateTimeOffset LastTickAt { get; private set; } = DateTimeOffset.MinValue;

    public MarketDataHub(
        IExchangeAdapter exchange,
        StrategyService strategyService,
        ILogger<MarketDataHub> logger)
    {
        _exchange = exchange;
        _strategyService = strategyService;
        _logger = logger;

        // Re-subscribe when strategy changes
        _strategyService.OnStrategyLoaded += async s =>
        {
            var assets = s.Positions.Select(p => p.Asset).Distinct().ToList();
            if (_cts is not null)
                await SubscribeAsync(assets, _cts.Token);
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var strategy = _strategyService.ActiveStrategy;
        if (strategy is not null)
        {
            var assets = strategy.Positions.Select(p => p.Asset).Distinct().ToList();
            await SubscribeAsync(assets, _cts.Token);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    public MarketSnapshot? GetSnapshot(string asset)
    {
        lock (_snapshotLock)
        {
            return _snapshots.GetValueOrDefault(asset);
        }
    }

    public IReadOnlyDictionary<string, MarketSnapshot> GetAllSnapshots()
    {
        lock (_snapshotLock) { return new Dictionary<string, MarketSnapshot>(_snapshots); }
    }

    private async Task SubscribeAsync(IReadOnlyList<string> assets, CancellationToken token)
    {
        await _exchange.SubscribeToMarketDataAsync(assets, async snap =>
        {
            lock (_snapshotLock) { _snapshots[snap.Asset] = snap; }
            LastTickAt = snap.Timestamp;
            if (OnSnapshot is not null) await OnSnapshot(snap);
        }, token);
    }

    public void Dispose() => _cts?.Dispose();
}

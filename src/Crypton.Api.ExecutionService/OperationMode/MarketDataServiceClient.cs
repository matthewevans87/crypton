using System.Collections.Concurrent;
using Crypton.Api.ExecutionService.Models;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Connects to the Market Data Service's SignalR hub and forwards price ticks
/// as <see cref="MarketSnapshot"/> objects to any registered subscribers.
///
/// Implements both <see cref="IMarketDataSource"/> (consumed by <see cref="PaperTradingAdapter"/>
/// and <see cref="ExecutionEngine"/> for condition evaluation) and
/// <see cref="IHostedService"/> (started and stopped by the generic host).
///
/// Reconnection is handled automatically by SignalR's built-in retry policy.
/// Subscribers register via <see cref="SubscribeAsync"/> and receive callbacks on every tick.
/// </summary>
public sealed class MarketDataServiceClient : IMarketDataSource, IHostedService, IAsyncDisposable
{
    private readonly string _hubUrl;
    private readonly ILogger<MarketDataServiceClient> _logger;
    private HubConnection? _hub;

    // Per-asset lists of callbacks. Keys are normalised to UPPER-CASE.
    private readonly ConcurrentDictionary<string, List<Func<MarketSnapshot, Task>>> _subscribers =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Lock _subscriberLock = new();

    public MarketDataServiceClient(string hubUrl, ILogger<MarketDataServiceClient> logger)
    {
        _hubUrl = hubUrl;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IMarketDataSource
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task SubscribeAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> callback,
        CancellationToken cancellationToken = default)
    {
        lock (_subscriberLock)
        {
            foreach (var asset in assets)
            {
                var key = asset.ToUpperInvariant();
                if (!_subscribers.TryGetValue(key, out var list))
                {
                    list = [];
                    _subscribers[key] = list;
                }
                list.Add(callback);
            }
        }
        return Task.CompletedTask;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IHostedService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl($"{_hubUrl}/hubs/marketdata")
            .WithAutomaticReconnect([
                TimeSpan.Zero,
                TimeSpan.FromSeconds(2),
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(15),
                TimeSpan.FromSeconds(30)
            ])
            .Build();

        _hub.On<PriceTickerDto>("OnPriceUpdate", async ticker =>
        {
            var snapshot = new MarketSnapshot
            {
                Asset = ticker.Asset,
                Bid = ticker.Bid,
                Ask = ticker.Ask,
                Timestamp = ticker.LastUpdated == default
                    ? DateTimeOffset.UtcNow
                    : new DateTimeOffset(ticker.LastUpdated, TimeSpan.Zero)
            };

            var key = ticker.Asset.ToUpperInvariant();
            List<Func<MarketSnapshot, Task>>? callbacks;
            lock (_subscriberLock)
            {
                _subscribers.TryGetValue(key, out callbacks);
            }

            if (callbacks is null) return;

            // Snapshot the list to avoid holding the lock during async callbacks.
            List<Func<MarketSnapshot, Task>> snapshot2;
            lock (_subscriberLock) { snapshot2 = [.. callbacks]; }

            foreach (var cb in snapshot2)
            {
                try
                {
                    await cb(snapshot);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Market data subscriber callback threw for {Asset}", ticker.Asset);
                }
            }
        });

        _hub.Reconnecting += ex =>
        {
            _logger.LogWarning(ex, "Market data hub reconnecting…");
            return Task.CompletedTask;
        };

        _hub.Reconnected += id =>
        {
            _logger.LogInformation("Market data hub reconnected. ConnectionId={Id}", id);
            return Task.CompletedTask;
        };

        _hub.Closed += ex =>
        {
            _logger.LogError(ex, "Market data hub connection closed permanently.");
            return Task.CompletedTask;
        };

        try
        {
            await _hub.StartAsync(cancellationToken);
            _logger.LogInformation("Connected to market data hub at {Url}", _hubUrl);
        }
        catch (Exception ex)
        {
            // Don't throw — SignalR will retry automatically once the service becomes available.
            _logger.LogWarning(ex,
                "Could not connect to market data hub at {Url} on startup. Will retry.", _hubUrl);
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_hub is not null)
        {
            try { await _hub.StopAsync(cancellationToken); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping market data hub connection.");
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
        {
            await _hub.DisposeAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private DTO: must match the shape of PriceTicker from the MarketData service
    // ─────────────────────────────────────────────────────────────────────────

    private sealed class PriceTickerDto
    {
        public string Asset { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Change24h { get; set; }
        public decimal ChangePercent24h { get; set; }
        public decimal High24h { get; set; }
        public decimal Low24h { get; set; }
        public decimal Volume24h { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}

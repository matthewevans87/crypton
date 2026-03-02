using System.Text.Json;
using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Simulated exchange adapter for paper trading.
/// Orders are immediately filled at mid ± slippage using the latest received market snapshot.
/// State (all orders) is persisted to <see cref="PaperTradingConfig.StatePath"/> after every
/// mutation so balance and fill history survive process restarts.
/// </summary>
public sealed class PaperTradingAdapter : IExchangeAdapter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    private readonly PaperTradingConfig _config;
    private readonly IMarketDataSource _marketDataSource;
    private readonly ILogger<PaperTradingAdapter> _logger;

    // Keyed by paper order-id
    private readonly Dictionary<string, PaperOrder> _orders = new(StringComparer.Ordinal);
    private readonly Lock _ordersLock = new();

    // Latest snapshot per asset
    private readonly Dictionary<string, MarketSnapshot> _lastSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _snapshotLock = new();

    public PaperTradingAdapter(
        IOptions<ExecutionServiceConfig> config,
        IMarketDataSource marketDataSource,
        ILogger<PaperTradingAdapter> logger)
    {
        _config = config.Value.PaperTrading;
        _marketDataSource = marketDataSource;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Persistence
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads previously persisted paper orders from <see cref="PaperTradingConfig.StatePath"/>.
    /// Call once at startup (before accepting any orders). Safe to call on a missing file.
    /// </summary>
    public void Load()
    {
        var path = _config.StatePath;
        if (!File.Exists(path))
        {
            _logger.LogInformation("Paper trading state file not found at {Path}. Starting with empty state.", path);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var orders = JsonSerializer.Deserialize<List<PaperOrder>>(json, JsonOpts);
            if (orders is null)
            {
                _logger.LogWarning("Paper trading state at {Path} deserialized to null. Starting with empty state.", path);
                return;
            }

            lock (_ordersLock)
            {
                _orders.Clear();
                foreach (var order in orders)
                    _orders[order.PaperOrderId] = order;
            }

            _logger.LogInformation("Loaded {Count} paper orders from {Path}.", orders.Count, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load paper trading state from {Path}. Starting with empty state.", path);
        }
    }

    /// <summary>Atomically writes the current order dictionary to <see cref="PaperTradingConfig.StatePath"/>.</summary>
    private void Persist()
    {
        var path = _config.StatePath;
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = path + ".tmp";
        try
        {
            List<PaperOrder> snapshot;
            lock (_ordersLock)
            {
                snapshot = _orders.Values.ToList();
            }

            File.WriteAllText(tmp, JsonSerializer.Serialize(snapshot, JsonOpts));
            File.Move(tmp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist paper trading state to {Path}.", path);
        }
    }

    // -------------------------------------------------------------------------
    // IExchangeAdapter — rate limit (never rate-limited in paper mode)
    // -------------------------------------------------------------------------

    public bool IsRateLimited => false;
    public DateTimeOffset? RateLimitResumesAt => null;

    // -------------------------------------------------------------------------
    // Market data
    // -------------------------------------------------------------------------

    public async Task SubscribeToMarketDataAsync(
        IReadOnlyList<string> assets,
        Func<MarketSnapshot, Task> onSnapshot,
        CancellationToken cancellationToken = default)
    {
        // Wrap the caller's callback so we capture each snapshot internally before forwarding.
        Func<MarketSnapshot, Task> wrapping = snap =>
        {
            lock (_snapshotLock)
            {
                _lastSnapshots[snap.Asset] = snap;
            }
            return onSnapshot(snap);
        };

        await _marketDataSource.SubscribeAsync(assets, wrapping, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Order management
    // -------------------------------------------------------------------------

    public Task<OrderAcknowledgement> PlaceOrderAsync(
        PlaceOrderRequest request,
        CancellationToken cancellationToken = default)
    {
        MarketSnapshot? snap;
        lock (_snapshotLock)
        {
            _lastSnapshots.TryGetValue(request.Asset, out snap);
        }

        if (snap is null)
        {
            // No market data → reject
            var rejected = new PaperOrder
            {
                PaperOrderId = "paper_" + Guid.NewGuid().ToString("N"),
                InternalId = request.InternalId,
                Asset = request.Asset,
                Side = request.Side,
                Type = request.Type,
                Quantity = request.Quantity,
                Status = OrderStatus.Rejected,
                RejectionReason = "no_market_data",
                CreatedAt = DateTimeOffset.UtcNow
            };

            lock (_ordersLock)
            {
                _orders[rejected.PaperOrderId] = rejected;
            }
            Persist();

            return Task.FromResult(new OrderAcknowledgement
            {
                InternalId = request.InternalId,
                ExchangeOrderId = rejected.PaperOrderId,
                Timestamp = rejected.CreatedAt
            });
        }

        // Compute fill price with slippage
        var mid = snap.Mid;
        var fillPrice = request.Side == OrderSide.Buy
            ? mid * (1m + _config.SlippagePct)
            : mid * (1m - _config.SlippagePct);

        var commission = request.Quantity * fillPrice * _config.CommissionRate;

        var paperId = "paper_" + Guid.NewGuid().ToString("N");
        var filled = new PaperOrder
        {
            PaperOrderId = paperId,
            InternalId = request.InternalId,
            Asset = request.Asset,
            Side = request.Side,
            Type = request.Type,
            Quantity = request.Quantity,
            Status = OrderStatus.Filled,
            FilledQuantity = request.Quantity,
            AverageFillPrice = fillPrice,
            Commission = commission,
            CreatedAt = DateTimeOffset.UtcNow
        };

        lock (_ordersLock)
        {
            _orders[paperId] = filled;
        }
        Persist();

        return Task.FromResult(new OrderAcknowledgement
        {
            InternalId = request.InternalId,
            ExchangeOrderId = paperId,
            Timestamp = filled.CreatedAt
        });
    }

    public Task<CancellationResult> CancelOrderAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        lock (_ordersLock)
        {
            if (!_orders.TryGetValue(exchangeOrderId, out var order))
            {
                return Task.FromResult(new CancellationResult
                {
                    ExchangeOrderId = exchangeOrderId,
                    Success = false,
                    ErrorMessage = "order_not_found"
                });
            }

            if (order.Status is OrderStatus.Open or OrderStatus.PartiallyFilled)
            {
                order.Status = OrderStatus.Cancelled;
                Persist();
                return Task.FromResult(new CancellationResult
                {
                    ExchangeOrderId = exchangeOrderId,
                    Success = true
                });
            }

            return Task.FromResult(new CancellationResult
            {
                ExchangeOrderId = exchangeOrderId,
                Success = false,
                ErrorMessage = $"cannot_cancel_order_in_status_{order.Status.ToString().ToLowerInvariant()}"
            });
        }
    }

    public Task<OrderStatusResult> GetOrderStatusAsync(
        string exchangeOrderId,
        CancellationToken cancellationToken = default)
    {
        PaperOrder? order;
        lock (_ordersLock)
        {
            _orders.TryGetValue(exchangeOrderId, out order);
        }

        if (order is null)
            throw new OrderNotFoundException(exchangeOrderId);

        return Task.FromResult(new OrderStatusResult
        {
            ExchangeOrderId = exchangeOrderId,
            Status = order.Status,
            FilledQuantity = order.FilledQuantity,
            AverageFillPrice = order.AverageFillPrice,
            RejectionReason = order.RejectionReason
        });
    }

    // -------------------------------------------------------------------------
    // Account / positions
    // -------------------------------------------------------------------------

    public Task<AccountBalance> GetAccountBalanceAsync(
        CancellationToken cancellationToken = default)
    {
        decimal spent;
        lock (_ordersLock)
        {
            spent = _orders.Values
                .Where(o => o.Status == OrderStatus.Filled && o.Side == OrderSide.Buy)
                .Sum(o => o.FilledQuantity * (o.AverageFillPrice ?? 0m) + o.Commission);

            var sells = _orders.Values
                .Where(o => o.Status == OrderStatus.Filled && o.Side == OrderSide.Sell)
                .Sum(o => o.FilledQuantity * (o.AverageFillPrice ?? 0m) - o.Commission);

            spent -= sells;
        }

        var available = _config.InitialBalanceUsd - spent;
        return Task.FromResult(new AccountBalance
        {
            AvailableUsd = available,
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    public Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(
        CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ExchangePosition>>([]);

    public Task<IReadOnlyList<Trade>> GetTradeHistoryAsync(
        DateTimeOffset since,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Trade> trades;
        lock (_ordersLock)
        {
            trades = _orders.Values
                .Where(o => o.Status == OrderStatus.Filled && o.CreatedAt >= since)
                .Select(o => new Trade
                {
                    ExchangeTradeId = o.PaperOrderId,
                    Asset = o.Asset,
                    Side = o.Side,
                    Quantity = o.FilledQuantity,
                    Price = o.AverageFillPrice ?? 0m,
                    Timestamp = o.CreatedAt,
                    Fee = o.Commission
                })
                .ToList();
        }
        return Task.FromResult(trades);
    }

    // -------------------------------------------------------------------------
    // Internal snapshot helper used by tests
    // -------------------------------------------------------------------------

    internal void InjectSnapshot(MarketSnapshot snapshot)
    {
        lock (_snapshotLock)
        {
            _lastSnapshots[snapshot.Asset] = snapshot;
        }
    }

    /// <summary>Exposes all orders for test assertions.</summary>
    internal IReadOnlyList<PaperOrder> GetAllOrders()
    {
        lock (_ordersLock)
        {
            return _orders.Values.ToList();
        }
    }

    // -------------------------------------------------------------------------
    // Internal order model
    // -------------------------------------------------------------------------

    internal sealed class PaperOrder
    {
        public required string PaperOrderId { get; set; }
        public required string InternalId { get; set; }
        public required string Asset { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public required OrderSide Side { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public required OrderType Type { get; set; }
        public required decimal Quantity { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public OrderStatus Status { get; set; }
        public decimal FilledQuantity { get; set; }
        public decimal? AverageFillPrice { get; set; }
        public decimal Commission { get; set; }
        public string? RejectionReason { get; set; }
        public required DateTimeOffset CreatedAt { get; set; }
    }
}

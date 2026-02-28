using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Models;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Simulated exchange adapter for paper trading.
/// Orders are immediately filled at mid ± slippage using the latest received market snapshot.
/// </summary>
public sealed class PaperTradingAdapter : IExchangeAdapter
{
    private readonly PaperTradingConfig _config;
    private readonly IMarketDataSource _marketDataSource;

    // Keyed by paper order-id
    private readonly Dictionary<string, PaperOrder> _orders = new(StringComparer.Ordinal);
    private readonly Lock _ordersLock = new();

    // Latest snapshot per asset
    private readonly Dictionary<string, MarketSnapshot> _lastSnapshots = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _snapshotLock = new();

    public PaperTradingAdapter(
        IOptions<ExecutionServiceConfig> config,
        IMarketDataSource marketDataSource)
    {
        _config = config.Value.PaperTrading;
        _marketDataSource = marketDataSource;
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

    // -------------------------------------------------------------------------
    // Private order model
    // -------------------------------------------------------------------------

    private sealed class PaperOrder
    {
        public required string PaperOrderId { get; init; }
        public required string InternalId { get; init; }
        public required string Asset { get; init; }
        public required OrderSide Side { get; init; }
        public required OrderType Type { get; init; }
        public required decimal Quantity { get; init; }
        public OrderStatus Status { get; set; }
        public decimal FilledQuantity { get; init; }
        public decimal? AverageFillPrice { get; init; }
        public decimal Commission { get; init; }
        public string? RejectionReason { get; init; }
        public required DateTimeOffset CreatedAt { get; init; }
    }
}

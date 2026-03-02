using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Metrics;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Orders;

/// <summary>
/// Accepts order requests, dispatches them to the exchange adapter, and manages
/// the full lifecycle of each order (placed → pending → filled/cancelled/rejected).
///
/// After every successful placement the adapter is immediately queried once for fill
/// status. This ensures paper-mode orders (which fill synchronously) are fully
/// reconciled without a separate polling loop. For live orders that fill later, call
/// <see cref="ApplyFillAsync"/> from the WebSocket executions channel handler.
/// </summary>
public sealed class OrderRouter
{
    private readonly IExchangeAdapter _exchange;
    private readonly PositionRegistry _positions;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<OrderRouter> _logger;
    private readonly FailureTracker? _failureTracker;
    private readonly IMetricsCollector? _metrics;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, OrderRecord> _orders = [];
    // Reverse lookup: exchange order ID → internal order ID for live WS fill handling.
    private readonly Dictionary<string, string> _exchangeToInternalId = [];

    public OrderRouter(
        IExchangeAdapter exchange,
        PositionRegistry positions,
        IEventLogger eventLogger,
        ILogger<OrderRouter> logger,
        FailureTracker? failureTracker = null,
        IMetricsCollector? metrics = null)
    {
        _exchange = exchange;
        _positions = positions;
        _eventLogger = eventLogger;
        _logger = logger;
        _failureTracker = failureTracker;
        _metrics = metrics;
    }

    public IReadOnlyList<OrderRecord> ActiveOrders
    {
        get { lock (_lock) { return _orders.Values.ToList(); } }
    }

    /// <summary>
    /// Submit an order to open a new position or close an existing one.
    /// Deduplication: will not submit if an active order already exists for the same
    /// <paramref name="strategyPositionId"/>.
    /// </summary>
    public async Task<OrderRecord?> PlaceEntryOrderAsync(
        string asset, OrderSide side, OrderType type, decimal quantity,
        decimal? limitPrice, string strategyPositionId, string mode,
        string strategyId = "",
        CancellationToken token = default)
    {
        // Deduplication check
        lock (_lock)
        {
            var existing = _orders.Values.FirstOrDefault(o =>
                o.StrategyPositionId == strategyPositionId &&
                o.Status is OrderStatus.Pending or OrderStatus.Open or OrderStatus.PartiallyFilled);

            if (existing is not null)
            {
                _logger.LogDebug("Duplicate entry suppressed for position {Id}", strategyPositionId);
                return null;
            }
        }

        var record = new OrderRecord
        {
            InternalId = Guid.NewGuid().ToString("N"),
            Asset = asset,
            Side = side,
            Type = type,
            Quantity = quantity,
            LimitPrice = limitPrice,
            StrategyPositionId = strategyPositionId,
            StrategyId = strategyId,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        lock (_lock) { _orders[record.InternalId] = record; }

        _metrics?.RecordOrderPlaced();

        await _eventLogger.LogAsync(EventTypes.OrderPlaced, mode, new Dictionary<string, object?>
        {
            ["internal_id"] = record.InternalId,
            ["asset"] = asset,
            ["side"] = side.ToString(),
            ["type"] = type.ToString(),
            ["quantity"] = (double)quantity
        }, token);

        try
        {
            var req = new PlaceOrderRequest
            {
                InternalId = record.InternalId,
                Asset = asset,
                Side = side,
                Type = type,
                Quantity = quantity,
                LimitPrice = limitPrice,
                StrategyPositionId = strategyPositionId
            };

            var ack = await _exchange.PlaceOrderAsync(req, token);

            lock (_lock)
            {
                record.ExchangeOrderId = ack.ExchangeOrderId;
                record.Status = OrderStatus.Open;
                record.UpdatedAt = DateTimeOffset.UtcNow;
                if (ack.ExchangeOrderId is not null)
                    _exchangeToInternalId[ack.ExchangeOrderId] = record.InternalId;
            }

            _metrics?.RecordOrderOpened();
            _failureTracker?.RecordSuccess();

            // Immediately poll fill status. Paper-mode fills synchronously; live market orders
            // may also fill before we can poll. If filled, apply now.
            await TryApplyImmediateFillAsync(record, strategyId, mode, token);

            return record;
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                record.Status = OrderStatus.Rejected;
                record.RejectionReason = ex.Message;
                record.UpdatedAt = DateTimeOffset.UtcNow;
            }

            await _eventLogger.LogAsync(EventTypes.OrderRejected, mode, new Dictionary<string, object?>
            {
                ["internal_id"] = record.InternalId,
                ["reason"] = ex.Message
            }, token);

            _metrics?.RecordOrderRejected();
            _failureTracker?.RecordFailure();
            return record;
        }
    }

    /// <summary>
    /// Apply a fill event to an order. Creates or updates the associated position.
    /// Safe to call multiple times for partial fills (idempotent for full fills).
    /// Also invoked from the live WebSocket <c>executions</c> channel handler.
    /// </summary>
    public async Task ApplyFillAsync(OrderFillEvent fill, string strategyId, string mode, CancellationToken token = default)
    {
        OrderRecord? record;
        lock (_lock) { _orders.TryGetValue(fill.InternalOrderId, out record); }

        if (record is null)
        {
            _logger.LogWarning("Fill arrived for unknown internal order {Id}", fill.InternalOrderId);
            return;
        }

        // Idempotency: ignore fills for orders already fully reconciled.
        lock (_lock)
        {
            if (record.Status == OrderStatus.Filled) return;
        }

        var effectiveStrategyId = string.IsNullOrEmpty(strategyId) ? record.StrategyId ?? string.Empty : strategyId;
        var eventType = fill.IsFullFill ? EventTypes.OrderFilled : EventTypes.OrderPartiallyFilled;

        lock (_lock)
        {
            var totalFilled = record.FilledQuantity + fill.FilledQuantity;
            var prevFilled = record.FilledQuantity;
            var prevAvg = record.AverageFillPrice ?? 0m;
            record.AverageFillPrice = totalFilled > 0
                ? (prevFilled * prevAvg + fill.FilledQuantity * fill.FillPrice) / totalFilled
                : fill.FillPrice;
            record.FilledQuantity = totalFilled;
            record.Status = fill.IsFullFill ? OrderStatus.Filled : OrderStatus.PartiallyFilled;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _eventLogger.LogAsync(eventType, mode, new Dictionary<string, object?>
        {
            ["internal_id"] = fill.InternalOrderId,
            ["exchange_order_id"] = fill.ExchangeOrderId,
            ["filled_quantity"] = (double)fill.FilledQuantity,
            ["fill_price"] = (double)fill.FillPrice
        }, token);

        if (fill.IsFullFill)
            _metrics?.RecordOrderFilled();

        // Open a position for both long (Buy) and short (Sell) full fills.
        if (fill.IsFullFill && record.StrategyPositionId is not null)
        {
            var direction = record.Side == OrderSide.Buy ? "long" : "short";
            var pos = _positions.OpenPosition(
                record.StrategyPositionId,
                effectiveStrategyId,
                record.Asset,
                direction,
                fill.FilledQuantity,
                fill.FillPrice);
            lock (_lock) { record.PositionId = pos.Id; }
            _metrics?.RecordPositionOpened();
        }
    }

    /// <summary>Cancel an open order.</summary>
    public async Task CancelOrderAsync(string internalId, string mode, CancellationToken token = default)
    {
        OrderRecord? record;
        lock (_lock) { _orders.TryGetValue(internalId, out record); }
        if (record?.ExchangeOrderId is null) return;

        var result = await _exchange.CancelOrderAsync(record.ExchangeOrderId, token);

        lock (_lock)
        {
            record.Status = result.Success ? OrderStatus.Cancelled : record.Status;
            record.UpdatedAt = DateTimeOffset.UtcNow;
        }

        if (result.Success)
        {
            await _eventLogger.LogAsync(EventTypes.OrderCancelled, mode,
                new Dictionary<string, object?> { ["internal_id"] = internalId }, token);
            _metrics?.RecordOrderClosed();
        }
    }

    /// <summary>
    /// Apply a fill event received from the live WebSocket executions channel,
    /// correlating by the exchange-assigned order ID.
    /// </summary>
    public async Task ApplyFillByExchangeOrderIdAsync(
        string exchangeOrderId,
        decimal filledQty,
        decimal fillPrice,
        bool isFullFill,
        DateTimeOffset timestamp,
        string mode,
        CancellationToken token = default)
    {
        string? internalId;
        lock (_lock) { _exchangeToInternalId.TryGetValue(exchangeOrderId, out internalId); }

        if (internalId is null)
        {
            _logger.LogWarning(
                "WS fill arrived for untracked exchange order {ExchangeOrderId} — ignoring.",
                exchangeOrderId);
            return;
        }

        await ApplyFillAsync(new OrderFillEvent
        {
            ExchangeOrderId = exchangeOrderId,
            InternalOrderId = internalId,
            FilledQuantity = filledQty,
            FillPrice = fillPrice,
            Timestamp = timestamp,
            IsFullFill = isFullFill
        }, string.Empty, mode, token);
    }

    // -------------------------------------------------------------------------
    // Internal helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Poll the exchange once for fill status immediately after placement.
    /// For paper-mode orders this resolves synchronously; for live market orders that
    /// fill before the network round-trip completes this avoids a polling delay.
    /// Failures are silently ignored — the WS executions channel or reconciliation will catch them.
    /// </summary>
    private async Task TryApplyImmediateFillAsync(
        OrderRecord record, string strategyId, string mode, CancellationToken token)
    {
        if (record.ExchangeOrderId is null) return;

        try
        {
            var status = await _exchange.GetOrderStatusAsync(record.ExchangeOrderId, token);

            if (status.Status is not (OrderStatus.Filled or OrderStatus.PartiallyFilled))
                return;

            await ApplyFillAsync(new OrderFillEvent
            {
                ExchangeOrderId = record.ExchangeOrderId,
                InternalOrderId = record.InternalId,
                FilledQuantity = status.FilledQuantity,
                FillPrice = status.AverageFillPrice ?? 0m,
                Timestamp = DateTimeOffset.UtcNow,
                IsFullFill = status.Status == OrderStatus.Filled
            }, strategyId, mode, token);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Immediate fill check failed for order {Id} — will be reconciled later",
                record.InternalId);
        }
    }
}
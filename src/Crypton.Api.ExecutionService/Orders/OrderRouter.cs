using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Orders;

/// <summary>
/// Accepts order requests, dispatches them to the exchange adapter, and manages
/// the full lifecycle of each order (placed → pending → filled/cancelled/rejected).
/// Implements ES-OM-001.
/// </summary>
public sealed class OrderRouter
{
    private readonly IExchangeAdapter _exchange;
    private readonly PositionRegistry _positions;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<OrderRouter> _logger;
    private readonly FailureTracker? _failureTracker;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, OrderRecord> _orders = [];

    public OrderRouter(
        IExchangeAdapter exchange,
        PositionRegistry positions,
        IEventLogger eventLogger,
        ILogger<OrderRouter> logger,
        FailureTracker? failureTracker = null)
    {
        _exchange = exchange;
        _positions = positions;
        _eventLogger = eventLogger;
        _logger = logger;
        _failureTracker = failureTracker;
    }

    public IReadOnlyList<OrderRecord> ActiveOrders
    {
        get { lock (_lock) { return _orders.Values.ToList(); } }
    }

    /// <summary>
    /// Submit an order to open a new position. Returns the created OrderRecord.
    /// Deduplication: will not submit if an active order already exists for the same strategyPositionId.
    /// </summary>
    public async Task<OrderRecord?> PlaceEntryOrderAsync(
        string asset, OrderSide side, OrderType type, decimal quantity,
        decimal? limitPrice, string strategyPositionId, string mode,
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
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        lock (_lock) { _orders[record.InternalId] = record; }

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
            }

            _failureTracker?.RecordSuccess();
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

            _failureTracker?.RecordFailure();
            return record;
        }
    }

    /// <summary>Apply a fill event to an order. Creates or updates the associated position.</summary>
    public async Task ApplyFillAsync(OrderFillEvent fill, string strategyId, string mode, CancellationToken token = default)
    {
        OrderRecord? record;
        lock (_lock) { _orders.TryGetValue(fill.InternalOrderId, out record); }

        if (record is null)
        {
            _logger.LogWarning("Fill arrived for unknown internal order {Id}", fill.InternalOrderId);
            return;
        }

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

        // Open or update position
        if (record.Side == OrderSide.Buy && fill.IsFullFill && record.StrategyPositionId is not null)
        {
            var pos = _positions.OpenPosition(
                record.StrategyPositionId, strategyId,
                record.Asset, "long", fill.FilledQuantity, fill.FillPrice);
            lock (_lock) { record.PositionId = pos.Id; }
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
            await _eventLogger.LogAsync(EventTypes.OrderCancelled, mode,
                new Dictionary<string, object?> { ["internal_id"] = internalId }, token);
    }
}

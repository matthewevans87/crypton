namespace Crypton.Api.ExecutionService.Models;

public enum OrderSide { Buy, Sell }
public enum OrderType { Market, Limit, StopLoss, StopLossLimit }
public enum OrderStatus { Pending, Open, PartiallyFilled, Filled, Cancelled, Rejected }

public sealed class PlaceOrderRequest
{
    public required string InternalId { get; init; }
    public required string Asset { get; init; }
    public required OrderSide Side { get; init; }
    public required OrderType Type { get; init; }
    public required decimal Quantity { get; init; }
    public decimal? LimitPrice { get; init; }
    public decimal? StopPrice { get; init; }
    public string? StrategyPositionId { get; init; }
}

public sealed class OrderAcknowledgement
{
    public required string InternalId { get; init; }
    public required string ExchangeOrderId { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed class OrderStatusResult
{
    public required string ExchangeOrderId { get; init; }
    public required OrderStatus Status { get; init; }
    public required decimal FilledQuantity { get; init; }
    public decimal? AverageFillPrice { get; init; }
    public string? RejectionReason { get; init; }
}

public sealed class CancellationResult
{
    public required string ExchangeOrderId { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class AccountBalance
{
    /// <summary>Available cash in USD (or base fiat currency).</summary>
    public required decimal AvailableUsd { get; init; }
    /// <summary>Per-asset balance. Key = asset symbol (e.g. "BTC"), Value = quantity held.</summary>
    public IReadOnlyDictionary<string, decimal> AssetBalances { get; init; }
        = new Dictionary<string, decimal>();
    public required DateTimeOffset Timestamp { get; init; }
}

public sealed class ExchangePosition
{
    public required string Asset { get; init; }
    public required string Direction { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal AverageEntryPrice { get; init; }
}

public sealed class Trade
{
    public required string ExchangeTradeId { get; init; }
    public required string Asset { get; init; }
    public required OrderSide Side { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal Price { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required decimal Fee { get; init; }
}

public sealed class OrderFillEvent
{
    public required string ExchangeOrderId { get; init; }
    public required string InternalOrderId { get; init; }
    public required decimal FilledQuantity { get; init; }
    public required decimal FillPrice { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required bool IsFullFill { get; init; }
}

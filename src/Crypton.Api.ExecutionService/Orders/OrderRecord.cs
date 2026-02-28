using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Models;

namespace Crypton.Api.ExecutionService.Orders;

public sealed class OrderRecord
{
    [JsonPropertyName("internal_id")]
    public required string InternalId { get; init; }

    [JsonPropertyName("exchange_order_id")]
    public string? ExchangeOrderId { get; set; }

    [JsonPropertyName("asset")]
    public required string Asset { get; init; }

    [JsonPropertyName("side")]
    public required OrderSide Side { get; init; }

    [JsonPropertyName("type")]
    public required OrderType Type { get; init; }

    [JsonPropertyName("quantity")]
    public required decimal Quantity { get; init; }

    [JsonPropertyName("limit_price")]
    public decimal? LimitPrice { get; init; }

    [JsonPropertyName("status")]
    public OrderStatus Status { get; set; } = OrderStatus.Pending;

    [JsonPropertyName("filled_quantity")]
    public decimal FilledQuantity { get; set; }

    [JsonPropertyName("average_fill_price")]
    public decimal? AverageFillPrice { get; set; }

    [JsonPropertyName("rejection_reason")]
    public string? RejectionReason { get; set; }

    [JsonPropertyName("strategy_position_id")]
    public string? StrategyPositionId { get; init; }

    [JsonPropertyName("position_id")]
    public string? PositionId { get; set; }

    [JsonPropertyName("created_at")]
    public required DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("updated_at")]
    public DateTimeOffset UpdatedAt { get; set; }
}

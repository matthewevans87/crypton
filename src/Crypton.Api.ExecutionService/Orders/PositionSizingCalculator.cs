using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Orders;

/// <summary>
/// Computes the order quantity for a new position entry.
/// Applies allocation percentage, lot-size rounding, and per-position size cap.
/// Implements ES-OM-002.
/// </summary>
public sealed class PositionSizingCalculator
{
    private readonly IExchangeAdapter _exchange;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<PositionSizingCalculator> _logger;

    // Minimum lot sizes per asset (in base currency). For Kraken spot these are well-known.
    // Can be extended via configuration.
    private static readonly Dictionary<string, decimal> MinLotSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC/USD"] = 0.0001m,
        ["ETH/USD"] = 0.001m,
        ["XRP/USD"] = 1m,
        ["SOL/USD"] = 0.01m,
    };

    private static readonly Dictionary<string, decimal> LotIncrements = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC/USD"] = 0.0001m,
        ["ETH/USD"] = 0.001m,
        ["XRP/USD"] = 1m,
        ["SOL/USD"] = 0.01m,
    };

    public PositionSizingCalculator(
        IExchangeAdapter exchange,
        IEventLogger eventLogger,
        ILogger<PositionSizingCalculator> logger)
    {
        _exchange = exchange;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    /// <summary>
    /// Calculate the order quantity for a new position.
    /// Returns null if the position cannot be sized (insufficient capital, below min lot).
    /// </summary>
    public async Task<decimal?> CalculateAsync(
        string asset,
        decimal allocationPct,
        decimal maxPerPositionPct,
        decimal currentPrice,
        string mode,
        CancellationToken token = default)
    {
        var balance = await _exchange.GetAccountBalanceAsync(token);
        var capital = balance.AvailableUsd;

        if (capital <= 0)
        {
            _logger.LogWarning("No available capital for position sizing on {Asset}", asset);
            await _eventLogger.LogAsync(EventTypes.EntrySkipped, mode, new Dictionary<string, object?>
            {
                ["asset"] = asset,
                ["reason"] = "no_available_capital"
            }, token);
            return null;
        }

        var effectivePct = Math.Min(allocationPct, maxPerPositionPct);
        var notional = capital * effectivePct;
        var rawQuantity = notional / currentPrice;

        // Round down to lot increment
        var increment = LotIncrements.GetValueOrDefault(asset, 0.0001m);
        var roundedQuantity = Math.Floor(rawQuantity / increment) * increment;

        // Check minimum lot size
        var minLot = MinLotSizes.GetValueOrDefault(asset, 0.0001m);
        if (roundedQuantity < minLot)
        {
            _logger.LogWarning("Computed quantity {Q} for {Asset} is below minimum lot size {Min}",
                roundedQuantity, asset, minLot);
            await _eventLogger.LogAsync(EventTypes.EntrySkipped, mode, new Dictionary<string, object?>
            {
                ["asset"] = asset,
                ["reason"] = "below_minimum_lot_size",
                ["computed_quantity"] = (double)roundedQuantity,
                ["min_lot"] = (double)minLot
            }, token);
            return null;
        }

        var wasClamped = allocationPct > maxPerPositionPct;

        _logger.LogDebug(
            "Sized {Asset}: capital={Capital} alloc={Alloc}% effective={Eff}% price={Price} qty={Qty}{Clamped}",
            asset, capital, allocationPct, effectivePct, currentPrice, roundedQuantity,
            wasClamped ? " [clamped to max_per_position_pct]" : "");

        return roundedQuantity;
    }
}

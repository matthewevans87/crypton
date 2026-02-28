using Crypton.Api.ExecutionService.Exchange;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Strategy;
using Crypton.Api.ExecutionService.Strategy.Conditions;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Execution;

/// <summary>
/// Monitors all open positions on every tick and triggers exits when conditions are met.
/// Implements ES-CEE-002.
/// </summary>
public sealed class ExitEvaluator
{
    private readonly PositionRegistry _positions;
    private readonly OrderRouter _orderRouter;
    private readonly StrategyService _strategyService;
    private readonly ConditionParser _conditionParser;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<ExitEvaluator> _logger;

    // Track in-flight close orders to prevent duplicates keyed by positionId.
    private readonly HashSet<string> _closeDispatched = [];
    private readonly Lock _closeLock = new();

    public ExitEvaluator(
        PositionRegistry positions,
        OrderRouter orderRouter,
        StrategyService strategyService,
        ConditionParser conditionParser,
        IEventLogger eventLogger,
        ILogger<ExitEvaluator> logger)
    {
        _positions = positions;
        _orderRouter = orderRouter;
        _strategyService = strategyService;
        _conditionParser = conditionParser;
        _eventLogger = eventLogger;
        _logger = logger;

        // Clear close-dispatch tracking when strategy changes.
        _strategyService.OnStrategyLoaded += _ =>
        {
            lock (_closeLock) { _closeDispatched.Clear(); }
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Called on every market tick. Evaluates all exit conditions for all open positions.
    /// </summary>
    public async Task EvaluateAsync(
        IReadOnlyDictionary<string, MarketSnapshot> snapshots,
        string mode,
        CancellationToken token = default)
    {
        var strategy = _strategyService.ActiveStrategy;
        var openPositions = _positions.OpenPositions;

        // exit_all posture or no strategy → close everything immediately.
        if (strategy?.Posture == "exit_all" || strategy is null)
        {
            foreach (var pos in openPositions)
                await DispatchCloseAsync(pos, snapshots, "exit_all", mode, token);
            return;
        }

        // Update unrealized P&L and trailing stops first.
        foreach (var pos in openPositions)
        {
            if (!snapshots.TryGetValue(pos.Asset, out var snap)) continue;
            _positions.UpdateUnrealizedPnl(pos.Asset, snap.Mid);
            UpdateTrailingStop(pos, snap, strategy);
        }

        // Evaluate exit conditions for each open position.
        foreach (var pos in openPositions)
        {
            if (!snapshots.TryGetValue(pos.Asset, out var snap)) continue;

            var strategyPos = strategy.Positions.FirstOrDefault(p => p.Id == pos.StrategyPositionId);
            if (strategyPos is null) continue;

            await EvaluatePositionExitAsync(pos, strategyPos, snap, snapshots, mode, token);
        }
    }

    private async Task EvaluatePositionExitAsync(
        OpenPosition pos,
        StrategyPosition stratPos,
        MarketSnapshot snap,
        IReadOnlyDictionary<string, MarketSnapshot> allSnapshots,
        string mode,
        CancellationToken token)
    {
        // Prevent duplicate close orders.
        lock (_closeLock)
        {
            if (_closeDispatched.Contains(pos.Id)) return;
        }

        // 1. Hard stop-loss.
        if (stratPos.StopLoss?.Type == "hard" && stratPos.StopLoss.Price.HasValue)
        {
            var triggered = pos.Direction == "long"
                ? snap.Bid <= stratPos.StopLoss.Price.Value
                : snap.Ask >= stratPos.StopLoss.Price.Value;

            if (triggered)
            {
                await DispatchCloseAsync(pos, allSnapshots, "stop_loss_hard", mode, token);
                return;
            }
        }

        // 2. Trailing stop-loss.
        if (stratPos.StopLoss?.Type == "trailing" && pos.TrailingStopPrice.HasValue)
        {
            var triggered = pos.Direction == "long"
                ? snap.Bid <= pos.TrailingStopPrice.Value
                : snap.Ask >= pos.TrailingStopPrice.Value;

            if (triggered)
            {
                await DispatchCloseAsync(pos, allSnapshots, "stop_loss_trailing", mode, token);
                return;
            }
        }

        // 3. Time-based exit.
        if (stratPos.TimeExitUtc.HasValue && DateTimeOffset.UtcNow >= stratPos.TimeExitUtc.Value)
        {
            await DispatchCloseAsync(pos, allSnapshots, "time_exit", mode, token);
            return;
        }

        // 4. Invalidation condition.
        if (!string.IsNullOrWhiteSpace(stratPos.InvalidationCondition))
        {
            try
            {
                var condition = _conditionParser.Parse(stratPos.InvalidationCondition);
                var result = condition.Evaluate(allSnapshots);
                if (result == true)
                {
                    await DispatchCloseAsync(pos, allSnapshots, "invalidation", mode, token);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Invalidation condition evaluation failed for position {Id}", pos.Id);
            }
        }

        // 5. Scaled take-profit targets.
        for (var i = 0; i < stratPos.TakeProfitTargets.Count; i++)
        {
            if (pos.TakeProfitTargetsHit.Contains(i)) continue;

            var target = stratPos.TakeProfitTargets[i];
            var triggered = pos.Direction == "long"
                ? snap.Ask >= target.Price
                : snap.Bid <= target.Price;

            if (!triggered) continue;

            // Ensure previous target is filled before this one.
            if (i > 0 && !pos.TakeProfitTargetsHit.Contains(i - 1)) continue;

            pos.TakeProfitTargetsHit.Add(i);
            _positions.UpsertPosition(pos);

            var closeQty = pos.Quantity * target.ClosePct;
            var reason = $"take_profit_target_{i}";

            await _eventLogger.LogAsync(EventTypes.ExitTriggered, mode, new Dictionary<string, object?>
            {
                ["position_id"] = pos.Id,
                ["reason"] = reason,
                ["close_pct"] = (double)target.ClosePct,
                ["target_price"] = (double)target.Price
            }, token);

            var side = pos.Direction == "long" ? OrderSide.Sell : OrderSide.Buy;
            await _orderRouter.PlaceEntryOrderAsync(
                pos.Asset, side, OrderType.Market, closeQty,
                null, $"{pos.StrategyPositionId}_tp_{i}", mode, token);

            // If this is the last target (cumulative close_pct ≥ 1.0), mark full close.
            var totalClosed = stratPos.TakeProfitTargets.Take(i + 1).Sum(t => t.ClosePct);
            if (totalClosed >= 1.0m - 0.001m)
            {
                lock (_closeLock) { _closeDispatched.Add(pos.Id); }
            }

            break; // One target per tick.
        }
    }

    private void UpdateTrailingStop(OpenPosition pos, MarketSnapshot snap, StrategyDocument strategy)
    {
        var stratPos = strategy.Positions.FirstOrDefault(p => p.Id == pos.StrategyPositionId);
        if (stratPos?.StopLoss?.Type != "trailing" || stratPos.StopLoss.TrailPct is null) return;

        var trailFraction = stratPos.StopLoss.TrailPct.Value;

        if (pos.Direction == "long")
        {
            var newStop = snap.Bid * (1 - trailFraction);
            if (pos.TrailingStopPrice is null || newStop > pos.TrailingStopPrice.Value)
            {
                pos.TrailingStopPrice = newStop;
                _positions.UpsertPosition(pos);
            }
        }
        else
        {
            var newStop = snap.Ask * (1 + trailFraction);
            if (pos.TrailingStopPrice is null || newStop < pos.TrailingStopPrice.Value)
            {
                pos.TrailingStopPrice = newStop;
                _positions.UpsertPosition(pos);
            }
        }
    }

    private async Task DispatchCloseAsync(
        OpenPosition pos,
        IReadOnlyDictionary<string, MarketSnapshot> snapshots,
        string reason,
        string mode,
        CancellationToken token)
    {
        lock (_closeLock)
        {
            if (_closeDispatched.Contains(pos.Id)) return;
            _closeDispatched.Add(pos.Id);
        }

        await _eventLogger.LogAsync(EventTypes.ExitTriggered, mode, new Dictionary<string, object?>
        {
            ["position_id"] = pos.Id,
            ["asset"] = pos.Asset,
            ["reason"] = reason
        }, token);

        var side = pos.Direction == "long" ? OrderSide.Sell : OrderSide.Buy;
        await _orderRouter.PlaceEntryOrderAsync(
            pos.Asset, side, OrderType.Market, pos.Quantity,
            null, $"{pos.StrategyPositionId}_exit_{reason}", mode, token);
    }
}

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
/// Evaluates entry conditions for each pending strategy position on every market tick.
/// When a condition transitions from false to true, dispatches an entry order.
/// Implements ES-CEE-001.
/// </summary>
public sealed class EntryEvaluator
{
    private readonly OrderRouter _orderRouter;
    private readonly PositionSizingCalculator _sizingCalc;
    private readonly PortfolioRiskEnforcer _riskEnforcer;
    private readonly StrategyService _strategyService;
    private readonly ConditionParser _conditionParser;
    private readonly PositionRegistry _positionRegistry;
    private readonly IExchangeAdapter _exchange;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<EntryEvaluator> _logger;

    // Track which strategy positions already have an active order (keyed by stratPositionId).
    private readonly HashSet<string> _entryDispatched = [];
    private readonly Lock _dispatchLock = new();

    // Cached compiled strategy — conditions (e.g. CrossingCondition) carry state between ticks.
    private CompiledStrategy? _compiledStrategy;
    private readonly Lock _compileLock = new();

    public EntryEvaluator(
        OrderRouter orderRouter,
        PositionSizingCalculator sizingCalc,
        PortfolioRiskEnforcer riskEnforcer,
        StrategyService strategyService,
        ConditionParser conditionParser,
        PositionRegistry positionRegistry,
        IExchangeAdapter exchange,
        IEventLogger eventLogger,
        ILogger<EntryEvaluator> logger)
    {
        _orderRouter = orderRouter;
        _sizingCalc = sizingCalc;
        _riskEnforcer = riskEnforcer;
        _strategyService = strategyService;
        _conditionParser = conditionParser;
        _positionRegistry = positionRegistry;
        _exchange = exchange;
        _eventLogger = eventLogger;
        _logger = logger;

        // Reset dispatched set and rebuild compiled strategy on strategy change.
        _strategyService.OnStrategyLoaded += s =>
        {
            lock (_dispatchLock) { _entryDispatched.Clear(); }
            lock (_compileLock) { _compiledStrategy = CompiledStrategy.Compile(s, _conditionParser); }
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Called on every market tick. Evaluates all pending positions.
    /// </summary>
    public async Task EvaluateAsync(
        IReadOnlyDictionary<string, MarketSnapshot> snapshots,
        string mode,
        CancellationToken token = default)
    {
        var strategy = _strategyService.ActiveStrategy;
        if (strategy is null || _strategyService.State != StrategyState.Active) return;

        // Posture guard: exit_all / flat → no entries.
        if (strategy.Posture == "exit_all" || strategy.Posture == "flat") return;

        var positions = _positionRegistry.OpenPositions;
        var equity = await GetEquityAsync(token);

        var entriesAllowed = await _riskEnforcer.EvaluateAsync(
            strategy.PortfolioRisk, positions, equity, mode, token);

        if (!entriesAllowed) return;
        if (_riskEnforcer.SafeModeTriggered) return;

        // Ensure we have a compiled strategy (first tick before OnStrategyLoaded fires, or on demand).
        CompiledStrategy compiledStrategy;
        lock (_compileLock)
        {
            if (_compiledStrategy is null)
                _compiledStrategy = CompiledStrategy.Compile(strategy, _conditionParser);
            compiledStrategy = _compiledStrategy;
        }

        var tasks = compiledStrategy.Positions.Select(cp =>
            EvaluatePositionEntryAsync(cp, snapshots, strategy, mode, token));

        await Task.WhenAll(tasks);
    }

    private async Task EvaluatePositionEntryAsync(
        CompiledPosition cp,
        IReadOnlyDictionary<string, MarketSnapshot> snapshots,
        StrategyDocument strategy,
        string mode,
        CancellationToken token)
    {
        var pos = cp.Source;

        // Already dispatched for this strategy cycle?
        lock (_dispatchLock)
        {
            if (_entryDispatched.Contains(pos.Id)) return;
        }

        bool shouldEnter;

        if (pos.EntryType == "market")
        {
            // Market: fire once when strategy loads.
            shouldEnter = true;
        }
        else if (pos.EntryType == "limit")
        {
            // Limit: fire once when the live price reaches/crosses the limit.
            if (!snapshots.TryGetValue(pos.Asset, out var snap)) return;
            shouldEnter = pos.Direction == "long"
                ? snap.Bid <= (pos.EntryLimitPrice ?? 0)
                : snap.Ask >= (pos.EntryLimitPrice ?? decimal.MaxValue);
        }
        else // conditional
        {
            if (cp.EntryCondition is null) return;
            var result = cp.EntryCondition.Evaluate(snapshots);
            if (result is null)
            {
                await _eventLogger.LogAsync(EventTypes.EntrySkipped, mode, new Dictionary<string, object?>
                {
                    ["position_id"] = pos.Id,
                    ["reason"] = "indicator_not_ready"
                }, token);
                return;
            }
            shouldEnter = result.Value;
        }

        if (!shouldEnter) return;

        if (!snapshots.TryGetValue(pos.Asset, out var currentSnap)) return;

        var quantity = await _sizingCalc.CalculateAsync(
            pos.Asset, pos.AllocationPct,
            strategy.PortfolioRisk.MaxPerPositionPct,
            currentSnap.Mid, mode, token);

        if (quantity is null) return;

        lock (_dispatchLock)
        {
            // Double-check: another concurrent task may have dispatched between the two checks.
            if (_entryDispatched.Contains(pos.Id)) return;
            _entryDispatched.Add(pos.Id);
        }

        await _eventLogger.LogAsync(EventTypes.EntryTriggered, mode, new Dictionary<string, object?>
        {
            ["position_id"] = pos.Id,
            ["asset"] = pos.Asset,
            ["entry_type"] = pos.EntryType,
            ["quantity"] = (double)quantity.Value
        }, token);

        var orderType = pos.EntryType == "limit"
            ? OrderType.Limit
            : OrderType.Market;

        var side = pos.Direction == "long" ? OrderSide.Buy : OrderSide.Sell;

        await _orderRouter.PlaceEntryOrderAsync(
            pos.Asset, side, orderType, quantity.Value,
            pos.EntryLimitPrice, pos.Id, mode, token);
    }

    private async Task<decimal> GetEquityAsync(CancellationToken token)
    {
        try
        {
            var balance = await _exchange.GetAccountBalanceAsync(token);
            // Equity = available cash + value of all open positions at current price.
            var openPositionsValue = _positionRegistry.OpenPositions
                .Where(p => p.CurrentPrice > 0)
                .Sum(p => p.Quantity * p.CurrentPrice);
            return balance.AvailableUsd + openPositionsValue;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch account balance for equity calculation");
            return 0m;
        }
    }
}

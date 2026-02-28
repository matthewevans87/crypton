using System.Text.Json;
using Crypton.Api.ExecutionService.Logging;
using Microsoft.Extensions.Logging;

namespace Crypton.Api.ExecutionService.Positions;

/// <summary>
/// Authoritative in-process record of all open positions and completed trades.
/// Thread-safe. Persisted atomically to disk on every mutation.
/// All mutations must go through the methods on this class.
/// </summary>
public sealed class PositionRegistry
{
    private readonly string _registryPath;
    private readonly string _tradeHistoryPath;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<PositionRegistry> _logger;
    private readonly Lock _lock = new();

    private readonly Dictionary<string, OpenPosition> _positions = [];
    private readonly List<ClosedTrade> _trades = [];

    /// <summary>
    /// Raised after any position mutation (open, upsert, or close).
    /// The argument is the position that was changed (for close, the position prior to removal).
    /// Fired outside internal locks.
    /// </summary>
    public event Func<OpenPosition, Task>? OnPositionChanged;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public PositionRegistry(
        string registryPath,
        string tradeHistoryPath,
        IEventLogger eventLogger,
        ILogger<PositionRegistry> logger)
    {
        _registryPath = registryPath;
        _tradeHistoryPath = tradeHistoryPath;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    /// <summary>Load state from disk on startup. Must be called before any other method.</summary>
    public void Load()
    {
        lock (_lock)
        {
            LoadPositions();
            LoadTrades();
        }
    }

    public IReadOnlyList<OpenPosition> OpenPositions
    {
        get { lock (_lock) { return _positions.Values.ToList(); } }
    }

    public IReadOnlyList<ClosedTrade> ClosedTrades
    {
        get { lock (_lock) { return _trades.ToList(); } }
    }

    /// <summary>Add or replace a position (used during reconciliation).</summary>
    public void UpsertPosition(OpenPosition position)
    {
        lock (_lock)
        {
            _positions[position.Id] = position;
            PersistPositions();
        }
        if (OnPositionChanged is not null)
            _ = OnPositionChanged(position);
    }

    /// <summary>Create a new position from a filled order.</summary>
    public OpenPosition OpenPosition(
        string strategyPositionId, string strategyId, string asset,
        string direction, decimal quantity, decimal entryPrice)
    {
        var position = new OpenPosition
        {
            Id = Guid.NewGuid().ToString("N"),
            StrategyPositionId = strategyPositionId,
            StrategyId = strategyId,
            Asset = asset,
            Direction = direction,
            Quantity = quantity,
            AverageEntryPrice = entryPrice,
            OpenedAt = DateTimeOffset.UtcNow
        };

        lock (_lock)
        {
            _positions[position.Id] = position;
            PersistPositions();
        }

        _ = _eventLogger.LogAsync(EventTypes.PositionOpened, "active", new Dictionary<string, object?>
        {
            ["position_id"] = position.Id,
            ["asset"] = asset,
            ["direction"] = direction,
            ["quantity"] = (double)quantity,
            ["entry_price"] = (double)entryPrice
        });

        if (OnPositionChanged is not null)
            _ = OnPositionChanged(position);

        return position;
    }

    /// <summary>Update quantity and average entry price when a partial fill arrives.</summary>
    public void ApplyPartialFill(string positionId, decimal additionalQuantity, decimal fillPrice)
    {
        lock (_lock)
        {
            if (!_positions.TryGetValue(positionId, out var pos)) return;
            var totalQty = pos.Quantity + additionalQuantity;
            var newAvg = (pos.Quantity * pos.AverageEntryPrice + additionalQuantity * fillPrice) / totalQty;
            pos.Quantity = totalQty;
            pos.AverageEntryPrice = newAvg;
            PersistPositions();
        }
    }

    /// <summary>Close a position (fully or partially). Returns the closed trade record.</summary>
    public ClosedTrade? ClosePosition(string positionId, decimal exitPrice, string exitReason)
    {
        OpenPosition? closedPos = null;
        ClosedTrade? trade = null;
        decimal pnl = 0;

        lock (_lock)
        {
            if (!_positions.TryGetValue(positionId, out var pos)) return null;
            closedPos = pos;

            pnl = pos.Direction == "long"
                ? (exitPrice - pos.AverageEntryPrice) * pos.Quantity
                : (pos.AverageEntryPrice - exitPrice) * pos.Quantity;

            trade = new ClosedTrade
            {
                Id = Guid.NewGuid().ToString("N"),
                PositionId = pos.Id,
                Asset = pos.Asset,
                Direction = pos.Direction,
                Quantity = pos.Quantity,
                EntryPrice = pos.AverageEntryPrice,
                ExitPrice = exitPrice,
                OpenedAt = pos.OpenedAt,
                ClosedAt = DateTimeOffset.UtcNow,
                ExitReason = exitReason,
                RealizedPnl = pnl,
                StrategyId = pos.StrategyId
            };

            _positions.Remove(positionId);
            _trades.Add(trade);
            PersistPositions();
            PersistTrades();
        }

        _ = _eventLogger.LogAsync(EventTypes.PositionClosed, "active", new Dictionary<string, object?>
        {
            ["position_id"] = positionId,
            ["asset"] = closedPos.Asset,
            ["exit_reason"] = exitReason,
            ["realized_pnl"] = (double)pnl
        });

        if (OnPositionChanged is not null)
            _ = OnPositionChanged(closedPos);

        return trade;
    }

    /// <summary>Update unrealized P&L for all open positions from latest market prices.</summary>
    public void UpdateUnrealizedPnl(string asset, decimal currentPrice)
    {
        lock (_lock)
        {
            foreach (var pos in _positions.Values.Where(p => p.Asset == asset))
            {
                pos.CurrentPrice = currentPrice;
                pos.UnrealizedPnl = pos.Direction == "long"
                    ? (currentPrice - pos.AverageEntryPrice) * pos.Quantity
                    : (pos.AverageEntryPrice - currentPrice) * pos.Quantity;
            }
        }
    }

    /// <summary>Remove a position by ID (used when reconciliation finds it closed externally).</summary>
    public bool RemovePosition(string positionId)
    {
        lock (_lock)
        {
            var removed = _positions.Remove(positionId);
            if (removed) PersistPositions();
            return removed;
        }
    }

    private void PersistPositions()
    {
        var dir = Path.GetDirectoryName(_registryPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _registryPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_positions.Values.ToList(), JsonOpts));
        File.Move(tmp, _registryPath, overwrite: true);
    }

    private void PersistTrades()
    {
        var dir = Path.GetDirectoryName(_tradeHistoryPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var tmp = _tradeHistoryPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_trades, JsonOpts));
        File.Move(tmp, _tradeHistoryPath, overwrite: true);
    }

    private void LoadPositions()
    {
        if (!File.Exists(_registryPath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<OpenPosition>>(
                File.ReadAllText(_registryPath), JsonOpts) ?? [];
            foreach (var p in list) _positions[p.Id] = p;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to load position registry"); }
    }

    private void LoadTrades()
    {
        if (!File.Exists(_tradeHistoryPath)) return;
        try
        {
            var list = JsonSerializer.Deserialize<List<ClosedTrade>>(
                File.ReadAllText(_tradeHistoryPath), JsonOpts) ?? [];
            _trades.AddRange(list);
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to load trade history"); }
    }
}

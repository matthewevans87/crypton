using System.Text.Json;
using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Resilience;

/// <summary>
/// Manages the persistent safe mode state. Safe mode survives restarts via disk persistence.
/// When activated, market-sells every open position to minimise exposure.
/// Thread-safe.
/// </summary>
public sealed class SafeModeController : ISafeModeController
{
    private readonly string _statePath;
    private readonly IEventLogger _eventLogger;
    private readonly PositionRegistry _positions;
    private readonly OrderRouter _orderRouter;
    private readonly FailureTracker _failureTracker;
    private readonly ILogger<SafeModeController> _logger;
    private readonly Lock _lock = new();
    private bool _isActive;
    private string? _reason;
    private DateTimeOffset? _triggeredAt;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    public bool IsActive { get { lock (_lock) { return _isActive; } } }
    public string? Reason { get { lock (_lock) { return _reason; } } }

    public SafeModeController(
        IOptions<ExecutionServiceConfig> config,
        IEventLogger eventLogger,
        PositionRegistry positions,
        OrderRouter orderRouter,
        FailureTracker failureTracker,
        ILogger<SafeModeController> logger)
    {
        _statePath = Path.Combine(config.Value.Safety.ResilienceStatePath, "safe_mode.json");
        _eventLogger = eventLogger;
        _positions = positions;
        _orderRouter = orderRouter;
        _failureTracker = failureTracker;
        _logger = logger;
    }

    /// <summary>
    /// Load persisted state from disk. Returns true if safe mode was already active.
    /// Call this once on service startup before processing any orders.
    /// </summary>
    public Task<bool> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_statePath))
            return Task.FromResult(false);
        try
        {
            var json = File.ReadAllText(_statePath);
            var state = JsonSerializer.Deserialize<SafeModeState>(json, JsonOpts);
            if (state is { Active: true })
            {
                lock (_lock)
                {
                    _isActive = true;
                    _reason = state.Reason;
                    _triggeredAt = state.TriggeredAt;
                }
                _logger.LogWarning("Safe mode restored from disk. Reason: {Reason}", state.Reason);
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load safe mode state from {Path}", _statePath);
        }
        return Task.FromResult(false);
    }

    /// <summary>
    /// Activate safe mode: persists state, emits event, and market-sells all open positions.
    /// No-op if already active.
    /// </summary>
    public async Task ActivateAsync(string reason, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_isActive) return;
            _isActive = true;
            _reason = reason;
            _triggeredAt = DateTimeOffset.UtcNow;
        }

        Persist();

        await _eventLogger.LogAsync(EventTypes.SafeModeActivated, "safe", new Dictionary<string, object?>
        {
            ["reason"] = reason,
            ["triggered_at"] = _triggeredAt!.Value.ToString("O")
        }, ct);

        _logger.LogWarning("Safe mode activated. Reason: {Reason}", reason);

        // Close all open positions with market orders.
        var openPositions = _positions.OpenPositions;
        foreach (var pos in openPositions)
        {
            try
            {
                var side = pos.Direction == "long" ? OrderSide.Sell : OrderSide.Buy;
                await _orderRouter.PlaceEntryOrderAsync(
                    pos.Asset, side, OrderType.Market, pos.Quantity,
                    null, $"safe_mode_close_{pos.Id}", "safe", ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to close position {PositionId} ({Asset}) during safe mode activation",
                    pos.Id, pos.Asset);
            }
        }
    }

    /// <summary>
    /// Deactivate safe mode: persists state, resets the failure tracker, and emits event.
    /// </summary>
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            _isActive = false;
            _reason = null;
            _triggeredAt = null;
        }

        Persist();
        _failureTracker.Reset();

        await _eventLogger.LogAsync(EventTypes.SafeModeDeactivated, "active", new Dictionary<string, object?>
        {
            ["deactivated_at"] = DateTimeOffset.UtcNow.ToString("O")
        }, ct);

        _logger.LogInformation("Safe mode deactivated.");
    }

    private void Persist()
    {
        try
        {
            bool isActive;
            string? reason;
            DateTimeOffset? triggeredAt;
            lock (_lock)
            {
                isActive = _isActive;
                reason = _reason;
                triggeredAt = _triggeredAt;
            }

            var dir = Path.GetDirectoryName(_statePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var state = new SafeModeState
            {
                Active = isActive,
                TriggeredAt = triggeredAt,
                Reason = reason
            };

            var tmp = _statePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOpts));
            File.Move(tmp, _statePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist safe mode state to {Path}", _statePath);
        }
    }

    private sealed class SafeModeState
    {
        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("triggered_at")]
        public DateTimeOffset? TriggeredAt { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}

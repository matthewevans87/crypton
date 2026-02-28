using Crypton.Api.ExecutionService.Execution;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Metrics;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Orders;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Crypton.Api.ExecutionService.Strategy;
using Microsoft.AspNetCore.Mvc;

namespace Crypton.Api.ExecutionService.Api;

// ---------------------------------------------------------------------------
// Status
// ---------------------------------------------------------------------------

[ApiController]
public sealed class StatusController : ControllerBase
{
    private readonly IOperationModeService _mode;
    private readonly ISafeModeController _safeMode;
    private readonly IStrategyService _strategy;
    private readonly PositionRegistry _positions;
    private readonly MarketDataHub _marketData;

    public StatusController(
        IOperationModeService mode,
        ISafeModeController safeMode,
        IStrategyService strategy,
        PositionRegistry positions,
        MarketDataHub marketData)
    {
        _mode = mode;
        _safeMode = safeMode;
        _strategy = strategy;
        _positions = positions;
        _marketData = marketData;
    }

    [HttpGet("/status")]
    public IActionResult GetStatus() => Ok(new
    {
        mode = _mode.CurrentMode,
        safe_mode = _safeMode.IsActive,
        strategy_state = _strategy.State.ToString().ToLowerInvariant(),
        strategy_id = _strategy.ActiveStrategyId,
        open_positions = _positions.OpenPositions.Count,
        last_tick_at = _marketData.LastTickAt
    });

    [HttpGet("/health/live")]
    public IActionResult Live() => Ok();
}

// ---------------------------------------------------------------------------
// Strategy
// ---------------------------------------------------------------------------

[ApiController]
public sealed class StrategyController : ControllerBase
{
    private readonly IStrategyService _strategy;

    public StrategyController(IStrategyService strategy) => _strategy = strategy;

    [HttpGet("/strategy")]
    public IActionResult GetStrategy()
    {
        var s = _strategy.ActiveStrategy;
        return s is null ? NotFound(new { error = "No active strategy." }) : Ok(s);
    }
}

// ---------------------------------------------------------------------------
// Positions
// ---------------------------------------------------------------------------

[ApiController]
public sealed class PositionsController : ControllerBase
{
    private readonly PositionRegistry _registry;

    public PositionsController(PositionRegistry registry) => _registry = registry;

    [HttpGet("/positions")]
    public IActionResult GetPositions() => Ok(_registry.OpenPositions);

    [HttpGet("/positions/{id}")]
    public IActionResult GetPosition(string id)
    {
        var pos = _registry.OpenPositions.FirstOrDefault(p => p.Id == id);
        return pos is null ? NotFound(new { error = "Position not found." }) : Ok(pos);
    }
}

// ---------------------------------------------------------------------------
// Orders
// ---------------------------------------------------------------------------

[ApiController]
public sealed class OrdersController : ControllerBase
{
    private readonly OrderRouter _router;

    public OrdersController(OrderRouter router) => _router = router;

    [HttpGet("/orders")]
    public IActionResult GetOrders() => Ok(_router.ActiveOrders);
}

// ---------------------------------------------------------------------------
// Trades
// ---------------------------------------------------------------------------

[ApiController]
public sealed class TradesController : ControllerBase
{
    private readonly PositionRegistry _registry;

    public TradesController(PositionRegistry registry) => _registry = registry;

    [HttpGet("/trades")]
    public IActionResult GetTrades() => Ok(_registry.ClosedTrades);
}

// ---------------------------------------------------------------------------
// Metrics
// ---------------------------------------------------------------------------

[ApiController]
public sealed class MetricsController : ControllerBase
{
    private readonly IMetricsCollector _metrics;

    public MetricsController(IMetricsCollector metrics) => _metrics = metrics;

    [HttpGet("/metrics")]
    public IActionResult GetMetrics() => Ok(_metrics.GetSnapshot());
}

// ---------------------------------------------------------------------------
// Events
// ---------------------------------------------------------------------------

[ApiController]
public sealed class EventsController : ControllerBase
{
    private readonly IEventLogger _eventLogger;

    public EventsController(IEventLogger eventLogger) => _eventLogger = eventLogger;

    [HttpGet("/events")]
    public async Task<IActionResult> GetEvents([FromQuery] int limit = 100)
    {
        var events = await _eventLogger.GetRecentAsync(Math.Clamp(limit, 1, 1000));
        return Ok(events);
    }
}

// ---------------------------------------------------------------------------
// Operator (write endpoints — require ApiKeyAuthFilter)
// ---------------------------------------------------------------------------

[ApiController]
public sealed class OperatorController : ControllerBase
{
    private readonly ISafeModeController _safeMode;
    private readonly IOperationModeService _mode;
    private readonly IStrategyService _strategy;

    public OperatorController(
        ISafeModeController safeMode,
        IOperationModeService mode,
        IStrategyService strategy)
    {
        _safeMode = safeMode;
        _mode = mode;
        _strategy = strategy;
    }

    [HttpPost("/operator/safe-mode/activate")]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<IActionResult> ActivateSafeMode(
        [FromBody] SafeModeActivateRequest body,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(body.Reason))
            return BadRequest(new { error = "reason is required." });

        await _safeMode.ActivateAsync(body.Reason, ct);
        return NoContent();
    }

    [HttpPost("/operator/safe-mode/deactivate")]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<IActionResult> DeactivateSafeMode(CancellationToken ct = default)
    {
        await _safeMode.DeactivateAsync(ct);
        return NoContent();
    }

    [HttpPost("/operator/mode/promote-to-live")]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<IActionResult> PromoteToLive([FromBody] OperatorNoteRequest body, CancellationToken ct = default)
    {
        await _mode.PromoteToLiveAsync(body.OperatorNote ?? string.Empty, ct);
        return NoContent();
    }

    [HttpPost("/operator/mode/demote-to-paper")]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<IActionResult> DemoteToPaper([FromBody] OperatorNoteRequest body, CancellationToken ct = default)
    {
        await _mode.DemoteToPaperAsync(body.OperatorNote ?? string.Empty, ct);
        return NoContent();
    }

    [HttpPost("/operator/strategy/reload")]
    [ServiceFilter(typeof(ApiKeyAuthFilter))]
    public async Task<IActionResult> ReloadStrategy(CancellationToken ct = default)
    {
        await _strategy.ForceReloadAsync(ct);
        return NoContent();
    }
}

// ---------------------------------------------------------------------------
// Request DTOs
// ---------------------------------------------------------------------------

public sealed record SafeModeActivateRequest(string Reason);
public sealed record OperatorNoteRequest(string? OperatorNote);

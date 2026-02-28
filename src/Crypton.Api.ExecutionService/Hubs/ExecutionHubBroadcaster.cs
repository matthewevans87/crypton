using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Metrics;
using Crypton.Api.ExecutionService.OperationMode;
using Crypton.Api.ExecutionService.Positions;
using Crypton.Api.ExecutionService.Resilience;
using Crypton.Api.ExecutionService.Strategy;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Hubs;

/// <summary>
/// Background service that pushes real-time updates to SignalR clients:
/// - StatusUpdate every 2 seconds (to group "status")
/// - MetricsUpdate every 1/metrics_update_hz seconds (to group "metrics")
/// - EventLog entry on each new event (to group "events")
/// - PositionUpdate on each position change (to group "positions")
/// </summary>
public sealed class ExecutionHubBroadcaster : IHostedService, IDisposable
{
    private readonly IHubContext<ExecutionHub> _hub;
    private readonly IStrategyService _strategy;
    private readonly ISafeModeController _safeMode;
    private readonly IOperationModeService _mode;
    private readonly PositionRegistry _positions;
    private readonly IMetricsCollector _metrics;
    private readonly IEventLogger _eventLogger;
    private readonly Configuration.ExecutionServiceConfig _config;
    private readonly ILogger<ExecutionHubBroadcaster> _logger;

    private CancellationTokenSource? _cts;
    private Task? _statusLoop;
    private Task? _metricsLoop;

    public ExecutionHubBroadcaster(
        IHubContext<ExecutionHub> hub,
        IStrategyService strategy,
        ISafeModeController safeMode,
        IOperationModeService mode,
        PositionRegistry positions,
        IMetricsCollector metrics,
        IEventLogger eventLogger,
        IOptions<Configuration.ExecutionServiceConfig> config,
        ILogger<ExecutionHubBroadcaster> logger)
    {
        _hub = hub;
        _strategy = strategy;
        _safeMode = safeMode;
        _mode = mode;
        _positions = positions;
        _metrics = metrics;
        _eventLogger = eventLogger;
        _config = config.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _statusLoop = RunStatusLoopAsync(_cts.Token);
        _metricsLoop = RunMetricsLoopAsync(_cts.Token);

        // Subscribe to events and position changes
        _eventLogger.OnEventLogged += OnEventLoggedAsync;
        _positions.OnPositionChanged += OnPositionChangedAsync;

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _eventLogger.OnEventLogged -= OnEventLoggedAsync;
        _positions.OnPositionChanged -= OnPositionChangedAsync;

        _cts?.Cancel();

        if (_statusLoop is not null) await _statusLoop.ConfigureAwait(false);
        if (_metricsLoop is not null) await _metricsLoop.ConfigureAwait(false);
    }

    private async Task RunStatusLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                var payload = new
                {
                    mode = _mode.CurrentMode,
                    safe_mode = _safeMode.IsActive,
                    strategy_state = _strategy.State.ToString().ToLowerInvariant(),
                    strategy_id = _strategy.ActiveStrategyId,
                    open_positions = _positions.OpenPositions.Count,
                    timestamp = DateTimeOffset.UtcNow
                };
                await _hub.Clients.Group(ExecutionHub.StatusGroup)
                    .SendAsync("StatusUpdate", payload, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Status broadcast error"); }
        }
    }

    private async Task RunMetricsLoopAsync(CancellationToken ct)
    {
        var hz = Math.Max(1, _config.Streaming.MetricsUpdateHz);
        var interval = TimeSpan.FromSeconds(1.0 / hz);
        using var timer = new PeriodicTimer(interval);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(ct);
                var snapshot = _metrics.GetSnapshot();
                await _hub.Clients.Group(ExecutionHub.MetricsGroup)
                    .SendAsync("MetricsUpdate", snapshot, ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogWarning(ex, "Metrics broadcast error"); }
        }
    }

    private async Task OnEventLoggedAsync(ExecutionEvent evt)
    {
        try
        {
            await _hub.Clients.Group(ExecutionHub.EventLogGroup)
                .SendAsync("EventLog", evt);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Event broadcast error"); }
    }

    private async Task OnPositionChangedAsync(Positions.OpenPosition position)
    {
        try
        {
            await _hub.Clients.Group(ExecutionHub.PositionsGroup)
                .SendAsync("PositionUpdate", position);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Position broadcast error"); }
    }

    public void Dispose() => _cts?.Dispose();
}

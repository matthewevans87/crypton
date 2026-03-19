using Microsoft.AspNetCore.SignalR;
using MonitoringDashboard.Hubs;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Services;

/// <summary>
/// Background service that periodically checks the health of all upstream services
/// and pushes <c>SystemHealthUpdated</c> to all connected dashboard clients via SignalR.
/// This means the dashboard does not need to poll <c>/api/system/status</c> while the
/// WebSocket connection is alive.
/// </summary>
public sealed class SystemHealthBroadcaster : BackgroundService
{
    private readonly ISystemHealthChecker _healthChecker;
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;
    private readonly ILogger<SystemHealthBroadcaster> _logger;

    // How often to push health updates. Intentionally infrequent; service health
    // changes rarely and the HTTP checks against upstream services are non-trivial.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(30);

    public SystemHealthBroadcaster(
        ISystemHealthChecker healthChecker,
        IHubContext<DashboardHub, IDashboardClient> hub,
        ILogger<SystemHealthBroadcaster> logger)
    {
        _healthChecker = healthChecker;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await timer.WaitForNextTickAsync(stoppingToken);
                var status = await _healthChecker.GetStatusAsync(stoppingToken, "broadcaster");
                await _hub.Clients.All.SystemHealthUpdated(status);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SystemHealthBroadcaster check/broadcast error");
            }
        }
    }
}

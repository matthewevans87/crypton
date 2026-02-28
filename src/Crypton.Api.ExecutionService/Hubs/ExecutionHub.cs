using Microsoft.AspNetCore.SignalR;

namespace Crypton.Api.ExecutionService.Hubs;

/// <summary>
/// SignalR hub for real-time streaming of execution service status,
/// metrics, event log entries, and position updates to connected clients.
/// </summary>
public sealed class ExecutionHub : Hub
{
    // Stream group names
    public const string StatusGroup = "status";
    public const string MetricsGroup = "metrics";
    public const string EventLogGroup = "events";
    public const string PositionsGroup = "positions";

    public Task SubscribeToStatus() => Groups.AddToGroupAsync(Context.ConnectionId, StatusGroup);
    public Task UnsubscribeFromStatus() => Groups.RemoveFromGroupAsync(Context.ConnectionId, StatusGroup);

    public Task SubscribeToMetrics() => Groups.AddToGroupAsync(Context.ConnectionId, MetricsGroup);
    public Task UnsubscribeFromMetrics() => Groups.RemoveFromGroupAsync(Context.ConnectionId, MetricsGroup);

    public Task SubscribeToEvents() => Groups.AddToGroupAsync(Context.ConnectionId, EventLogGroup);
    public Task UnsubscribeFromEvents() => Groups.RemoveFromGroupAsync(Context.ConnectionId, EventLogGroup);

    public Task SubscribeToPositions() => Groups.AddToGroupAsync(Context.ConnectionId, PositionsGroup);
    public Task UnsubscribeFromPositions() => Groups.RemoveFromGroupAsync(Context.ConnectionId, PositionsGroup);

    // Generic helpers kept for backwards compatibility
    public Task Subscribe(string stream) => Groups.AddToGroupAsync(Context.ConnectionId, stream);
    public Task Unsubscribe(string stream) => Groups.RemoveFromGroupAsync(Context.ConnectionId, stream);
}


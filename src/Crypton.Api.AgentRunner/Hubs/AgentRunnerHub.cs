using Microsoft.AspNetCore.SignalR;

namespace AgentRunner.Hubs;

/// <summary>
/// SignalR hub for real-time streaming of agent runner status,
/// step events, LLM tokens, and tool call updates to connected clients.
/// </summary>
public sealed class AgentRunnerHub : Hub
{
    public const string StatusGroup = "status";
    public const string StepsGroup = "steps";
    public const string TokensGroup = "tokens";
    public const string ToolCallsGroup = "toolcalls";
    public const string MetricsGroup = "metrics";

    public Task SubscribeToStatus() => Groups.AddToGroupAsync(Context.ConnectionId, StatusGroup);
    public Task UnsubscribeFromStatus() => Groups.RemoveFromGroupAsync(Context.ConnectionId, StatusGroup);

    public Task SubscribeToSteps() => Groups.AddToGroupAsync(Context.ConnectionId, StepsGroup);
    public Task UnsubscribeFromSteps() => Groups.RemoveFromGroupAsync(Context.ConnectionId, StepsGroup);

    public Task SubscribeToTokens() => Groups.AddToGroupAsync(Context.ConnectionId, TokensGroup);
    public Task UnsubscribeFromTokens() => Groups.RemoveFromGroupAsync(Context.ConnectionId, TokensGroup);

    public Task SubscribeToToolCalls() => Groups.AddToGroupAsync(Context.ConnectionId, ToolCallsGroup);
    public Task UnsubscribeFromToolCalls() => Groups.RemoveFromGroupAsync(Context.ConnectionId, ToolCallsGroup);

    public Task SubscribeToMetrics() => Groups.AddToGroupAsync(Context.ConnectionId, MetricsGroup);
    public Task UnsubscribeFromMetrics() => Groups.RemoveFromGroupAsync(Context.ConnectionId, MetricsGroup);

    // Generic helpers for backwards compatibility
    public Task Subscribe(string stream) => Groups.AddToGroupAsync(Context.ConnectionId, stream);
    public Task Unsubscribe(string stream) => Groups.RemoveFromGroupAsync(Context.ConnectionId, stream);
}

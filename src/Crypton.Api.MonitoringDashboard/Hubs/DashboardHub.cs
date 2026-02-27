using Microsoft.AspNetCore.SignalR;
using MonitoringDashboard.Models;

namespace MonitoringDashboard.Hubs;

public class DashboardHub : Hub<IDashboardClient>
{
    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}

public interface IDashboardClient
{
    Task PortfolioUpdated(PortfolioSummary summary);
    Task PositionUpdated(Position position);
    Task PriceUpdated(PriceTicker ticker);
    Task AgentStateChanged(AgentState state);
    Task ToolCallStarted(ToolCall toolCall);
    Task ToolCallCompleted(ToolCall toolCall);
    Task ReasoningUpdated(ReasoningStep step);
    Task StrategyUpdated(Strategy strategy);
    Task CycleCompleted(CyclePerformance performance);
    Task EvaluationCompleted(EvaluationSummary evaluation);
    Task ErrorOccurred(string error);
}

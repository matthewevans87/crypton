namespace MonitoringDashboard.Configuration;

public sealed class MonitoringDashboardConfig
{
    public ServiceEndpointConfig MarketDataService { get; init; } = new();
    public ServiceEndpointConfig ExecutionService { get; init; } = new();
    public AgentRunnerConfig AgentRunner { get; init; } = new();
}

public sealed class ServiceEndpointConfig
{
    public string Url { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class AgentRunnerConfig
{
    public string Url { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
}

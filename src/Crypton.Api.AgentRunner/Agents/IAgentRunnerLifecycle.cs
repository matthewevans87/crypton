namespace AgentRunner.Agents;

public interface IAgentRunnerLifecycle
{
    bool IsRunning { get; }
    Task StartAsync();
    Task StopAsync();
}
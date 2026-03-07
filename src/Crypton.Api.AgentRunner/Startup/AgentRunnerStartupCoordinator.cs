using AgentRunner.Agents;
using AgentRunner.Configuration;

namespace AgentRunner.Startup;

public sealed class AgentRunnerStartupCoordinator
{
    private readonly IStartupValidator _validator;
    private readonly ServiceAvailabilityState _availabilityState;
    private readonly IAgentRunnerLifecycle _agentRunner;
    private readonly AgentRunnerConfig _config;

    public AgentRunnerStartupCoordinator(
        IStartupValidator validator,
        ServiceAvailabilityState availabilityState,
        IAgentRunnerLifecycle agentRunner,
        AgentRunnerConfig config)
    {
        _validator = validator;
        _availabilityState = availabilityState;
        _agentRunner = agentRunner;
        _config = config;
    }

    public async Task<AgentRunnerStartupResult> TryStartAsync(CancellationToken cancellationToken = default)
    {
        if (_agentRunner.IsRunning)
        {
            return AgentRunnerStartupResult.AlreadyRunningResult();
        }

        var validation = await _validator.ValidateAsync(_config, cancellationToken);
        if (!validation.IsValid)
        {
            _availabilityState.EnterDegraded(validation.Errors);
            return AgentRunnerStartupResult.DegradedResult(validation.Errors);
        }

        _availabilityState.ClearDegraded();
        await _agentRunner.StartAsync();
        return AgentRunnerStartupResult.StartedResult();
    }
}

public sealed record AgentRunnerStartupResult(
    bool Started,
    bool IsDegraded,
    string Message,
    IReadOnlyList<string> Errors)
{
    public static AgentRunnerStartupResult StartedResult() =>
        new(true, false, "Agent runner service started successfully", []);

    public static AgentRunnerStartupResult AlreadyRunningResult() =>
        new(false, false, "Agent runner service is already running", []);

    public static AgentRunnerStartupResult DegradedResult(IReadOnlyList<string> errors) =>
        new(false, true, "Agent runner is in degraded service mode", errors);
}

using AgentRunner.Configuration;

namespace AgentRunner.Startup;

public interface IStartupValidator
{
    Task<StartupValidationResult> ValidateAsync(
        AgentRunnerConfig config,
        CancellationToken cancellationToken = default);
}
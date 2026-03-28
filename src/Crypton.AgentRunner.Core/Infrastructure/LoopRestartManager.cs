using AgentRunner.Abstractions;
using AgentRunner.Configuration;

namespace AgentRunner.Infrastructure;

/// <summary>
/// Decides whether the loop should restart after an unexpected exit, applying
/// exponential backoff between attempts.
/// </summary>
public sealed class LoopRestartManager
{
    private readonly AgentRunnerConfig _config;
    private readonly IEventLogger _logger;

    public LoopRestartManager(AgentRunnerConfig config, IEventLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns <c>true</c> after waiting the backoff delay; the caller should restart the loop.
    /// Returns <c>false</c> when the maximum restart budget is exhausted.
    /// </summary>
    public async Task<bool> ShouldRestartAsync(int restartCount, CancellationToken ct)
    {
        if (restartCount >= _config.Resilience.MaxRestartAttempts)
        {
            _logger.LogError(
                $"Exceeded max restart attempts ({_config.Resilience.MaxRestartAttempts}). Giving up.");
            return false;
        }

        var delayMinutes = Math.Min(
            _config.Resilience.BaseRestartDelayMinutes * Math.Pow(2, restartCount - 1),
            _config.Resilience.MaxRestartDelayMinutes);

        _logger.LogWarning(
            $"Loop exited unexpectedly. Restarting in {delayMinutes:F1} min " +
            $"(attempt {restartCount + 1}/{_config.Resilience.MaxRestartAttempts}).");

        await Task.Delay(TimeSpan.FromMinutes(delayMinutes), ct);
        return true;
    }
}

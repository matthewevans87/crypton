using AgentRunner.Configuration;
using AgentRunner.Logging;

namespace AgentRunner.Agents;

/// <summary>
/// Decides whether the loop should restart after an unexpected exit, applying
/// exponential backoff between attempts. Returns false when the restart budget
/// is exhausted so the caller can transition to Failed.
/// </summary>
public class LoopRestartManager
{
    private readonly AgentRunnerConfig _config;
    private readonly IEventLogger _logger;

    public LoopRestartManager(AgentRunnerConfig config, IEventLogger logger)
    {
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// Returns <c>true</c> after waiting the appropriate backoff delay, signalling
    /// the caller should restart the loop. Returns <c>false</c> when the maximum
    /// restart count has been reached.
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
            $"Loop exited. Restarting in {delayMinutes:F1} min " +
            $"(attempt {restartCount}/{_config.Resilience.MaxRestartAttempts})");

        await Task.Delay(TimeSpan.FromMinutes(delayMinutes), ct);
        return true;
    }
}

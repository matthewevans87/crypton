using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.StateMachine;

namespace AgentRunner.Agents;

/// <summary>
/// Manages the wait period between learning cycles. Supports live-configurable intervals
/// (re-read on each poll tick) and a force-cycle signal for immediate progression.
/// </summary>
public class CycleScheduler
{
    private const int PollIntervalSeconds = 30;

    private readonly AgentRunnerConfig _config;
    private readonly ArtifactManager _artifactManager;
    private readonly IEventLogger _logger;

    private TaskCompletionSource<bool>? _forceSignal;

    public DateTime NextScheduledRunTime { get; private set; }

    public CycleScheduler(
        AgentRunnerConfig config,
        ArtifactManager artifactManager,
        IEventLogger logger)
    {
        _config = config;
        _artifactManager = artifactManager;
        _logger = logger;
    }

    /// <summary>
    /// Waits for the configured cycle interval (or a force signal), then returns the next
    /// state and a freshly-created cycle ID.
    /// </summary>
    /// <param name="completedCycle">The cycle that just finished, if any. Its ID is recorded
    /// as the latest previous cycle and its end time is stamped here.</param>
    /// <param name="latestPreviousCycleId">The most recent cycle ID with a completed strategy.
    /// If non-null, the next cycle starts with Evaluate; otherwise it starts with Plan.</param>
    /// <param name="onCycleCompleted">Callback fired when the completed cycle is closed out.</param>
    public async Task<(LoopState NextState, string NewCycleId)> WaitAsync(
        CycleContext? completedCycle,
        string? latestPreviousCycleId,
        Action<string>? onCycleCompleted,
        CancellationToken ct)
    {
        var previousCycleId = latestPreviousCycleId;

        if (completedCycle != null)
        {
            previousCycleId = completedCycle.CycleId;
            completedCycle.CycleEndTime = DateTime.UtcNow;
            onCycleCompleted?.Invoke(completedCycle.CycleId);

            try
            {
                _artifactManager.ArchiveOldCycles();
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Cycle archival failed (non-fatal): {ex.Message}");
            }
        }

        _forceSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var totalWaited = TimeSpan.Zero;

        while (true)
        {
            var targetInterval = TimeSpan.FromMinutes(_config.Cycle.ScheduleIntervalMinutes);
            NextScheduledRunTime = DateTime.UtcNow + (targetInterval - totalWaited);

            if (totalWaited >= targetInterval)
                break;

            var remaining = targetInterval - totalWaited;
            var tick = TimeSpan.FromSeconds(Math.Min(PollIntervalSeconds, remaining.TotalSeconds));

            var delayTask = Task.Delay(tick, ct);
            var completed = await Task.WhenAny(delayTask, _forceSignal.Task);

            ct.ThrowIfCancellationRequested();

            if (completed == _forceSignal.Task)
            {
                _logger.LogInfo("Force-cycle signal received; starting new cycle immediately.");
                break;
            }

            totalWaited += tick;

            // Re-check on each tick so live config changes take effect within one tick window
            if (totalWaited >= TimeSpan.FromMinutes(_config.Cycle.ScheduleIntervalMinutes))
                break;
        }

        var newCycleId = _artifactManager.CreateCycleDirectory();
        var nextState = previousCycleId != null ? LoopState.Evaluate : LoopState.Plan;

        _logger.LogInfo(previousCycleId != null
            ? $"Starting Evaluate step for previous cycle {previousCycleId}"
            : "No previous cycle; starting with Plan step");

        return (nextState, newCycleId);
    }

    /// <summary>Interrupts the current wait and starts a new cycle immediately.</summary>
    public void ForceNextCycle() => _forceSignal?.TrySetResult(true);
}

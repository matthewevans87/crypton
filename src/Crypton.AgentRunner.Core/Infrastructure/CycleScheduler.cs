using AgentRunner.Abstractions;
using AgentRunner.Configuration;

namespace AgentRunner.Infrastructure;

/// <summary>
/// Determines when the next cycle should run based on a configured interval.
/// Supports a force-signal to advance immediately.
/// </summary>
public sealed class CycleScheduler : ICycleScheduler
{
    private readonly AgentRunnerConfig _config;
    private volatile bool _forceNext;

    public DateTimeOffset NextScheduledAt { get; private set; } = DateTimeOffset.UtcNow;

    public CycleScheduler(AgentRunnerConfig config)
    {
        _config = config;
    }

    public bool IsDue(DateTimeOffset lastCycleEnd)
    {
        if (_forceNext)
            return true;

        var interval = TimeSpan.FromMinutes(_config.Cycle.ScheduleIntervalMinutes);
        return DateTimeOffset.UtcNow >= lastCycleEnd + interval;
    }

    public TimeSpan TimeUntilNext(DateTimeOffset lastCycleEnd)
    {
        if (_forceNext)
            return TimeSpan.Zero;

        var interval = TimeSpan.FromMinutes(_config.Cycle.ScheduleIntervalMinutes);
        var due = lastCycleEnd + interval;
        var remaining = due - DateTimeOffset.UtcNow;
        NextScheduledAt = due;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    public void ForceNext()
    {
        _forceNext = true;
        NextScheduledAt = DateTimeOffset.UtcNow;
    }

    internal void ResetForce() => _forceNext = false;
}

using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Infrastructure;
using Xunit;

namespace AgentRunner.Tests.Agents;

/// <summary>Tests for CycleScheduler scheduling logic.</summary>
public class CycleSchedulerTests
{
    private static CycleScheduler MakeScheduler(int intervalMinutes = 60)
        => new(new AgentRunnerConfig
        {
            Cycle = new CycleConfig { ScheduleIntervalMinutes = intervalMinutes }
        });

    [Fact]
    public void IsDue_WhenForced_ReturnsTrue()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        scheduler.ForceNext();
        Assert.True(scheduler.IsDue(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsDue_WhenIntervalNotElapsed_ReturnsFalse()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        Assert.False(scheduler.IsDue(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsDue_WhenIntervalElapsed_ReturnsTrue()
    {
        var scheduler = MakeScheduler(intervalMinutes: 1);
        var lastCycleEnd = DateTimeOffset.UtcNow.AddMinutes(-2);
        Assert.True(scheduler.IsDue(lastCycleEnd));
    }

    [Fact]
    public void TimeUntilNext_WhenForced_ReturnsZero()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        scheduler.ForceNext();
        Assert.Equal(TimeSpan.Zero, scheduler.TimeUntilNext(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void NextScheduledAt_UpdatedAfterTimeUntilNext()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        var lastCycleEnd = DateTimeOffset.UtcNow;
        scheduler.TimeUntilNext(lastCycleEnd);
        Assert.True(scheduler.NextScheduledAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public void TimeUntilNext_WhenOverdue_ReturnsZero()
    {
        var scheduler = MakeScheduler(intervalMinutes: 1);
        var lastCycleEnd = DateTimeOffset.UtcNow.AddMinutes(-5);
        Assert.Equal(TimeSpan.Zero, scheduler.TimeUntilNext(lastCycleEnd));
    }
}

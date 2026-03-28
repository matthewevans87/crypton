using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Infrastructure;
using Xunit;

namespace AgentRunner.Tests.Agents;

public class CycleSchedulerAdditionalTests
{
    private static CycleScheduler MakeScheduler(int intervalMinutes)
        => new(new AgentRunnerConfig
        {
            Cycle = new CycleConfig { ScheduleIntervalMinutes = intervalMinutes }
        });

    [Fact]
    public void ForceNext_MakesIsDueReturnTrue()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        Assert.False(scheduler.IsDue(DateTimeOffset.UtcNow));
        scheduler.ForceNext();
        Assert.True(scheduler.IsDue(DateTimeOffset.UtcNow));
    }

    [Fact]
    public void IsDue_ZeroInterval_AlwaysTrue()
    {
        var scheduler = MakeScheduler(intervalMinutes: 0);
        Assert.True(scheduler.IsDue(DateTimeOffset.UtcNow.AddMinutes(-1)));
    }

    [Fact]
    public void TimeUntilNext_ReturnPositive_WhenNotDue()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        var remaining = scheduler.TimeUntilNext(DateTimeOffset.UtcNow);
        Assert.True(remaining > TimeSpan.Zero);
    }
}

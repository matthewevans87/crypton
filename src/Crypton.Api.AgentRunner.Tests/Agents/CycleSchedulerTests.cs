using AgentRunner.Agents;
using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using AgentRunner.StateMachine;
using Moq;
using Xunit;

namespace AgentRunner.Tests.Agents;

public class CycleSchedulerTests : IDisposable
{
    private readonly string _tempPath;
    private readonly ArtifactManager _artifactManager;

    public CycleSchedulerTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), $"scheduler_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempPath);

        _artifactManager = new ArtifactManager(new StorageConfig
        {
            BasePath = _tempPath,
            CyclesPath = "cycles",
            MemoryPath = "memory",
            ArchiveRetentionCount = 5
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempPath))
            Directory.Delete(_tempPath, true);
    }

    private CycleScheduler MakeScheduler(int intervalMinutes = 0)
    {
        var config = new AgentRunnerConfig
        {
            Cycle = new CycleConfig { ScheduleIntervalMinutes = intervalMinutes }
        };
        return new CycleScheduler(config, _artifactManager, new Mock<IEventLogger>().Object);
    }

    [Fact]
    public async Task WaitAsync_WithNoPreviousCycle_ReturnsLoopStatePlan()
    {
        var scheduler = MakeScheduler(intervalMinutes: 0);

        var (nextState, _) = await scheduler.WaitAsync(null, null, null, CancellationToken.None);

        Assert.Equal(LoopState.Plan, nextState);
    }

    [Fact]
    public async Task WaitAsync_WithPreviousCycleId_ReturnsLoopStateEvaluate()
    {
        var scheduler = MakeScheduler(intervalMinutes: 0);

        var (nextState, _) = await scheduler.WaitAsync(null, "some_cycle_id", null, CancellationToken.None);

        Assert.Equal(LoopState.Evaluate, nextState);
    }

    [Fact]
    public async Task WaitAsync_WithCompletedCycle_ReturnsLoopStateEvaluate()
    {
        var scheduler = MakeScheduler(intervalMinutes: 0);
        var cycleId = _artifactManager.CreateCycleDirectory();
        var completedCycle = new CycleContext { CycleId = cycleId };

        var (nextState, _) = await scheduler.WaitAsync(completedCycle, null, null, CancellationToken.None);

        Assert.Equal(LoopState.Evaluate, nextState);
    }

    [Fact]
    public async Task WaitAsync_ReturnsNewCycleId()
    {
        var scheduler = MakeScheduler(intervalMinutes: 0);

        var (_, newCycleId) = await scheduler.WaitAsync(null, null, null, CancellationToken.None);

        Assert.False(string.IsNullOrEmpty(newCycleId));
    }

    [Fact]
    public async Task WaitAsync_WithCompletedCycle_InvokesCycleCompletedCallback()
    {
        var scheduler = MakeScheduler(intervalMinutes: 0);
        var cycleId = _artifactManager.CreateCycleDirectory();
        var completedCycle = new CycleContext { CycleId = cycleId };
        string? callbackCycleId = null;

        await scheduler.WaitAsync(completedCycle, null, id => callbackCycleId = id, CancellationToken.None);

        Assert.Equal(cycleId, callbackCycleId);
    }

    [Fact]
    public async Task WaitAsync_WithForceSignal_CompletesBeforeFullInterval()
    {
        // 60-minute interval — would time out the test without a force signal
        var scheduler = MakeScheduler(intervalMinutes: 60);

        var waitTask = scheduler.WaitAsync(null, null, null, CancellationToken.None);

        // Allow the scheduler to enter its wait loop
        await Task.Delay(50);
        scheduler.ForceNextCycle();

        var (_, cycleId) = await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(string.IsNullOrEmpty(cycleId));
    }

    [Fact]
    public async Task WaitAsync_WhenCancelled_Throws()
    {
        var scheduler = MakeScheduler(intervalMinutes: 60);
        using var cts = new CancellationTokenSource();

        var waitTask = scheduler.WaitAsync(null, null, null, cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
    }
}

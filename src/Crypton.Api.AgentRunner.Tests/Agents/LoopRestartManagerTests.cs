using AgentRunner.Agents;
using AgentRunner.Configuration;
using AgentRunner.Logging;
using Moq;
using Xunit;

namespace AgentRunner.Tests.Agents;

public class LoopRestartManagerTests
{
    private static AgentRunnerConfig MakeConfig(int maxAttempts = 3, int baseDelayMinutes = 0, int maxDelayMinutes = 0)
        => new()
        {
            Resilience = new ResilienceConfig
            {
                MaxRestartAttempts = maxAttempts,
                BaseRestartDelayMinutes = baseDelayMinutes,
                MaxRestartDelayMinutes = maxDelayMinutes
            }
        };

    [Fact]
    public async Task ShouldRestartAsync_WhenBelowMaxAttempts_ReturnsTrue()
    {
        var logger = new Mock<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger.Object);

        var result = await manager.ShouldRestartAsync(1, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenAtMaxAttempts_ReturnsFalse()
    {
        var logger = new Mock<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger.Object);

        var result = await manager.ShouldRestartAsync(3, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenExceedsMaxAttempts_ReturnsFalse()
    {
        var logger = new Mock<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger.Object);

        var result = await manager.ShouldRestartAsync(99, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenAtMaxAttempts_LogsError()
    {
        var logger = new Mock<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger.Object);

        await manager.ShouldRestartAsync(3, CancellationToken.None);

        logger.Verify(l => l.LogError(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenBelowMaxAttempts_LogsWarning()
    {
        var logger = new Mock<IEventLogger>();
        // Zero delay so the test does not actually wait
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3, baseDelayMinutes: 0, maxDelayMinutes: 0), logger.Object);

        await manager.ShouldRestartAsync(1, CancellationToken.None);

        logger.Verify(l => l.LogWarning(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenCancelled_Throws()
    {
        var logger = new Mock<IEventLogger>();
        // Use 1-minute delay so the task actually waits and can be cancelled
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 5, baseDelayMinutes: 1, maxDelayMinutes: 30), logger.Object);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.ShouldRestartAsync(1, cts.Token));
    }
}

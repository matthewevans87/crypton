using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Infrastructure;
using NSubstitute;
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
        var logger = Substitute.For<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger);
        Assert.True(await manager.ShouldRestartAsync(1, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenAtMaxAttempts_ReturnsFalse()
    {
        var logger = Substitute.For<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger);
        Assert.False(await manager.ShouldRestartAsync(3, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenExceedsMaxAttempts_ReturnsFalse()
    {
        var logger = Substitute.For<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger);
        Assert.False(await manager.ShouldRestartAsync(99, CancellationToken.None));
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenAtMaxAttempts_LogsError()
    {
        var logger = Substitute.For<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3), logger);
        await manager.ShouldRestartAsync(3, CancellationToken.None);
        logger.Received(1).LogError(Arg.Any<string>());
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenBelowMaxAttempts_LogsWarning()
    {
        var logger = Substitute.For<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 3, baseDelayMinutes: 0, maxDelayMinutes: 0), logger);
        await manager.ShouldRestartAsync(1, CancellationToken.None);
        logger.Received(1).LogWarning(Arg.Any<string>());
    }

    [Fact]
    public async Task ShouldRestartAsync_WhenCancelled_Throws()
    {
        var logger = Substitute.For<IEventLogger>();
        var manager = new LoopRestartManager(MakeConfig(maxAttempts: 5, baseDelayMinutes: 1, maxDelayMinutes: 30), logger);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.ShouldRestartAsync(1, cts.Token));
    }
}

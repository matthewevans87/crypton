using AgentRunner.Configuration;
using AgentRunner.Execution;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class ResilientToolExecutorTests
{
    private static AgentRunnerConfig MakeConfig(int maxRetries = 0, int timeoutSeconds = 30, int maxDelaySeconds = 1)
        => new()
        {
            Tools = new ToolConfig
            {
                MaxRetries = maxRetries,
                DefaultTimeoutSeconds = timeoutSeconds,
                MaxRetryDelaySeconds = maxDelaySeconds
            }
        };

    [Fact]
    public async Task ExecuteWithResilienceAsync_Success_ReturnsResult()
    {
        var executor = new ResilientToolExecutor(MakeConfig());
        var result = await executor.ExecuteWithResilienceAsync("test", ct => Task.FromResult("ok"));
        Assert.Equal("ok", result);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_NonTransientError_ThrowsImmediately()
    {
        var attempts = 0;
        var executor = new ResilientToolExecutor(MakeConfig(maxRetries: 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteWithResilienceAsync<string>("test", _ =>
            {
                attempts++;
                throw new InvalidOperationException("hard error");
            }));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_TransientError_IsRetried()
    {
        var attempts = 0;
        var executor = new ResilientToolExecutor(MakeConfig(maxRetries: 2));
        await Assert.ThrowsAnyAsync<Exception>(() =>
            executor.ExecuteWithResilienceAsync<string>("test", _ =>
            {
                attempts++;
                throw new HttpRequestException("timeout");
            }));
        Assert.Equal(3, attempts); // 1 initial + 2 retries
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_SucceedsOnRetry_ReturnsResult()
    {
        var attempts = 0;
        var executor = new ResilientToolExecutor(MakeConfig(maxRetries: 2));
        var result = await executor.ExecuteWithResilienceAsync("test", ct =>
        {
            attempts++;
            if (attempts < 2) throw new HttpRequestException("timeout");
            return Task.FromResult("success on retry");
        });
        Assert.Equal("success on retry", result);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task ExecuteWithResilienceAsync_Cancellation_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var executor = new ResilientToolExecutor(MakeConfig());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            executor.ExecuteWithResilienceAsync("test", ct =>
            {
                ct.ThrowIfCancellationRequested();
                return Task.FromResult("x");
            }, cts.Token));
    }
}

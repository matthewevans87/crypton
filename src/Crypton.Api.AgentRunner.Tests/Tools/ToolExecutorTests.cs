using System.Diagnostics;
using AgentRunner.Tools;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class ToolExecutorTests
{
    [Fact]
    public void RegisterTool_AddsToCollection()
    {
        var executor = new ToolExecutor(30);
        var tool = new TestTool();
        
        executor.RegisterTool(tool);
        
        var retrieved = executor.GetTool("test_tool");
        Assert.NotNull(retrieved);
        Assert.Equal("test_tool", retrieved.Name);
    }

    [Fact]
    public void GetTool_UnknownTool_ReturnsNull()
    {
        var executor = new ToolExecutor(30);
        
        var result = executor.GetTool("nonexistent");
        
        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsResult()
    {
        var executor = new ToolExecutor(30);
        executor.RegisterTool(new TestTool());
        
        var call = new ToolCall
        {
            ToolName = "test_tool",
            Parameters = new Dictionary<string, object> { { "value", 42 } }
        };
        
        var result = await executor.ExecuteAsync(call);
        
        Assert.True(result.Success);
        Assert.Equal("Success: 42", result.Data);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownTool_ReturnsError()
    {
        var executor = new ToolExecutor(30);
        
        var call = new ToolCall { ToolName = "unknown" };
        var result = await executor.ExecuteAsync(call);
        
        Assert.False(result.Success);
        Assert.Contains("not found", result.Error);
    }

    [Fact]
    public async Task ExecuteBatchAsync_RunsAll()
    {
        var executor = new ToolExecutor(30);
        executor.RegisterTool(new TestTool());
        
        var calls = new List<ToolCall>
        {
            new() { ToolName = "test_tool", Parameters = new() { { "value", 1 } } },
            new() { ToolName = "test_tool", Parameters = new() { { "value", 2 } } }
        };
        
        var results = await executor.ExecuteBatchAsync(calls);
        
        Assert.Equal(2, results.Count);
        Assert.True(results.All(r => r.Success));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Retry / exponential-backoff behaviour
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TooManyRequests")]
    [InlineData("too many requests")]
    [InlineData("429")]
    [InlineData("ratelimit")]
    [InlineData("rate limit")]
    [InlineData("timeout")]
    [InlineData("timed out")]
    [InlineData("connection")]
    [InlineData("unavailable")]
    [InlineData("502")]
    [InlineData("503")]
    public async Task ExecuteAsync_TransientError_IsRetried(string errorMessage)
    {
        // Tool fails once with a transient error, then succeeds on the next attempt.
        var executor = new ToolExecutor(
            defaultTimeoutSeconds: 30,
            maxRetries: 3,
            maxRetryDelaySeconds: 0); // zero delay for fast tests
        var tool = new CountingFailTool(failCount: 1, errorMessage: errorMessage);
        executor.RegisterTool(tool);

        var result = await executor.ExecuteAsync(new ToolCall { ToolName = tool.Name });

        Assert.True(result.Success, $"Expected success after retrying transient error '{errorMessage}'");
        Assert.Equal(2, tool.InvocationCount); // 1 fail + 1 success
    }

    [Fact]
    public async Task ExecuteAsync_NonTransientError_IsNotRetried()
    {
        var executor = new ToolExecutor(
            defaultTimeoutSeconds: 30,
            maxRetries: 3,
            maxRetryDelaySeconds: 0);
        var tool = new CountingFailTool(failCount: 99, errorMessage: "some unexpected business error");
        executor.RegisterTool(tool);

        var result = await executor.ExecuteAsync(new ToolCall { ToolName = tool.Name });

        Assert.False(result.Success);
        Assert.Equal(1, tool.InvocationCount); // tried once, not retried
    }

    [Fact]
    public async Task ExecuteAsync_TransientError_ExhaustsAllRetries_ReturnsFailure()
    {
        const int maxRetries = 2;
        var executor = new ToolExecutor(
            defaultTimeoutSeconds: 30,
            maxRetries: maxRetries,
            maxRetryDelaySeconds: 0);
        var tool = new CountingFailTool(failCount: 99, errorMessage: "TooManyRequests");
        executor.RegisterTool(tool);

        var result = await executor.ExecuteAsync(new ToolCall { ToolName = tool.Name });

        Assert.False(result.Success);
        Assert.Equal(maxRetries + 1, tool.InvocationCount); // 1 initial + maxRetries
    }

    [Fact]
    public async Task ExecuteAsync_MaxRetryDelaySeconds_CapsExponentialBackoff()
    {
        // With cap=1s and 3 retries the delays are 1+1+1=3s.
        // Without cap they would be 1+2+4=7s.
        // We verify the whole call completes well within 6 seconds.
        const int maxRetries = 3;
        const int capSeconds = 1;
        var executor = new ToolExecutor(
            defaultTimeoutSeconds: 30,
            maxRetries: maxRetries,
            maxRetryDelaySeconds: capSeconds);
        var tool = new CountingFailTool(failCount: 99, errorMessage: "TooManyRequests");
        executor.RegisterTool(tool);

        var sw = Stopwatch.StartNew();
        await executor.ExecuteAsync(new ToolCall { ToolName = tool.Name });
        sw.Stop();

        // 3 retries × 1s cap = ~3s; 7s would indicate the cap isn't working.
        Assert.True(sw.Elapsed.TotalSeconds < 6,
            $"Elapsed {sw.Elapsed.TotalSeconds:F1}s suggests backoff cap is not being applied (expected < 6s)");
        Assert.Equal(maxRetries + 1, tool.InvocationCount);
    }

    [Fact]
    public async Task ExecuteAsync_TransientError_RetriesSucceedEventually()
    {
        // Fails twice with 429 then succeeds on the 3rd attempt.
        var executor = new ToolExecutor(
            defaultTimeoutSeconds: 30,
            maxRetries: 3,
            maxRetryDelaySeconds: 0);
        var tool = new CountingFailTool(failCount: 2, errorMessage: "429");
        executor.RegisterTool(tool);

        var result = await executor.ExecuteAsync(new ToolCall { ToolName = tool.Name });

        Assert.True(result.Success);
        Assert.Equal(3, tool.InvocationCount); // 2 fails + 1 success
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Test helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Fails <paramref name="failCount"/> times with the given error, then succeeds.</summary>
    private sealed class CountingFailTool : Tool
    {
        private readonly int _failCount;
        private readonly string _errorMessage;
        public int InvocationCount { get; private set; }

        public CountingFailTool(int failCount, string errorMessage)
        {
            _failCount = failCount;
            _errorMessage = errorMessage;
        }

        public override string Name => "counting_fail_tool";
        public override string Description => "Fails a fixed number of times then succeeds.";
        public override ToolParameterSchema? Parameters => null;

        public override Task<ToolResult> ExecuteAsync(
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            if (InvocationCount <= _failCount)
                return Task.FromResult(new ToolResult { Success = false, Error = _errorMessage });

            return Task.FromResult(new ToolResult { Success = true, Data = "ok" });
        }
    }

    private class TestTool : Tool
    {
        public override string Name => "test_tool";
        public override string Description => "A test tool";
        public override ToolParameterSchema? Parameters => new()
        {
            Type = "object",
            Properties = new()
            {
                ["value"] = new() { Type = "integer" }
            }
        };

        public override Task<ToolResult> ExecuteAsync(Dictionary<string, object> parameters, CancellationToken cancellationToken = default)
        {
            var value = parameters.TryGetValue("value", out var v) ? v.ToString() : "nothing";
            return Task.FromResult(new ToolResult
            {
                Success = true,
                Data = $"Success: {value}"
            });
        }
    }
}

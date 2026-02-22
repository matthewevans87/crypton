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

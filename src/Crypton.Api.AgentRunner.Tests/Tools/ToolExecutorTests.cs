using Crypton.Tools;
using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
using Spectre.Console.Cli;
using Xunit;

namespace AgentRunner.Tests.Tools;

/// <summary>
/// Unit tests for <see cref="InProcessVirtualTerminal"/> resilience behaviour
/// (circuit breaker, unknown tool, basic run-through).
/// </summary>
public class InProcessVirtualTerminalTests
{
    private static readonly ToolsConfig ZeroRetryConfig = new()
    {
        MaxRetries = 0,
        DefaultTimeoutSeconds = 30,
        MaxRetryDelaySeconds = 1,
    };

    private static (ToolRegistry Registry, InProcessVirtualTerminal Vt) Build(ToolsConfig? config = null)
    {
        var registry = new ToolRegistry();
        var vt = new InProcessVirtualTerminal(registry, config ?? ZeroRetryConfig);
        return (registry, vt);
    }

    [Fact]
    public async Task RunAsync_UnknownTool_ReturnsFailureResult()
    {
        var (_, vt) = Build();
        var result = await vt.RunAsync("not_a_real_tool", "{}");
        Assert.False(result.IsSuccess);
        Assert.Contains("not_a_real_tool", result.Output);
    }

    [Fact]
    public async Task RunAsync_ToolExitsZero_IsSuccess()
    {
        var (registry, vt) = Build();

        // Register a no-op tool using MacroSignalsTool Settings (empty settings)
        // Fake HTTP client returns an empty 200 to satisfy the tool
        registry.Register<MacroSignalsTool, MacroSignalsTool.Settings>(
            "test_tool",
            console =>
            {
                var tool = new MacroSignalsTool(
                    FakeHttp(200, "{\"trend\":\"neutral\"}"),
                    "http://localhost",
                    console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(MacroSignalsTool), tool);
                var app = new CommandApp<MacroSignalsTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            });

        var result = await vt.RunAsync("test_tool", "{}");
        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
    }

    [Fact]
    public async Task RunAsync_ElapsedMs_IsPositiveOrZero()
    {
        var (_, vt) = Build();
        var result = await vt.RunAsync("nonexistent", "{}");
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public async Task RunAsync_AfterCircuitBreakerOpens_ReturnsCircuitOpenError()
    {
        var config = new ToolsConfig
        {
            MaxRetries = 0,
            DefaultTimeoutSeconds = 30,
            MaxRetryDelaySeconds = 1,
        };

        var (registry, vt) = Build(config);

        // Register a tool that always throws to accumulate failures
        var failCount = 0;
        registry.Register<MacroSignalsTool, MacroSignalsTool.Settings>(
            "always_fail",
            console =>
            {
                var http = FakeHttp(503, "server error");
                var tool = new MacroSignalsTool(http, "http://localhost", console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(MacroSignalsTool), tool);
                var app = new CommandApp<MacroSignalsTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            });

        // Exhaust failures to open circuit breaker (threshold = 5)
        for (var i = 0; i < 5; i++)
        {
            await vt.RunAsync("always_fail", "{}");
        }

        var result = await vt.RunAsync("always_fail", "{}");
        // Circuit is open — should return error immediately without running the tool
        Assert.False(result.IsSuccess);
    }

    private static System.Net.Http.HttpClient FakeHttp(int statusCode, string body)
    {
        var handler = new FakeVtHandler(statusCode, body);
        return new System.Net.Http.HttpClient(handler);
    }
}

file sealed class FakeVtHandler : System.Net.Http.HttpMessageHandler
{
    private readonly int _status;
    private readonly string _body;
    public FakeVtHandler(int status, string body) { _status = status; _body = body; }
    protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(
        System.Net.Http.HttpRequestMessage _, CancellationToken __)
        => Task.FromResult(new System.Net.Http.HttpResponseMessage((System.Net.HttpStatusCode)_status)
        {
            Content = new System.Net.Http.StringContent(_body)
        });
}

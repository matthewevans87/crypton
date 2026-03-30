using System.Net;
using System.Text;
using System.Text.Json;
using Crypton.Tools;
using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
using Spectre.Console.Cli;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class BirdToolTests
{
    private static readonly ToolsConfig Config = new() { MaxRetries = 0, DefaultTimeoutSeconds = 30 };

    private static (ToolRegistry Registry, InProcessVirtualTerminal Vt) BuildVt(
        HttpClient http, string baseUrl = "http://localhost:11435")
    {
        var registry = new ToolRegistry();
        registry.Register<BirdTool, BirdTool.Settings>(
            "bird",
            console =>
            {
                var tool = new BirdTool(http, baseUrl, console);
                var reg = new InProcessTypeRegistrar();
                reg.RegisterInstance(typeof(BirdTool), tool);
                var app = new CommandApp<BirdTool>(reg);
                app.Configure(c => c.PropagateExceptions());
                return app;
            });
        return (registry, new InProcessVirtualTerminal(registry, Config));
    }

    private static HttpClient BirdHttp(int exitCode, string? stdout = null, string? stderr = null)
    {
        var body = JsonSerializer.Serialize(new
        {
            exitCode,
            stdout = stdout ?? "[]",
            stderr = stderr ?? "",
        });
        return new HttpClient(new BirdFakeHandler(HttpStatusCode.OK, body));
    }

    [Fact]
    public async Task RunAsync_SearchMode_ReturnsSuccess()
    {
        var tweets = JsonSerializer.Serialize(new[] { new { id = "1", text = "BTC ATH" } });
        var (_, vt) = BuildVt(BirdHttp(0, tweets));

        var result = await vt.RunAsync("bird", "{\"query\": \"BTC\"}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
        Assert.DoesNotContain("Error:", result.Output);
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_WritesErrorOutput()
    {
        var (_, vt) = BuildVt(BirdHttp(1, "", "auth failed"));

        var result = await vt.RunAsync("bird", "{\"query\": \"BTC\"}");

        // Tool returns exit code 1 → IsSuccess = false, output should mention error
        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_BirdServerDown_ReturnsFailure()
    {
        var http = new HttpClient(new BirdFakeHandler(HttpStatusCode.ServiceUnavailable, "{}"));
        var (_, vt) = BuildVt(http);

        var result = await vt.RunAsync("bird", "{\"query\": \"BTC\"}");

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task RunAsync_WithModeAndCount_ReturnsSuccess()
    {
        var tweets = JsonSerializer.Serialize(new[] { new { id = "1", text = "test" } });
        var (_, vt) = BuildVt(BirdHttp(0, tweets));

        var result = await vt.RunAsync("bird",
            "{\"query\": \"bitcoin ETF\", \"mode\": \"search\", \"count\": 5}");

        Assert.True(result.IsSuccess, $"Expected success. Output: {result.Output}");
    }
}

file sealed class BirdFakeHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _content;

    public BirdFakeHandler(HttpStatusCode status, string content)
    {
        _status = status;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage _, CancellationToken __)
        => Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json")
        });
}

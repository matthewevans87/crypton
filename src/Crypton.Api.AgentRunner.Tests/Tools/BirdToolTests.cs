using System.Net;
using System.Text;
using System.Text.Json;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Execution;
using AgentRunner.Execution.Tools;
using Microsoft.Extensions.AI;
using NSubstitute;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class BirdToolTests
{
    private static BirdTool CreateTool(HttpClient httpClient, string baseUrl = "http://localhost:11435")
    {
        var executor = new ResilientToolExecutor(new AgentRunnerConfig
        {
            Tools = new ToolConfig { MaxRetries = 0, DefaultTimeoutSeconds = 30 }
        });
        return new BirdTool(httpClient, baseUrl, executor);
    }

    private static HttpClient CreateHttpClient(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new FakeHttpMessageHandler(status, responseJson);
        return new HttpClient(handler);
    }

    [Fact]
    public void Name_IsBird()
    {
        var tool = CreateTool(new HttpClient());
        Assert.Equal("bird", tool.Name);
    }

    [Fact]
    public async Task ExecuteAsync_SearchMode_ReturnsStdout()
    {
        var tweets = JsonSerializer.Serialize(new[] { new { id = "1", text = "hello" } });
        var json = JsonSerializer.Serialize(new { exitCode = 0, stdout = tweets, stderr = "" });
        var tool = CreateTool(CreateHttpClient(json));
        var fn = tool.AsAIFunction();

        var result = (await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "BTC" })))?.ToString();
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_NonZeroExitCode_ReturnsError()
    {
        var json = JsonSerializer.Serialize(new { exitCode = 1, stdout = "", stderr = "auth failed" });
        var tool = CreateTool(CreateHttpClient(json));
        var fn = tool.AsAIFunction();

        var result = (await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "BTC" })))?.ToString();
        Assert.Contains("Error:", result);
    }

    [Fact]
    public async Task ExecuteAsync_HttpError_ReturnsError()
    {
        var tool = CreateTool(CreateHttpClient("{}", HttpStatusCode.ServiceUnavailable));
        var fn = tool.AsAIFunction();

        var result = (await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["query"] = "BTC" })))?.ToString();
        Assert.Contains("Error:", result);
    }
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _content;

    public FakeHttpMessageHandler(HttpStatusCode status, string content)
    {
        _status = status;
        _content = content;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(_status)
        {
            Content = new StringContent(_content, Encoding.UTF8, "application/json")
        });
}

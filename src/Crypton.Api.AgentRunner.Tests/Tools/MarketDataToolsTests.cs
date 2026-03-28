using System.Net;
using System.Text;
using System.Text.Json;
using AgentRunner.Configuration;
using AgentRunner.Execution;
using AgentRunner.Execution.Tools;
using Microsoft.Extensions.AI;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class MarketDataToolsTests
{
    private static HttpClient CreateHttpClient(object responseObj, HttpStatusCode status = HttpStatusCode.OK)
    {
        var json = JsonSerializer.Serialize(responseObj);
        var handler = new FakeMarketDataHttpHandler(status, json);
        return new HttpClient(handler);
    }

    private static ResilientToolExecutor CreateExecutor()
        => new(new AgentRunnerConfig { Tools = new ToolConfig { MaxRetries = 0, DefaultTimeoutSeconds = 30 } });

    [Fact]
    public async Task GetPriceTool_ReturnsPrice()
    {
        var response = new { symbol = "BTC/USD", price = 65000.0m, timestamp = "2025-01-01T00:00:00Z" };
        var tool = new GetPriceTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());
        var fn = tool.AsAIFunction();

        var result = (await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["symbol"] = "BTC/USD" })))?.ToString();
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task GetPriceTool_HttpError_Throws()
    {
        var tool = new GetPriceTool(CreateHttpClient(new { }, HttpStatusCode.ServiceUnavailable), "http://localhost:5002", CreateExecutor());
        var fn = tool.AsAIFunction();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?> { ["symbol"] = "BTC/USD" })).AsTask());
    }

    [Fact]
    public async Task CurrentPositionTool_ReturnsPositionData()
    {
        var response = new { mode = "paper", balance = new { availableUsd = 10000.0m }, openPositions = new object[] { } };
        var tool = new CurrentPositionTool(CreateHttpClient(response), "http://localhost:5001", CreateExecutor());
        var fn = tool.AsAIFunction();

        var result = (await fn.InvokeAsync(new AIFunctionArguments(new Dictionary<string, object?>())))?.ToString();
        Assert.NotNull(result);
    }
}

internal sealed class FakeMarketDataHttpHandler : HttpMessageHandler
{
    private readonly HttpStatusCode _status;
    private readonly string _content;

    public FakeMarketDataHttpHandler(HttpStatusCode status, string content)
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

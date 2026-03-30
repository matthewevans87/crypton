using System.Net;
using System.Text;
using System.Text.Json;
using Crypton.Tools.Configuration;
using Crypton.Tools.Infrastructure;
using Crypton.Tools.Tools;
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
        => new(new ToolsConfig { MaxRetries = 0, DefaultTimeoutSeconds = 30 });

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

    // ─── InvokeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPriceTool_InvokeAsync_DefaultSymbol_ReturnsResult()
    {
        var response = new { symbol = "BTC/USD", price = 65000.0m };
        var tool = new GetPriceTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task GetPriceTool_InvokeAsync_ExplicitSymbols_ReturnsResult()
    {
        var response = new { symbol = "ETH/USD", price = 3000.0m };
        var tool = new GetPriceTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string> { ["symbols"] = "ETH,SOL" });
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task CurrentPositionTool_InvokeAsync_NoParams_ReturnsResult()
    {
        var response = new { mode = "paper", totalValue = 10000.0m };
        var tool = new CurrentPositionTool(CreateHttpClient(response), "http://localhost:5001", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task MacroSignalsTool_InvokeAsync_NoParams_ReturnsResult()
    {
        var response = new { trend = "bullish", fearGreedIndex = 65 };
        var tool = new MacroSignalsTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task OrderBookTool_InvokeAsync_MissingAsset_ReturnsError()
    {
        var tool = new OrderBookTool(CreateHttpClient(new { }), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task OrderBookTool_InvokeAsync_ValidAsset_ReturnsResult()
    {
        var response = new { symbol = "BTC/USD", bids = new object[] { }, asks = new object[] { } };
        var tool = new OrderBookTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string> { ["asset"] = "BTC" });
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task OrderBookTool_InvokeAsync_InvalidDepth_UsesDefault()
    {
        var response = new { symbol = "BTC/USD", bids = new object[] { }, asks = new object[] { } };
        var tool = new OrderBookTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());

        // "notanumber" should fall back to depth=10 without throwing
        var result = await tool.InvokeAsync(new Dictionary<string, string>
        {
            ["asset"] = "BTC",
            ["depth"] = "notanumber"
        });
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task TechnicalIndicatorsTool_InvokeAsync_MissingAsset_ReturnsError()
    {
        var tool = new TechnicalIndicatorsTool(CreateHttpClient(new { }), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task TechnicalIndicatorsTool_InvokeAsync_ValidParams_ReturnsResult()
    {
        var response = new { symbol = "BTC/USD", timeframe = "1d", rsi = 55.0m };
        var tool = new TechnicalIndicatorsTool(CreateHttpClient(response), "http://localhost:5002", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "4h"
        });
        Assert.NotNull(result);
        Assert.DoesNotContain("Error:", result);
    }

    [Fact]
    public async Task WebSearchTool_InvokeAsync_MissingQuery_ReturnsError()
    {
        var tool = new WebSearchTool(CreateHttpClient(new { }), "fake-api-key", CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task WebFetchTool_InvokeAsync_MissingUrl_ReturnsError()
    {
        var tool = new WebFetchTool(CreateHttpClient(new { }), CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>());
        Assert.StartsWith("Error:", result);
    }

    [Fact]
    public async Task WebFetchTool_InvokeAsync_ValidUrl_ReturnsResult()
    {
        var html = "<html><body><p>Hello world</p></body></html>";
        var handler = new FakeMarketDataHttpHandler(HttpStatusCode.OK, html);
        var tool = new WebFetchTool(new HttpClient(handler), CreateExecutor());

        var result = await tool.InvokeAsync(new Dictionary<string, string>
        {
            ["url"] = "https://example.com"
        });
        Assert.NotNull(result);
        Assert.Contains("Hello world", result);
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

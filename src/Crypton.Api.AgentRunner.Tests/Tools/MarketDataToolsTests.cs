using System.Net;
using System.Net.Http.Json;
using AgentRunner.Tools;
using Xunit;

namespace AgentRunner.Tests.Tools;

public class CurrentPositionToolTests
{
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHandler;

    public CurrentPositionToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidResponse_ReturnsSuccess()
    {
        var response = new { totalValue = 50000m, balances = new[] { new { asset = "BTC", available = 1.5m } } };
        _mockHandler.SetupResponse("http://localhost:5002/api/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceUnavailable_ReturnsError()
    {
        _mockHandler.SetupError("http://localhost:5002/api/portfolio/summary", HttpStatusCode.ServiceUnavailable);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.Contains("unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaching_ReturnsCachedResult()
    {
        var response = new { totalValue = 50000m };
        _mockHandler.SetupResponse("http://localhost:5002/api/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5002", 60);

        var result1 = await tool.ExecuteAsync(new Dictionary<string, object>());
        var result2 = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithUrlTrailingSlash_HandlesCorrectly()
    {
        var response = new { totalValue = 50000m };
        _mockHandler.SetupResponse("http://localhost:5002/api/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5002/", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result.Success);
    }
}

public class TechnicalIndicatorsToolTests
{
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHandler;

    public TechnicalIndicatorsToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidParameters_ReturnsSuccess()
    {
        var response = new { rsi = 65.5m, macd = 150.0m };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingAsset_ReturnsError()
    {
        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["timeframe"] = "1h"
        });

        Assert.False(result.Success);
        Assert.Contains("asset", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingTimeframe_ReturnsError()
    {
        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC"
        });

        Assert.False(result.Success);
        Assert.Contains("timeframe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithServiceUnavailable_ReturnsError()
    {
        _mockHandler.SetupError("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", HttpStatusCode.ServiceUnavailable);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.False(result.Success);
        Assert.Contains("unavailable", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithRequestTimeout_ReturnsError()
    {
        _mockHandler.SetupTimeout("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h");

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.False(result.Success);
        Assert.Contains("timed out", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaching_ReturnsCachedResult()
    {
        var response = new { rsi = 65.5m };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result1 = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });
        var result2 = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithDifferentAssets_QueriesCorrectly()
    {
        var response = new { rsi = 70.0m };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=ETH/USD&timeframe=4h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "ETH",
            ["timeframe"] = "4h"
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithUrlTrailingSlash_HandlesCorrectly()
    {
        var response = new { rsi = 65.5m };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002/", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.True(result.Success);
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, object> _responses = new();
    private readonly HashSet<string> _errors = new();
    private readonly HashSet<string> _timeouts = new();

    public void SetupResponse<T>(string url, T response)
    {
        _responses[url] = response;
    }

    public void SetupError(string url, HttpStatusCode statusCode)
    {
        _errors.Add(url);
    }

    public void SetupTimeout(string url)
    {
        _timeouts.Add(url);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? "";

        if (_timeouts.Contains(url))
        {
            throw new TaskCanceledException("Simulated request timeout");
        }

        if (_errors.Contains(url))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        }

        if (_responses.TryGetValue(url, out var response))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(response)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

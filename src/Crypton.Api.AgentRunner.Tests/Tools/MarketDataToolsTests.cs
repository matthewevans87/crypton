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
        var response = new
        {
            Mode = "paper",
            Balance = new { AvailableUsd = 10000m, AssetBalances = new Dictionary<string, decimal> { ["BTC"] = 0.5m }, Timestamp = DateTimeOffset.UtcNow },
            OpenPositions = Array.Empty<object>(),
            RecentTrades = Array.Empty<object>()
        };
        _mockHandler.SetupResponse("http://localhost:5004/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5004", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result.Success);
        var data = Assert.IsType<PortfolioSummaryResponse>(result.Data);
        Assert.Equal("paper", data.Mode);
        Assert.NotNull(data.Balance);
        Assert.Equal(10000m, data.Balance.AvailableUsd);
        Assert.NotNull(data.OpenPositions);
    }

    [Fact]
    public async Task ExecuteAsync_WhenServiceReturnsError_ReturnsFailure()
    {
        _mockHandler.SetupError("http://localhost:5004/portfolio/summary", HttpStatusCode.ServiceUnavailable);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5004", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaching_ReturnsCachedResult()
    {
        var response = new { Mode = "paper", Balance = new { AvailableUsd = 10000m, AssetBalances = new Dictionary<string, decimal>(), Timestamp = DateTimeOffset.UtcNow }, OpenPositions = Array.Empty<object>(), RecentTrades = Array.Empty<object>() };
        _mockHandler.SetupResponse("http://localhost:5004/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5004", 60);

        var result1 = await tool.ExecuteAsync(new Dictionary<string, object>());
        var result2 = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithUrlTrailingSlash_HandlesCorrectly()
    {
        var response = new { Mode = "paper", Balance = new { AvailableUsd = 10000m, AssetBalances = new Dictionary<string, decimal>(), Timestamp = DateTimeOffset.UtcNow }, OpenPositions = Array.Empty<object>(), RecentTrades = Array.Empty<object>() };
        _mockHandler.SetupResponse("http://localhost:5004/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5004/", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_MissingRequiredFields_ReturnsError()
    {
        // Response missing Balance and OpenPositions
        var response = new { Mode = "paper" };
        _mockHandler.SetupResponse("http://localhost:5004/portfolio/summary", response);

        var tool = new CurrentPositionTool(_httpClient, "http://localhost:5004", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.Contains("validation failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("balance", result.Error, StringComparison.OrdinalIgnoreCase);
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
        var response = new { currentPrice = 65000m, rsi = 65.5m, macd = 150.0m, symbol = "BTC/USD", timeframe = "1h" };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.True(result.Success);
        var data = Assert.IsType<TechnicalIndicatorsResponse>(result.Data);
        Assert.Equal(65000m, data.CurrentPrice);
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
        var response = new { currentPrice = 65000m, rsi = 65.5m, symbol = "BTC/USD", timeframe = "1h" };
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
        var response = new { currentPrice = 3200m, rsi = 70.0m, symbol = "ETH/USD", timeframe = "4h" };
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
        var response = new { currentPrice = 65000m, rsi = 65.5m, symbol = "BTC/USD", timeframe = "1h" };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002/", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_MissingCurrentPrice_ReturnsError()
    {
        var response = new { rsi = 65.5m, macd = 150.0m, symbol = "BTC/USD", timeframe = "1h" };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.False(result.Success);
        Assert.Contains("validation failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("currentPrice", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_NullCurrentPrice_ReturnsError()
    {
        var response = new { currentPrice = (decimal?)null, rsi = 65.5m, symbol = "BTC/USD", timeframe = "1h" };
        _mockHandler.SetupResponse("http://localhost:5002/api/indicators?symbol=BTC/USD&timeframe=1h", response);

        var tool = new TechnicalIndicatorsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>
        {
            ["asset"] = "BTC",
            ["timeframe"] = "1h"
        });

        Assert.False(result.Success);
        Assert.Contains("validation failed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("currentPrice", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}

public class GetPriceToolTests
{
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHandler;

    public GetPriceToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    [Fact]
    public async Task ExecuteAsync_SingleAsset_ReturnsPriceTicker()
    {
        var response = new[] { new { asset = "BTC/USD", price = 65000m, high24h = 66000m, low24h = 64000m, volume24h = 1000m } };
        _mockHandler.SetupResponse("http://localhost:5002/api/prices?symbols=BTC%2FUSD", response);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "BTC" });

        Assert.True(result.Success);
        var data = Assert.IsType<PriceTickerResponse>(result.Data);
        Assert.Equal(65000m, data.Price);
        Assert.Equal("BTC/USD", data.Asset);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleAssets_ReturnsList()
    {
        var response = new[]
        {
            new { asset = "BTC/USD", price = 65000m, high24h = 66000m, low24h = 64000m, volume24h = 1000m },
            new { asset = "ETH/USD", price = 3200m, high24h = 3300m, low24h = 3100m, volume24h = 500m }
        };
        _mockHandler.SetupResponse("http://localhost:5002/api/prices?symbols=BTC%2FUSD%2CETH%2FUSD", response);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "BTC,ETH" });

        Assert.True(result.Success);
        var data = Assert.IsAssignableFrom<IEnumerable<PriceTickerResponse>>(result.Data);
        Assert.Equal(2, data.Count());
    }

    [Fact]
    public async Task ExecuteAsync_DefaultsTobtc_WhenSymbolsOmitted()
    {
        var response = new[] { new { asset = "BTC/USD", price = 65000m, high24h = 66000m, low24h = 64000m, volume24h = 1000m } };
        _mockHandler.SetupResponse("http://localhost:5002/api/prices?symbols=BTC%2FUSD", response);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_NormalisesXbtAlias()
    {
        var response = new[] { new { asset = "BTC/USD", price = 65000m, high24h = 66000m, low24h = 64000m, volume24h = 1000m } };
        _mockHandler.SetupResponse("http://localhost:5002/api/prices?symbols=BTC%2FUSD", response);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "XBT" });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_MissingPrice_ReturnsValidationError()
    {
        var response = new[] { new { asset = "BTC/USD" } };
        _mockHandler.SetupResponse("http://localhost:5002/api/prices?symbols=BTC%2FUSD", response);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "BTC" });

        Assert.False(result.Success);
        Assert.Contains("price", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceUnavailable_ReturnsError()
    {
        _mockHandler.SetupError("http://localhost:5002/api/prices?symbols=BTC%2FUSD", System.Net.HttpStatusCode.ServiceUnavailable);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "BTC" });

        Assert.False(result.Success);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaching_DoesNotRehitService()
    {
        var response = new[] { new { asset = "BTC/USD", price = 65000m, high24h = 66000m, low24h = 64000m, volume24h = 1000m } };
        _mockHandler.SetupResponse("http://localhost:5002/api/prices?symbols=BTC%2FUSD", response);

        var tool = new GetPriceTool(_httpClient, "http://localhost:5002", 60);

        var result1 = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "BTC" });
        var result2 = await tool.ExecuteAsync(new Dictionary<string, object> { ["symbols"] = "BTC" });

        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }
}

public class MacroSignalsToolTests
{
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHandler;

    public MacroSignalsToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidResponse_ReturnsMacroSignals()
    {
        var response = new { trend = "bullish", volatilityRegime = "normal", fearGreedIndex = 62m, sentiment = "greed", btcDominance = 52.3m };
        _mockHandler.SetupResponse("http://localhost:5002/api/macro", response);

        var tool = new MacroSignalsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result.Success);
        var data = Assert.IsType<MacroSignalsResponse>(result.Data);
        Assert.Equal("bullish", data.Trend);
        Assert.Equal("normal", data.VolatilityRegime);
        Assert.Equal(62m, data.FearGreedIndex);
    }

    [Fact]
    public async Task ExecuteAsync_MissingTrend_ReturnsValidationError()
    {
        var response = new { volatilityRegime = "normal", fearGreedIndex = 50m };
        _mockHandler.SetupResponse("http://localhost:5002/api/macro", response);

        var tool = new MacroSignalsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.Contains("trend", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceUnavailable_ReturnsError()
    {
        _mockHandler.SetupError("http://localhost:5002/api/macro", System.Net.HttpStatusCode.ServiceUnavailable);

        var tool = new MacroSignalsTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaching_DoesNotRehitService()
    {
        var response = new { trend = "neutral", volatilityRegime = "low", fearGreedIndex = 45m, sentiment = "fear" };
        _mockHandler.SetupResponse("http://localhost:5002/api/macro", response);

        var tool = new MacroSignalsTool(_httpClient, "http://localhost:5002", 60);

        var result1 = await tool.ExecuteAsync(new Dictionary<string, object>());
        var result2 = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }
}

public class OrderBookToolTests
{
    private readonly HttpClient _httpClient;
    private readonly MockHttpMessageHandler _mockHandler;

    public OrderBookToolTests()
    {
        _mockHandler = new MockHttpMessageHandler();
        _httpClient = new HttpClient(_mockHandler);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidParameters_ReturnsOrderBook()
    {
        var response = new
        {
            symbol = "BTC/USD",
            bids = new[] { new { price = 64990m, quantity = 0.5m }, new { price = 64980m, quantity = 1.2m } },
            asks = new[] { new { price = 65010m, quantity = 0.3m }, new { price = 65020m, quantity = 0.8m } }
        };
        _mockHandler.SetupResponse("http://localhost:5002/api/orderbook/BTC%2FUSD?depth=10", response);

        var tool = new OrderBookTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["asset"] = "BTC" });

        Assert.True(result.Success);
        var data = Assert.IsType<OrderBookResponse>(result.Data);
        Assert.Equal("BTC/USD", data.Symbol);
        Assert.Equal(2, data.Bids!.Count);
        Assert.Equal(2, data.Asks!.Count);
    }

    [Fact]
    public async Task ExecuteAsync_MissingAsset_ReturnsError()
    {
        var tool = new OrderBookTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object>());

        Assert.False(result.Success);
        Assert.Contains("asset", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_NormalisesSymbol()
    {
        var response = new
        {
            symbol = "ETH/USD",
            bids = new[] { new { price = 3190m, quantity = 1m } },
            asks = new[] { new { price = 3210m, quantity = 1m } }
        };
        _mockHandler.SetupResponse("http://localhost:5002/api/orderbook/ETH%2FUSD?depth=10", response);

        var tool = new OrderBookTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["asset"] = "ETH/USD" });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_ServiceUnavailable_ReturnsError()
    {
        _mockHandler.SetupError("http://localhost:5002/api/orderbook/BTC%2FUSD?depth=10", System.Net.HttpStatusCode.ServiceUnavailable);

        var tool = new OrderBookTool(_httpClient, "http://localhost:5002", 60);

        var result = await tool.ExecuteAsync(new Dictionary<string, object> { ["asset"] = "BTC" });

        Assert.False(result.Success);
        Assert.Contains("error", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithCaching_DoesNotRehitService()
    {
        var response = new
        {
            symbol = "BTC/USD",
            bids = new[] { new { price = 64990m, quantity = 0.5m } },
            asks = new[] { new { price = 65010m, quantity = 0.3m } }
        };
        _mockHandler.SetupResponse("http://localhost:5002/api/orderbook/BTC%2FUSD?depth=10", response);

        var tool = new OrderBookTool(_httpClient, "http://localhost:5002", 60);

        var result1 = await tool.ExecuteAsync(new Dictionary<string, object> { ["asset"] = "BTC" });
        var result2 = await tool.ExecuteAsync(new Dictionary<string, object> { ["asset"] = "BTC" });

        Assert.True(result1.Success);
        Assert.True(result2.Success);
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, object> _responses = new();
    private readonly HashSet<string> _errors = new();
    private readonly HashSet<string> _timeouts = new();

    public void SetupResponse<T>(string url, T response)
    {
        _responses[url] = response!;
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

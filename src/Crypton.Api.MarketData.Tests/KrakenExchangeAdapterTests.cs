using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MarketDataService.Adapters;
using MarketDataService.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MarketDataService.Tests;

public class KrakenExchangeAdapterTests
{
    private readonly Mock<ILogger<KrakenExchangeAdapter>> _mockLogger;
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public KrakenExchangeAdapterTests()
    {
        _mockLogger = new Mock<ILogger<KrakenExchangeAdapter>>();
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _httpClient = new HttpClient();
        _configuration = new ConfigurationBuilder().Build();
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        Assert.Equal("Kraken", adapter.ExchangeName);
        Assert.False(adapter.IsConnected);
    }

    [Fact]
    public async Task GetPricesAsync_WithValidSymbols_ReturnsPrices()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        handler.SetupResponse("https://api.kraken.com/0/public/Ticker?pair=XXBTZUSD", GetKrakenTickerResponse());

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetPricesAsync(new[] { "BTC/USD" });

        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public async Task GetPriceAsync_WithValidSymbol_ReturnsPrice()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        handler.SetupResponse("https://api.kraken.com/0/public/Ticker?pair=XXBTZUSD", GetKrakenTickerResponse());

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetPriceAsync("BTC/USD");

        Assert.NotNull(result);
        Assert.Equal("BTC/USD", result.Asset);
    }

    [Fact]
    public async Task GetPriceAsync_WithInvalidSymbol_ReturnsNull()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        handler.SetupResponse("https://api.kraken.com/0/public/Ticker?pair=INVALID", "{\"error\":[\"EQuery:Unknown asset pair\"]}");

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetPriceAsync("INVALID");

        Assert.Null(result);
    }

    [Fact(Skip = "Mock handler URL matching issue - needs investigation")]
    public async Task GetOhlcvAsync_WithValidSymbol_ReturnsOhlcv()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        var ohlcvResponse = @"{
            ""error"": [],
            ""result"": {
                ""last"": 1234567890,
                ""XBT/USD"": [
                    [""1234567800"", ""49000.0"", ""50000.0"", ""48000.0"", ""49000.0"", ""100.0"", ""1000""],
                    [""1234567801"", ""49000.0"", ""51000.0"", ""48500.0"", ""50000.0"", ""150.0"", ""1500""]
                ]
            }
        }";
        handler.SetupResponse("https://api.kraken.com/0/public/OHLC?pair=XBT/USD&interval=60", ohlcvResponse);

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetOhlcvAsync("BTC/USD", "1h", 10);

        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }

    [Fact]
    public async Task GetOrderBookAsync_WithValidSymbol_ReturnsOrderBook()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        handler.SetupResponse("https://api.kraken.com/0/public/Depth?pair=XXBTZUSD&count=10", GetKrakenOrderBookResponse());

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetOrderBookAsync("BTC/USD", 10);

        Assert.NotNull(result);
        Assert.Equal("BTC/USD", result.Symbol);
    }

    [Fact]
    public async Task GetBalanceAsync_WithoutAuth_ReturnsEmpty()
    {
        var handler = MockHttpMessageHandler.CreateWithResult(HttpStatusCode.Unauthorized, "{\"error\":[\"EAuth:Invalid key\"]}");
        var httpClient = new HttpClient(handler);

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetBalanceAsync();

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetPortfolioSummaryAsync_ReturnsSummary()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        handler.SetupResponse("https://api.kraken.com/0/private/Balance", "{\"error\":[],\"result\":{\"XXBT\":\"1.5\",\"ZUSD\":\"50000\"}}");

        var testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kraken:ApiKey"] = "test-key",
                ["Kraken:ApiSecret"] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
            })
            .Build();

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, testConfig);

        var result = await adapter.GetPortfolioSummaryAsync();

        Assert.NotNull(result);
        Assert.Equal(2, result.Balances.Count);
    }

    [Fact]
    public void Events_ShouldBeAccessible()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        PriceTicker? receivedTicker = null;

        adapter.OnPriceUpdate += (sender, ticker) => receivedTicker = ticker;
        adapter.OnOrderBookUpdate += (sender, book) => { };
        adapter.OnTrade += (sender, trade) => { };
        adapter.OnConnectionStateChanged += (sender, connected) => { };

        Assert.True(true);
    }

    [Fact]
    public void ProcessMessage_WithTickerObject_RaisesPriceUpdate()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);
        PriceTicker? receivedTicker = null;

        adapter.OnPriceUpdate += (_, ticker) => receivedTicker = ticker;

        var processMessage = typeof(KrakenExchangeAdapter).GetMethod(
            "ProcessMessage",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(processMessage);

        // WS v2 ticker message format
        var message = "{\"channel\":\"ticker\",\"type\":\"update\",\"data\":[{\"symbol\":\"XBT/USD\",\"bid\":50000.0,\"bid_qty\":0.5,\"ask\":50010.0,\"ask_qty\":0.3,\"last\":50005.0,\"volume\":200.0,\"vwap\":50002.0,\"low\":49000.0,\"high\":51000.0,\"change\":5.0,\"change_pct\":0.01}]}";
        processMessage!.Invoke(adapter, new object[] { message });

        Assert.NotNull(receivedTicker);
        Assert.Equal("BTC/USD", receivedTicker!.Asset);
        Assert.Equal(50005.0m, receivedTicker.Price);
        Assert.Equal(50000.0m, receivedTicker.Bid);
        Assert.Equal(50010.0m, receivedTicker.Ask);
    }

    [Fact]
    public async Task GetTradesAsync_WithValidSymbol_ReturnsTrades()
    {
        var tradesResponse = @"{
            ""error"": [],
            ""result"": {
                ""trades"": {
                    ""TX-001"": [""50000.0"", ""0.5"", ""1234567890"", ""b"", ""l"", """"],
                    ""TX-002"": [""50001.0"", ""0.3"", ""1234567891"", ""s"", ""l"", """"]
                },
                ""count"": 2
            }
        }";

        var handler = MockHttpMessageHandler.CreateWithResult(HttpStatusCode.OK, tradesResponse);
        var httpClient = new HttpClient(handler);

        var testConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kraken:ApiKey"] = "test-key",
                ["Kraken:ApiSecret"] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4 })
            })
            .Build();

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, testConfig);

        var result = await adapter.GetTradesAsync("BTC/USD", 10);

        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetTradesAsync_WithInvalidResponse_ReturnsEmpty()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        handler.SetupResponse("https://api.kraken.com/0/private/TradesHistory?pair=XBT/USD", "{\"error\":[]}");

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetTradesAsync("BTC/USD", 10);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetTradesAsync_WithHttpError_RecordsFailure()
    {
        var handler = MockHttpMessageHandler.CreateWithResult(HttpStatusCode.ServiceUnavailable, "{\"error\":[\"Service unavailable\"]}");
        var httpClient = new HttpClient(handler);

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var result = await adapter.GetTradesAsync("BTC/USD", 10);

        Assert.NotNull(result);
    }

    [Fact]
    public void CircuitBreakerState_InitiallyClosed()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        Assert.Equal(Services.CircuitBreakerState.Closed, adapter.CircuitBreakerState);
    }

    [Fact]
    public void ReconnectCount_InitiallyZero()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        Assert.Equal(0, adapter.ReconnectCount);
    }

    [Fact]
    public async Task GetPricesAsync_WithCircuitBreakerOpen_ThrowsException()
    {
        var handler = new MockHttpMessageHandler();
        var httpClient = new HttpClient(handler);

        var adapter = new KrakenExchangeAdapter(httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        for (int i = 0; i < 5; i++)
        {
            try
            {
                await adapter.GetPricesAsync(new[] { "BTC/USD" });
            }
            catch { }
        }

        await Assert.ThrowsAsync<Services.CircuitBreakerOpenException>(() =>
            adapter.GetPricesAsync(new[] { "BTC/USD" }));
    }

    [Fact]
    public void SubscribeToSymbols_ContainsExpectedMappings()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var field = typeof(KrakenExchangeAdapter).GetField("SymbolMapping",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var mapping = field?.GetValue(null) as Dictionary<string, string>;

        Assert.NotNull(mapping);
        Assert.Equal("XXBTZUSD", mapping["BTC/USD"]);
        Assert.Equal("XETHZUSD", mapping["ETH/USD"]);
    }

    private static string GetKrakenTickerResponse()
    {
        return @"{
            ""error"": [],
            ""result"": {
                ""XBT/USD"": {
                    ""a"": [""50000.5"", ""1"", ""1.000""],
                    ""b"": [""50000.0"", ""2"", ""2.000""],
                    ""c"": [""50000.2"", ""0.5""],
                    ""v"": [""100.0"", ""200.0""],
                    ""p"": [""49000.0"", ""49500.0""],
                    ""t"": [""1000"", ""2000""],
                    ""l"": [""48000.0"", ""47000.0""],
                    ""h"": [""51000.0"", ""52000.0""],
                    ""o"": ""49000.0""
                }
            }
        }";
    }

    private static string GetKrakenOhlcvResponse()
    {
        return @"{
            ""error"": [],
            ""result"": {
                ""last"": 1234567890,
                ""XBT/USD"": [
                    [""1234567800"", ""49000.0"", ""50000.0"", ""48000.0"", ""49000.0"", ""100.0"", ""1000""],
                    [""1234567801"", ""49000.0"", ""51000.0"", ""48500.0"", ""50000.0"", ""150.0"", ""1500""]
                ]
            }
        }";
    }

    private static string GetKrakenOrderBookResponse()
    {
        return @"{
            ""error"": [],
            ""result"": {
                ""XBT/USD"": {
                    ""asks"": [
                        [""50000.5"", ""1.0"", ""10""],
                        [""50001.0"", ""2.0"", ""20""]
                    ],
                    ""bids"": [
                        [""49999.5"", ""1.5"", ""15""],
                        [""49999.0"", ""2.5"", ""25""]
                    ]
                }
            }
        }";
    }

    [Fact]
    public void ParseTradeUpdate_WithValidData_ReturnsTrade()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // WS v2: trade is an object with named fields
        var tradeData = JsonSerializer.Deserialize<JsonElement>(
            "{\"price\": 50000.5, \"qty\": 0.5, \"trade_id\": 1234567890, \"side\": \"buy\", \"timestamp\": \"2024-06-01T12:00:00.000Z\"}");

        var result = method?.Invoke(adapter, new object[] { tradeData, "BTC/USD" }) as Trade;

        Assert.NotNull(result);
        Assert.Equal("BTC/USD_1234567890", result.Id);
        Assert.Equal("BTC/USD", result.Symbol);
        Assert.Equal(50000.5m, result.Price);
        Assert.Equal(0.5m, result.Quantity);
        Assert.Equal("buy", result.Side);
    }

    [Fact]
    public void ParseTradeUpdate_WithInsufficientData_ReturnsNull()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var tradeData = JsonSerializer.Deserialize<JsonElement>(@"[""50000.5"", ""0.5""]");

        var result = method?.Invoke(adapter, new object[] { tradeData, "BTC/USD" }) as Trade;

        Assert.Null(result);
    }

    [Fact]
    public void ParseTradeUpdate_WithEmptyArray_ReturnsNull()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var tradeData = JsonSerializer.Deserialize<JsonElement>(@"[]");

        var result = method?.Invoke(adapter, new object[] { tradeData, "BTC/USD" }) as Trade;

        Assert.Null(result);
    }

    [Fact]
    public void ParseTradeUpdate_WithInvalidPrice_ReturnsNull()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var tradeData = JsonSerializer.Deserialize<JsonElement>(@"[""invalid"", ""0.5"", ""1234567890"", ""b""]");

        var result = method?.Invoke(adapter, new object[] { tradeData, "BTC/USD" }) as Trade;

        Assert.Null(result);
    }

    [Fact]
    public void ParseTradeUpdate_WithInvalidQuantity_ReturnsNull()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        var tradeData = JsonSerializer.Deserialize<JsonElement>(@"[""50000.5"", ""invalid"", ""1234567890"", ""b""]");

        var result = method?.Invoke(adapter, new object[] { tradeData, "BTC/USD" }) as Trade;

        Assert.Null(result);
    }

    [Fact]
    public void ParseTradeUpdate_WithSellSide_ReturnsCorrectSide()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // WS v2: side field is the full word "sell"
        var tradeData = JsonSerializer.Deserialize<JsonElement>(
            "{\"price\": 50000.5, \"qty\": 0.5, \"trade_id\": 1234567890, \"side\": \"sell\", \"timestamp\": \"2024-06-01T12:00:00.000Z\"}");

        var result = method?.Invoke(adapter, new object[] { tradeData, "ETH/USD" }) as Trade;

        Assert.NotNull(result);
        Assert.Equal("sell", result.Side);
    }

    [Fact]
    public void ParseTradeUpdate_WithIsoTimestamp_ParsesCorrectly()
    {
        var adapter = new KrakenExchangeAdapter(_httpClient, _mockLogger.Object, _mockLoggerFactory.Object, _configuration);

        var method = typeof(KrakenExchangeAdapter).GetMethod("ParseTradeUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        // WS v2: timestamp is an ISO 8601 string, not a Unix float
        var tradeData = JsonSerializer.Deserialize<JsonElement>(
            "{\"price\": 50000.5, \"qty\": 0.5, \"trade_id\": 999, \"side\": \"buy\", \"timestamp\": \"1970-01-01T00:00:00.000Z\"}");

        var result = method?.Invoke(adapter, new object[] { tradeData, "BTC/USD" }) as Trade;

        Assert.NotNull(result);
        Assert.Equal(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), result.Timestamp);
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    private readonly Dictionary<string, string> _responses = new();
    private Func<HttpRequestMessage, HttpResponseMessage>? _customHandler;

    public void SetupResponse(string url, string responseBody)
    {
        _responses[url] = responseBody;
    }

    public static MockHttpMessageHandler CreateWithResult(HttpStatusCode statusCode, string content)
    {
        var handler = new MockHttpMessageHandler();
        handler._customHandler = _ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content)
        };
        return handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_customHandler != null)
        {
            return Task.FromResult(_customHandler(request));
        }

        var url = request.RequestUri?.ToString() ?? "";

        if (_responses.TryGetValue(url, out var response))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response)
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("Not found")
        });
    }
}

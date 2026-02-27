using MarketDataService.Adapters;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MarketDataService.Tests;

public class TechnicalIndicatorServiceTests
{
    private readonly Mock<IExchangeAdapter> _mockAdapter;
    private readonly Mock<ILogger<TechnicalIndicatorService>> _mockLogger;
    private readonly InMemoryMarketDataCache _cache;
    private readonly TechnicalIndicatorService _service;

    public TechnicalIndicatorServiceTests()
    {
        _mockAdapter = new Mock<IExchangeAdapter>();
        _mockLogger = new Mock<ILogger<TechnicalIndicatorService>>();
        _cache = new InMemoryMarketDataCache();
        _service = new TechnicalIndicatorService(_mockAdapter.Object, _cache);
    }

    [Fact]
    public async Task CalculateAsync_WithValidData_ReturnsRsi()
    {
        var ohlcv = GenerateOhlcvData(30, 50000m);
        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ohlcv);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.NotNull(result);
        Assert.NotNull(result.Rsi);
    }

    [Fact]
    public async Task CalculateAsync_WithCachedData_ReturnsCached()
    {
        var cachedIndicator = new TechnicalIndicator
        {
            Symbol = "BTC/USD",
            Timeframe = "1h",
            Rsi = 50m,
            LastUpdated = DateTime.UtcNow
        };
        _cache.SetTechnicalIndicator(cachedIndicator);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.NotNull(result);
        Assert.Equal(50m, result.Rsi);
        _mockAdapter.Verify(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CalculateAsync_InsufficientData_ReturnsNull()
    {
        var ohlcv = GenerateOhlcvData(10, 50000m);
        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ohlcv);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.Null(result);
    }

    [Fact]
    public async Task CalculateAsync_CalculatesMacd()
    {
        var ohlcv = GenerateOhlcvData(50, 50000m);
        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ohlcv);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.NotNull(result);
        Assert.NotNull(result.Macd);
        Assert.NotNull(result.MacdSignal);
    }

    [Fact]
    public async Task CalculateAsync_CalculatesBollingerBands()
    {
        var ohlcv = GenerateOhlcvData(30, 50000m);
        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ohlcv);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.NotNull(result);
        Assert.NotNull(result.BollingerUpper);
        Assert.NotNull(result.BollingerMiddle);
        Assert.NotNull(result.BollingerLower);
    }

    [Fact]
    public async Task CalculateAsync_OverboughtSignal_ReturnsOverbought()
    {
        var prices = new List<decimal>();
        var basePrice = 50000m;
        for (int i = 0; i < 30; i++)
        {
            basePrice += 200m;
            prices.Add(basePrice);
        }
        var ohlcv = prices.Select((p, i) => new Ohlcv
        {
            Timestamp = DateTime.UtcNow.AddHours(-i),
            Open = p - 50m,
            High = p + 100m,
            Low = p - 150m,
            Close = p,
            Volume = 1000m
        }).ToList();

        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ohlcv);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.NotNull(result);
        Assert.Equal("overbought", result.Signal);
    }

    [Fact]
    public async Task CalculateAsync_OversoldSignal_ReturnsOversold()
    {
        var prices = new List<decimal>();
        var basePrice = 50000m;
        for (int i = 0; i < 30; i++)
        {
            basePrice -= 200m;
            prices.Add(basePrice);
        }
        var ohlcv = prices.Select((p, i) => new Ohlcv
        {
            Timestamp = DateTime.UtcNow.AddHours(-i),
            Open = p - 50m,
            High = p + 100m,
            Low = p - 150m,
            Close = p,
            Volume = 1000m
        }).ToList();

        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ohlcv);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.NotNull(result);
        Assert.Equal("oversold", result.Signal);
    }

    [Fact]
    public async Task CalculateAsync_InvalidAdapter_ReturnsNull()
    {
        _mockAdapter.Setup(a => a.GetOhlcvAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<Ohlcv>)null!);

        var result = await _service.CalculateAsync("BTC/USD", "1h");

        Assert.Null(result);
    }

    private static List<Ohlcv> GenerateOhlcvData(int count, decimal basePrice)
    {
        var result = new List<Ohlcv>();
        var random = new Random(42);
        
        for (int i = count - 1; i >= 0; i--)
        {
            var volatility = basePrice * 0.02m;
            var change = (decimal)(random.NextDouble() * (double)volatility - (double)volatility / 2);
            var close = basePrice + change;
            
            result.Add(new Ohlcv
            {
                Timestamp = DateTime.UtcNow.AddHours(-i),
                Open = close - (decimal)(random.NextDouble() * 100),
                High = close + (decimal)(random.NextDouble() * 100),
                Low = close - (decimal)(random.NextDouble() * 100),
                Close = close,
                Volume = (decimal)(random.NextDouble() * 10000)
            });
            
            basePrice = close;
        }
        
        return result;
    }
}

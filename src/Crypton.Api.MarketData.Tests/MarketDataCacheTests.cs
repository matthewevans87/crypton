using MarketDataService.Models;
using MarketDataService.Services;
using Xunit;

namespace MarketDataService.Tests;

public class MarketDataCacheTests
{
    private readonly InMemoryMarketDataCache _cache;

    public MarketDataCacheTests()
    {
        _cache = new InMemoryMarketDataCache();
    }

    [Fact]
    public void SetPrice_ShouldStorePrice()
    {
        var ticker = new PriceTicker
        {
            Asset = "BTC/USD",
            Price = 50000m,
            Bid = 49999m,
            Ask = 50001m,
            LastUpdated = DateTime.UtcNow
        };

        _cache.SetPrice(ticker);

        var result = _cache.GetPrice("BTC/USD");
        Assert.NotNull(result);
        Assert.Equal("BTC/USD", result.Asset);
        Assert.Equal(50000m, result.Price);
    }

    [Fact]
    public async Task GetPrice_Expired_ShouldReturnNull()
    {
        var ticker = new PriceTicker
        {
            Asset = "BTC/USD",
            Price = 50000m,
            LastUpdated = DateTime.UtcNow
        };

        _cache.SetPrice(ticker);

        await Task.Delay(6000);

        var result = _cache.GetPrice("BTC/USD");
        Assert.Null(result);
    }

    [Fact]
    public void GetAllPrices_ShouldReturnAllNonExpiredPrices()
    {
        _cache.SetPrice(new PriceTicker { Asset = "BTC/USD", Price = 50000m, LastUpdated = DateTime.UtcNow });
        _cache.SetPrice(new PriceTicker { Asset = "ETH/USD", Price = 3000m, LastUpdated = DateTime.UtcNow });

        var result = _cache.GetAllPrices();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SetOrderBook_ShouldStoreOrderBook()
    {
        var orderBook = new OrderBook
        {
            Symbol = "BTC/USD",
            Bids = new List<OrderBookEntry>
            {
                new() { Price = 49999m, Quantity = 1.5m }
            },
            Asks = new List<OrderBookEntry>
            {
                new() { Price = 50001m, Quantity = 1.2m }
            },
            LastUpdated = DateTime.UtcNow
        };

        _cache.SetOrderBook(orderBook);

        var result = _cache.GetOrderBook("BTC/USD");
        Assert.NotNull(result);
        Assert.Equal("BTC/USD", result.Symbol);
        Assert.Single(result.Bids);
        Assert.Single(result.Asks);
    }

    [Fact]
    public void SetOhlcv_ShouldStoreOhlcvData()
    {
        var ohlcv = new List<Ohlcv>
        {
            new() { Timestamp = DateTime.UtcNow, Open = 50000m, High = 51000m, Low = 49000m, Close = 50500m, Volume = 1000m }
        };

        _cache.SetOhlcv("BTC/USD", "1h", ohlcv);

        var result = _cache.GetOhlcv("BTC/USD", "1h");
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(50000m, result[0].Open);
    }

    [Fact]
    public void SetBalance_ShouldStoreBalances()
    {
        var balances = new List<Balance>
        {
            new() { Asset = "BTC", Available = 1.5m },
            new() { Asset = "USD", Available = 50000m }
        };

        _cache.SetBalance(balances);

        var result = _cache.GetBalance();
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SetPortfolioSummary_ShouldStoreSummary()
    {
        var summary = new PortfolioSummary
        {
            TotalValue = 100000m,
            UnrealizedPnL = 5000m,
            AvailableCapital = 25000m,
            LastUpdated = DateTime.UtcNow
        };

        _cache.SetPortfolioSummary(summary);

        var result = _cache.GetPortfolioSummary();
        Assert.NotNull(result);
        Assert.Equal(100000m, result.TotalValue);
    }

    [Fact]
    public void SetTechnicalIndicator_ShouldStoreIndicator()
    {
        var indicator = new TechnicalIndicator
        {
            Symbol = "BTC/USD",
            Timeframe = "1h",
            Rsi = 65m,
            Macd = 150m,
            LastUpdated = DateTime.UtcNow
        };

        _cache.SetTechnicalIndicator(indicator);

        var result = _cache.GetTechnicalIndicator("BTC/USD", "1h");
        Assert.NotNull(result);
        Assert.Equal(65m, result.Rsi);
    }

    [Fact]
    public void Clear_ShouldRemoveAllData()
    {
        _cache.SetPrice(new PriceTicker { Asset = "BTC/USD", Price = 50000m, LastUpdated = DateTime.UtcNow });
        _cache.SetBalance(new List<Balance> { new() { Asset = "BTC", Available = 1m } });

        _cache.Clear();

        Assert.Null(_cache.GetPrice("BTC/USD"));
        Assert.Null(_cache.GetBalance());
    }

    [Fact]
    public void GetPrice_NonExistent_ShouldReturnNull()
    {
        var result = _cache.GetPrice("NONEXISTENT");
        Assert.Null(result);
    }

    [Fact]
    public void MultipleSymbols_ShouldTrackIndependently()
    {
        _cache.SetPrice(new PriceTicker { Asset = "BTC/USD", Price = 50000m, LastUpdated = DateTime.UtcNow });
        _cache.SetPrice(new PriceTicker { Asset = "ETH/USD", Price = 3000m, LastUpdated = DateTime.UtcNow });

        var btc = _cache.GetPrice("BTC/USD");
        var eth = _cache.GetPrice("ETH/USD");

        Assert.NotNull(btc);
        Assert.NotNull(eth);
        Assert.Equal(50000m, btc.Price);
        Assert.Equal(3000m, eth.Price);
    }
}

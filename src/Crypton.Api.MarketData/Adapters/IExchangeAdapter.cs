using MarketDataService.Models;
using MarketDataService.Services;

namespace MarketDataService.Adapters;

public interface IExchangeAdapter
{
    string ExchangeName { get; }
    Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    Task<PriceTicker?> GetPriceAsync(string symbol, CancellationToken cancellationToken = default);
    Task<List<PriceTicker>> GetPricesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default);
    Task<OrderBook?> GetOrderBookAsync(string symbol, int depth = 10, CancellationToken cancellationToken = default);
    Task<List<Ohlcv>> GetOhlcvAsync(string symbol, string timeframe, int limit = 100, CancellationToken cancellationToken = default);
    Task<List<Balance>> GetBalanceAsync(CancellationToken cancellationToken = default);
    Task<List<Trade>> GetTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default);
    Task<PortfolioSummary> GetPortfolioSummaryAsync(CancellationToken cancellationToken = default);
    event EventHandler<PriceTicker>? OnPriceUpdate;
    event EventHandler<OrderBook>? OnOrderBookUpdate;
    event EventHandler<Trade>? OnTrade;
    event EventHandler<bool>? OnConnectionStateChanged;
    bool IsConnected { get; }
    int ReconnectCount { get; }
    TimeSpan CurrentReconnectDelay { get; }
    CircuitBreakerState CircuitBreakerState { get; }
}

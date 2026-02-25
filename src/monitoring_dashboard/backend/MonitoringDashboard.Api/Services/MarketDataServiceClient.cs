using Microsoft.AspNetCore.SignalR.Client;

namespace MonitoringDashboard.Services;

public interface IMarketDataServiceClient
{
    event EventHandler<PriceTicker>? OnPriceUpdate;
    event EventHandler<OrderBook>? OnOrderBookUpdate;
    event EventHandler<bool>? OnConnectionStatus;
    
    Task ConnectAsync(CancellationToken cancellationToken = default);
    Task DisconnectAsync();
    bool IsConnected { get; }
    Task<List<PriceTicker>> GetPricesAsync(IEnumerable<string>? symbols = null);
    Task<PriceTicker?> GetPriceAsync(string symbol);
    Task<List<Balance>> GetBalanceAsync();
    Task<PortfolioSummary> GetPortfolioSummaryAsync();
    Task<List<Ohlcv>> GetOhlcvAsync(string symbol, string timeframe, int limit = 100);
    Task<TechnicalIndicator?> GetIndicatorsAsync(string symbol, string timeframe);
}

public class PriceTicker
{
    public string Asset { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public decimal Change24h { get; set; }
    public decimal ChangePercent24h { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal High24h { get; set; }
    public decimal Low24h { get; set; }
    public decimal Volume24h { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class OrderBook
{
    public string Symbol { get; set; } = string.Empty;
    public List<OrderBookEntry> Bids { get; set; } = new();
    public List<OrderBookEntry> Asks { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class OrderBookEntry
{
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public int Count { get; set; }
}

public class Ohlcv
{
    public DateTime Timestamp { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
}

public class Balance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Available { get; set; }
    public decimal Hold { get; set; }
    public decimal Total => Available + Hold;
}

public class PortfolioSummary
{
    public decimal TotalValue { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal AvailableCapital { get; set; }
    public List<Balance> Balances { get; set; } = new();
    public List<Position> Positions { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class Position
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnrealizedPnL { get; set; }
    public decimal UnrealizedPnLPercent { get; set; }
    public DateTime OpenedAt { get; set; }
}

public class TechnicalIndicator
{
    public string Asset { get; set; } = string.Empty;
    public string Timeframe { get; set; } = string.Empty;
    public decimal? Rsi { get; set; }
    public decimal? Macd { get; set; }
    public decimal? MacdSignal { get; set; }
    public decimal? MacdHistogram { get; set; }
    public decimal? BollingerUpper { get; set; }
    public decimal? BollingerMiddle { get; set; }
    public decimal? BollingerLower { get; set; }
    public string? Signal { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class MarketDataServiceClient : IMarketDataServiceClient, IAsyncDisposable
{
    private HubConnection? _connection;
    private readonly string _marketDataServiceUrl;
    private readonly ILogger<MarketDataServiceClient> _logger;
    private readonly HttpClient _httpClient;
    private bool _isConnected;

    public event EventHandler<PriceTicker>? OnPriceUpdate;
    public event EventHandler<OrderBook>? OnOrderBookUpdate;
    public event EventHandler<bool>? OnConnectionStatus;
    
    public bool IsConnected => _isConnected;

    public MarketDataServiceClient(string marketDataServiceUrl, HttpClient httpClient, ILogger<MarketDataServiceClient> logger)
    {
        _marketDataServiceUrl = marketDataServiceUrl.TrimEnd('/');
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _connection = new HubConnectionBuilder()
                .WithUrl($"{_marketDataServiceUrl}/hubs/marketdata")
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30) })
                .Build();

            _connection.On<PriceTicker>("OnPriceUpdate", ticker =>
            {
                _logger.LogDebug("Received price update for {Asset}", ticker.Asset);
                OnPriceUpdate?.Invoke(this, ticker);
            });

            _connection.On<OrderBook>("OnOrderBookUpdate", orderBook =>
            {
                _logger.LogDebug("Received order book update for {Symbol}", orderBook.Symbol);
                OnOrderBookUpdate?.Invoke(this, orderBook);
            });

            _connection.On<bool>("OnConnectionStatus", isConnected =>
            {
                _isConnected = isConnected;
                _logger.LogInformation("Market Data Service connection status: {IsConnected}", isConnected);
                OnConnectionStatus?.Invoke(this, isConnected);
            });

            _connection.Closed += async (error) =>
            {
                _isConnected = false;
                _logger.LogWarning(error, "Market Data Service connection closed");
                OnConnectionStatus?.Invoke(this, false);
                await Task.CompletedTask;
            };

            _connection.Reconnected += async (connectionId) =>
            {
                _isConnected = true;
                _logger.LogInformation("Reconnected to Market Data Service with connection ID: {ConnectionId}", connectionId);
                OnConnectionStatus?.Invoke(this, true);
                await Task.CompletedTask;
            };

            _connection.Reconnecting += async (error) =>
            {
                _logger.LogWarning(error, "Reconnecting to Market Data Service...");
                await Task.CompletedTask;
            };

            await _connection.StartAsync(cancellationToken);
            _isConnected = true;
            _logger.LogInformation("Connected to Market Data Service at {Url}", _marketDataServiceUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Market Data Service");
            _isConnected = false;
            throw;
        }
    }

    public async Task DisconnectAsync()
    {
        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
            _isConnected = false;
        }
    }

    public async Task<List<PriceTicker>> GetPricesAsync(IEnumerable<string>? symbols = null)
    {
        try
        {
            var url = $"{_marketDataServiceUrl}/api/prices";
            if (symbols != null)
            {
                var symbolList = string.Join(",", symbols);
                url += $"?symbols={symbolList}";
            }
            
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<PriceTicker>>() ?? new List<PriceTicker>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get prices from Market Data Service");
            return new List<PriceTicker>();
        }
    }

    public async Task<PriceTicker?> GetPriceAsync(string symbol)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_marketDataServiceUrl}/api/prices/{Uri.EscapeDataString(symbol)}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<PriceTicker>();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get price for {Symbol} from Market Data Service", symbol);
            return null;
        }
    }

    public async Task<List<Balance>> GetBalanceAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_marketDataServiceUrl}/api/balance");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<Balance>>() ?? new List<Balance>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get balance from Market Data Service");
            return new List<Balance>();
        }
    }

    public async Task<PortfolioSummary> GetPortfolioSummaryAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_marketDataServiceUrl}/api/portfolio/summary");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<PortfolioSummary>() ?? new PortfolioSummary();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get portfolio summary from Market Data Service");
            return new PortfolioSummary();
        }
    }

    public async Task<List<Ohlcv>> GetOhlcvAsync(string symbol, string timeframe, int limit = 100)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_marketDataServiceUrl}/api/ohlcv?symbol={Uri.EscapeDataString(symbol)}&timeframe={timeframe}&limit={limit}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<List<Ohlcv>>() ?? new List<Ohlcv>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get OHLCV from Market Data Service");
            return new List<Ohlcv>();
        }
    }

    public async Task<TechnicalIndicator?> GetIndicatorsAsync(string symbol, string timeframe)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_marketDataServiceUrl}/api/indicators?symbol={Uri.EscapeDataString(symbol)}&timeframe={timeframe}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TechnicalIndicator>();
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get indicators from Market Data Service");
            return null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}

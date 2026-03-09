using MarketDataService.Models;
using MarketDataService.Services;

namespace MarketDataService.Adapters;

/// <summary>
/// Deterministic, no-network exchange adapter used for E2E testing.
/// Activated when MARKETDATA__EXCHANGE__USEMOCK=true is set.
/// </summary>
public class MockExchangeAdapter : IExchangeAdapter
{
    private readonly ILogger<MockExchangeAdapter> _logger;
    private bool _isConnected;
    private Timer? _priceTimer;

    // Deterministic test prices — tests can assert against these exact values.
    public static readonly Dictionary<string, decimal> BaseP = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BTC/USD"] = 50_000m,
        ["ETH/USD"] = 3_000m,
        ["SOL/USD"] = 120m,
        ["XRP/USD"] = 0.60m,
    };

    public MockExchangeAdapter(ILogger<MockExchangeAdapter> logger)
    {
        _logger = logger;
    }

    public string ExchangeName => "Mock";
    public bool IsConnected => _isConnected;
    public int ReconnectCount => 0;
    public DateTime? LastConnectedAt => _isConnected ? DateTime.UtcNow : null;
    public TimeSpan CurrentReconnectDelay => TimeSpan.Zero;
    public CircuitBreakerState CircuitBreakerState => CircuitBreakerState.Closed;

    public event EventHandler<PriceTicker>? OnPriceUpdate;
#pragma warning disable CS0067 // Events raised on-demand by callers; unused in mock but required by interface
    public event EventHandler<OrderBook>? OnOrderBookUpdate;
    public event EventHandler<Trade>? OnTrade;
    public event EventHandler<List<Balance>>? OnBalanceUpdate;
#pragma warning restore CS0067
    public event EventHandler<bool>? OnConnectionStateChanged;

    public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        _isConnected = true;
        _logger.LogInformation("MockExchangeAdapter connected (test mode)");
        OnConnectionStateChanged?.Invoke(this, true);

        // Emit price updates every 2 seconds so SignalR streams have data during tests.
        _priceTimer = new Timer(_ =>
        {
            foreach (var (symbol, price) in BaseP)
            {
                OnPriceUpdate?.Invoke(this, MakeTicker(symbol, price));
            }
        }, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));

        return Task.FromResult(true);
    }

    public Task DisconnectAsync()
    {
        _priceTimer?.Dispose();
        _isConnected = false;
        OnConnectionStateChanged?.Invoke(this, false);
        return Task.CompletedTask;
    }

    public Task<PriceTicker?> GetPriceAsync(string symbol, CancellationToken cancellationToken = default)
    {
        var price = BaseP.GetValueOrDefault(symbol.ToUpper(), 1m);
        return Task.FromResult<PriceTicker?>(MakeTicker(symbol, price));
    }

    public Task<List<PriceTicker>> GetPricesAsync(IEnumerable<string> symbols, CancellationToken cancellationToken = default)
    {
        var tickers = symbols
            .Select(s => MakeTicker(s, BaseP.GetValueOrDefault(s.ToUpper(), 1m)))
            .ToList();
        return Task.FromResult(tickers);
    }

    public Task<OrderBook?> GetOrderBookAsync(string symbol, int depth = 10, CancellationToken cancellationToken = default)
    {
        var mid = BaseP.GetValueOrDefault(symbol.ToUpper(), 1m);
        var ob = new OrderBook
        {
            Symbol = symbol,
            LastUpdated = DateTime.UtcNow,
            Bids = Enumerable.Range(1, depth)
                .Select(i => new OrderBookEntry { Price = mid - i * 10, Quantity = i, Count = 1 })
                .ToList(),
            Asks = Enumerable.Range(1, depth)
                .Select(i => new OrderBookEntry { Price = mid + i * 10, Quantity = i, Count = 1 })
                .ToList(),
        };
        return Task.FromResult<OrderBook?>(ob);
    }

    public Task<List<Ohlcv>> GetOhlcvAsync(string symbol, string timeframe, int limit = 100, CancellationToken cancellationToken = default)
    {
        var close = BaseP.GetValueOrDefault(symbol.ToUpper(), 1m);
        var now = DateTime.UtcNow;
        var candles = Enumerable.Range(0, limit)
            .Select(i => new Ohlcv
            {
                Timestamp = now.AddHours(-limit + i),
                Open = close * 0.99m,
                High = close * 1.01m,
                Low = close * 0.98m,
                Close = close,
                Volume = 100m + i,
            })
            .ToList();
        return Task.FromResult(candles);
    }

    public Task<List<Balance>> GetBalanceAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new List<Balance>
        {
            new() { Asset = "USD", Available = 10_000m, Hold = 0m },
            new() { Asset = "BTC", Available = 0.5m,   Hold = 0m },
            new() { Asset = "ETH", Available = 2m,      Hold = 0m },
        });
    }

    public Task<List<Trade>> GetTradesAsync(string symbol, int limit = 50, CancellationToken cancellationToken = default)
    {
        var price = BaseP.GetValueOrDefault(symbol.ToUpper(), 1m);
        var trades = Enumerable.Range(0, Math.Min(limit, 5))
            .Select(i => new Trade
            {
                Id = $"mock-trade-{i}",
                Symbol = symbol,
                Price = price,
                Quantity = 0.1m,
                Side = i % 2 == 0 ? "buy" : "sell",
                Timestamp = DateTime.UtcNow.AddMinutes(-i),
            })
            .ToList();
        return Task.FromResult(trades);
    }

    public Task<PortfolioSummary> GetPortfolioSummaryAsync(CancellationToken cancellationToken = default)
    {
        var balances = new List<Balance>
        {
            new() { Asset = "USD", Available = 10_000m, Hold = 0m },
            new() { Asset = "BTC", Available = 0.5m,    Hold = 0m },
            new() { Asset = "ETH", Available = 2m,       Hold = 0m },
        };

        var btcValue = 0.5m * BaseP["BTC/USD"];
        var ethValue = 2m * BaseP["ETH/USD"];
        var total = 10_000m + btcValue + ethValue;

        return Task.FromResult(new PortfolioSummary
        {
            TotalValue = total,
            AvailableCapital = 10_000m,
            UnrealizedPnL = 0m,
            Balances = balances,
            Positions = new List<Position>(),
            LastUpdated = DateTime.UtcNow,
        });
    }

    private static PriceTicker MakeTicker(string symbol, decimal price) => new()
    {
        Asset = symbol,
        Price = price,
        Bid = price - 5m,
        Ask = price + 5m,
        High24h = price * 1.02m,
        Low24h = price * 0.98m,
        Change24h = price * 0.01m,
        ChangePercent24h = 1.0m,
        Volume24h = 1000m,
        LastUpdated = DateTime.UtcNow,
    };
}

using MarketDataService.Models;

namespace MarketDataService.Services;

public interface IMarketDataCache
{
    void SetPrice(PriceTicker ticker);
    PriceTicker? GetPrice(string symbol);
    List<PriceTicker> GetAllPrices();
    void SetOrderBook(OrderBook orderBook);
    OrderBook? GetOrderBook(string symbol);
    void SetOhlcv(string symbol, string timeframe, List<Ohlcv> data);
    List<Ohlcv>? GetOhlcv(string symbol, string timeframe);
    void SetBalance(List<Balance> balances);
    List<Balance>? GetBalance();
    void SetPortfolioSummary(PortfolioSummary summary);
    PortfolioSummary? GetPortfolioSummary();
    void SetTechnicalIndicator(TechnicalIndicator indicator);
    TechnicalIndicator? GetTechnicalIndicator(string symbol, string timeframe);
    void Clear();
}

public class InMemoryMarketDataCache : IMarketDataCache
{
    private readonly Dictionary<string, PriceTicker> _prices = new();
    private readonly Dictionary<string, OrderBook> _orderBooks = new();
    private readonly Dictionary<(string symbol, string timeframe), List<Ohlcv>> _ohlcv = new();
    private List<Balance>? _balances;
    private PortfolioSummary? _portfolioSummary;
    private readonly Dictionary<(string symbol, string timeframe), TechnicalIndicator> _indicators = new();
    
    private readonly TimeSpan _priceTtl = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _orderBookTtl = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _balanceTtl = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _ohlcvTtl = TimeSpan.FromMinutes(1);
    
    private readonly Dictionary<string, DateTime> _priceTimestamps = new();
    private readonly Dictionary<string, DateTime> _orderBookTimestamps = new();
    private DateTime? _balanceTimestamp;
    private readonly Dictionary<(string symbol, string timeframe), DateTime> _ohlcvTimestamps = new();
    private DateTime? _portfolioTimestamp;
    private readonly Dictionary<(string symbol, string timeframe), DateTime> _indicatorTimestamps = new();

    public void SetPrice(PriceTicker ticker)
    {
        lock (_prices)
        {
            _prices[ticker.Asset] = ticker;
            _priceTimestamps[ticker.Asset] = DateTime.UtcNow;
        }
    }

    public PriceTicker? GetPrice(string symbol)
    {
        lock (_prices)
        {
            if (_prices.TryGetValue(symbol, out var ticker))
            {
                if (_priceTimestamps.TryGetValue(symbol, out var timestamp))
                {
                    if (DateTime.UtcNow - timestamp < _priceTtl)
                    {
                        return ticker;
                    }
                }
            }
            return null;
        }
    }

    public List<PriceTicker> GetAllPrices()
    {
        lock (_prices)
        {
            var result = new List<PriceTicker>();
            var now = DateTime.UtcNow;
            
            foreach (var kvp in _prices)
            {
                if (_priceTimestamps.TryGetValue(kvp.Key, out var timestamp))
                {
                    if (now - timestamp < _priceTtl)
                    {
                        result.Add(kvp.Value);
                    }
                }
            }
            
            return result;
        }
    }

    public void SetOrderBook(OrderBook orderBook)
    {
        lock (_orderBooks)
        {
            _orderBooks[orderBook.Symbol] = orderBook;
            _orderBookTimestamps[orderBook.Symbol] = DateTime.UtcNow;
        }
    }

    public OrderBook? GetOrderBook(string symbol)
    {
        lock (_orderBooks)
        {
            if (_orderBooks.TryGetValue(symbol, out var orderBook))
            {
                if (_orderBookTimestamps.TryGetValue(symbol, out var timestamp))
                {
                    if (DateTime.UtcNow - timestamp < _orderBookTtl)
                    {
                        return orderBook;
                    }
                }
            }
            return null;
        }
    }

    public void SetOhlcv(string symbol, string timeframe, List<Ohlcv> data)
    {
        lock (_ohlcv)
        {
            _ohlcv[(symbol, timeframe)] = data;
            _ohlcvTimestamps[(symbol, timeframe)] = DateTime.UtcNow;
        }
    }

    public List<Ohlcv>? GetOhlcv(string symbol, string timeframe)
    {
        lock (_ohlcv)
        {
            var key = (symbol, timeframe);
            if (_ohlcv.TryGetValue(key, out var data))
            {
                if (_ohlcvTimestamps.TryGetValue(key, out var timestamp))
                {
                    if (DateTime.UtcNow - timestamp < _ohlcvTtl)
                    {
                        return data;
                    }
                }
            }
            return null;
        }
    }

    public void SetBalance(List<Balance> balances)
    {
        lock (_prices)
        {
            _balances = balances;
            _balanceTimestamp = DateTime.UtcNow;
        }
    }

    public List<Balance>? GetBalance()
    {
        lock (_prices)
        {
            if (_balances != null && _balanceTimestamp.HasValue)
            {
                if (DateTime.UtcNow - _balanceTimestamp.Value < _balanceTtl)
                {
                    return _balances;
                }
            }
            return null;
        }
    }

    public void SetPortfolioSummary(PortfolioSummary summary)
    {
        lock (_prices)
        {
            _portfolioSummary = summary;
            _portfolioTimestamp = DateTime.UtcNow;
        }
    }

    public PortfolioSummary? GetPortfolioSummary()
    {
        lock (_prices)
        {
            if (_portfolioSummary != null && _portfolioTimestamp.HasValue)
            {
                if (DateTime.UtcNow - _portfolioTimestamp.Value < _balanceTtl)
                {
                    return _portfolioSummary;
                }
            }
            return null;
        }
    }

    public void SetTechnicalIndicator(TechnicalIndicator indicator)
    {
        lock (_indicators)
        {
            _indicators[(indicator.Symbol, indicator.Timeframe)] = indicator;
            _indicatorTimestamps[(indicator.Symbol, indicator.Timeframe)] = DateTime.UtcNow;
        }
    }

    public TechnicalIndicator? GetTechnicalIndicator(string symbol, string timeframe)
    {
        lock (_indicators)
        {
            var key = (symbol, timeframe);
            if (_indicators.TryGetValue(key, out var indicator))
            {
                if (_indicatorTimestamps.TryGetValue(key, out var timestamp))
                {
                    if (DateTime.UtcNow - timestamp < _ohlcvTtl)
                    {
                        return indicator;
                    }
                }
            }
            return null;
        }
    }

    public void Clear()
    {
        lock (_prices)
        {
            _prices.Clear();
            _orderBooks.Clear();
            _ohlcv.Clear();
            _balances = null;
            _portfolioSummary = null;
            _indicators.Clear();
        }
    }
}

using MarketDataService.Adapters;
using MarketDataService.Models;
using MarketDataService.Services;
using Microsoft.AspNetCore.Mvc;

namespace MarketDataService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PricesController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<PricesController> _logger;

    public PricesController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<PricesController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<PriceTicker>>> GetPrices([FromQuery] string? symbols = null)
    {
        var symbolList = string.IsNullOrEmpty(symbols) 
            ? new[] { "BTC/USD", "ETH/USD", "SOL/USD" }
            : symbols.Split(',');

        var cachedPrices = _cache.GetAllPrices();
        var cachedDict = cachedPrices.ToDictionary(p => p.Asset);

        var result = new List<PriceTicker>();
        var needsFetch = new List<string>();

        foreach (var symbol in symbolList)
        {
            if (cachedDict.TryGetValue(symbol, out var cached))
            {
                result.Add(cached);
            }
            else
            {
                needsFetch.Add(symbol);
            }
        }

        if (needsFetch.Count > 0)
        {
            try
            {
                var freshPrices = await _exchangeAdapter.GetPricesAsync(needsFetch);
                foreach (var price in freshPrices)
                {
                    _cache.SetPrice(price);
                    result.Add(price);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch prices");
            }
        }

        return Ok(result);
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<PriceTicker>> GetPrice(string symbol)
    {
        var cached = _cache.GetPrice(symbol);
        if (cached != null)
        {
            return Ok(cached);
        }

        try
        {
            var price = await _exchangeAdapter.GetPriceAsync(symbol);
            if (price != null)
            {
                _cache.SetPrice(price);
                return Ok(price);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch price for {Symbol}", symbol);
        }

        return NotFound();
    }
}

[ApiController]
[Route("api/[controller]")]
public class OrderBookController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<OrderBookController> _logger;

    public OrderBookController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<OrderBookController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("{symbol}")]
    public async Task<ActionResult<OrderBook>> GetOrderBook(string symbol, [FromQuery] int depth = 10)
    {
        var cached = _cache.GetOrderBook(symbol);
        if (cached != null)
        {
            return Ok(cached);
        }

        try
        {
            var orderBook = await _exchangeAdapter.GetOrderBookAsync(symbol, depth);
            if (orderBook != null)
            {
                _cache.SetOrderBook(orderBook);
                return Ok(orderBook);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch order book for {Symbol}", symbol);
        }

        return NotFound();
    }
}

[ApiController]
[Route("api/[controller]")]
public class OhlcvController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<OhlcvController> _logger;

    public OhlcvController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<OhlcvController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Ohlcv>>> GetOhlcv(
        [FromQuery] string symbol = "BTC/USD",
        [FromQuery] string timeframe = "1h",
        [FromQuery] int limit = 100)
    {
        var cached = _cache.GetOhlcv(symbol, timeframe);
        if (cached != null && cached.Count > 0)
        {
            return Ok(cached);
        }

        try
        {
            var ohlcv = await _exchangeAdapter.GetOhlcvAsync(symbol, timeframe, limit);
            if (ohlcv != null && ohlcv.Count > 0)
            {
                _cache.SetOhlcv(symbol, timeframe, ohlcv);
                return Ok(ohlcv);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch OHLCV for {Symbol}", symbol);
        }

        return NotFound();
    }
}

[ApiController]
[Route("api/[controller]")]
public class BalanceController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<BalanceController> _logger;

    public BalanceController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<BalanceController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<Balance>>> GetBalance()
    {
        var cached = _cache.GetBalance();
        if (cached != null)
        {
            return Ok(cached);
        }

        try
        {
            var balances = await _exchangeAdapter.GetBalanceAsync();
            if (balances != null)
            {
                _cache.SetBalance(balances);
                return Ok(balances);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch balance");
        }

        return NotFound();
    }
}

[ApiController]
[Route("api/[controller]")]
public class PortfolioController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<PortfolioController> _logger;

    public PortfolioController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<PortfolioController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<PortfolioSummary>> GetSummary()
    {
        var cached = _cache.GetPortfolioSummary();
        if (cached != null)
        {
            return Ok(cached);
        }

        try
        {
            var summary = await _exchangeAdapter.GetPortfolioSummaryAsync();
            if (summary != null)
            {
                _cache.SetPortfolioSummary(summary);
                return Ok(summary);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch portfolio summary");
        }

        return NotFound();
    }
}

[ApiController]
[Route("api/[controller]")]
public class IndicatorsController : ControllerBase
{
    private readonly ITechnicalIndicatorService _indicatorService;
    private readonly ILogger<IndicatorsController> _logger;

    public IndicatorsController(ITechnicalIndicatorService indicatorService, ILogger<IndicatorsController> logger)
    {
        _indicatorService = indicatorService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<TechnicalIndicator>> GetIndicator(
        [FromQuery] string symbol = "BTC/USD",
        [FromQuery] string timeframe = "1h")
    {
        try
        {
            var indicator = await _indicatorService.CalculateAsync(symbol, timeframe);
            if (indicator != null)
            {
                return Ok(indicator);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate indicator for {Symbol}", symbol);
        }

        return NotFound();
    }
}

[ApiController]
[Route("api/[controller]")]
public class StatusController : ControllerBase
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly ILogger<StatusController> _logger;

    public StatusController(IExchangeAdapter exchangeAdapter, ILogger<StatusController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _logger = logger;
    }

    [HttpGet]
    public ActionResult<ExchangeStatus> GetStatus()
    {
        return Ok(new ExchangeStatus
        {
            Exchange = _exchangeAdapter.ExchangeName,
            IsConnected = _exchangeAdapter.IsConnected
        });
    }
}

[ApiController]
[Route("api/[controller]")]
public class MacroController : ControllerBase
{
    // Approximate circulating supplies (millions) for cap-weighted BTC dominance.
    private const decimal BtcSupply = 19.7m;   // million BTC
    private const decimal EthSupply = 120.0m;  // million ETH
    private const decimal SolSupply = 550.0m;  // million SOL

    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<MacroController> _logger;

    public MacroController(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<MacroController> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Returns macro market signals derived from live Kraken ticker data.
    /// Trend, volatility regime, and a Fear/Greed proxy are computed from BTC/USD 24-hour data.
    /// BTC dominance is approximated using known circulating supplies for BTC, ETH, and SOL.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<MacroSignals>> GetMacroSignals()
    {
        var symbols = new[] { "BTC/USD", "ETH/USD", "SOL/USD" };

        // Try cache first, fall back to live fetch
        var tickers = symbols
            .Select(s => _cache.GetPrice(s))
            .OfType<PriceTicker>()
            .ToList();

        if (tickers.Count < symbols.Length)
        {
            try
            {
                var fresh = await _exchangeAdapter.GetPricesAsync(symbols);
                foreach (var t in fresh)
                    _cache.SetPrice(t);
                tickers = fresh;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch tickers for macro signals");
                if (tickers.Count == 0)
                    return StatusCode(503, new { error = "Kraken unavailable; no cached data." });
                // Continue with partial cache
            }
        }

        var btc = tickers.FirstOrDefault(t => t.Asset is "BTC/USD");
        if (btc is null)
            return StatusCode(503, new { error = "BTC/USD data not available." });

        // --- Trend ---
        var trend = btc.ChangePercent24h switch
        {
            > 2m  => "bullish",
            < -2m => "bearish",
            _     => "neutral"
        };

        // --- Volatility regime ---
        var dayRange = btc.Price > 0 ? (btc.High24h - btc.Low24h) / btc.Price * 100m : 0m;
        var volatilityRegime = dayRange switch
        {
            > 8m => "high",
            < 3m => "low",
            _    => "normal"
        };

        // --- Fear & Greed proxy (0–100) ---
        // Momentum contribution: 50 neutral, +5 per 1% 24h gain, clamped to [0, 100].
        var rawFgi = 50m + btc.ChangePercent24h * 5m;
        // Volatility drag: high volatility implies increased fear.
        if (volatilityRegime == "high")  rawFgi -= 10m;
        if (volatilityRegime == "low")   rawFgi += 5m;
        var fgi = Math.Clamp(rawFgi, 0m, 100m);

        var sentiment = fgi switch
        {
            < 25m  => "extreme fear",
            < 45m  => "fear",
            < 55m  => "neutral",
            < 75m  => "greed",
            _      => "extreme greed"
        };

        // --- BTC dominance (cap-weighted, BTC+ETH+SOL only) ---
        var eth = tickers.FirstOrDefault(t => t.Asset is "ETH/USD");
        var sol = tickers.FirstOrDefault(t => t.Asset is "SOL/USD");

        decimal? btcDominance = null;
        decimal? totalMarketCap = null;

        if (eth is not null && sol is not null)
        {
            var btcCap = btc.Price * BtcSupply;
            var ethCap = eth.Price * EthSupply;
            var solCap = sol.Price * SolSupply;
            var trackedCap = btcCap + ethCap + solCap;

            // Scale to approximate full-market dominance:
            // BTC historically accounts for ~50–55 % of total market cap;
            // BTC+ETH+SOL are roughly 70–75 % of total. We scale up to account for altcoins.
            const decimal trackedFraction = 0.72m;
            var estimatedTotal = trackedCap / trackedFraction;

            btcDominance  = Math.Round(btcCap / estimatedTotal * 100m, 1);
            totalMarketCap = Math.Round(estimatedTotal / 1_000_000m, 3); // Trillions USD
        }

        return Ok(new MacroSignals
        {
            Trend            = trend,
            VolatilityRegime = volatilityRegime,
            FearGreedIndex   = Math.Round(fgi, 1),
            Sentiment        = sentiment,
            BtcDominance     = btcDominance,
            TotalMarketCap   = totalMarketCap,
            LastUpdated      = DateTime.UtcNow
        });
    }
}

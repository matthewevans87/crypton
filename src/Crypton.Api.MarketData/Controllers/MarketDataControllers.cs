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

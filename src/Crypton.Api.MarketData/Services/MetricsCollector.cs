using MarketDataService.Adapters;
using MarketDataService.Services;

namespace MarketDataService.Services;

public class Alert
{
    public string Severity { get; set; } = "";
    public string Message { get; set; } = "";
    public string Metric { get; set; } = "";
}

public class MetricsResponse
{
    public bool IsHealthy { get; set; }
    public Dictionary<string, object> Metrics { get; set; } = new();
    public List<Alert> ActiveAlerts { get; set; } = new();
    public DateTime Timestamp { get; set; }
}

public interface IMetricsCollector
{
    void RecordWsConnected(bool connected);
    void RecordReconnect();
    void RecordPriceUpdateLatency(TimeSpan latency);
    void RecordApiRequest();
    void RecordRateLimitRemaining(int remaining);
    void RecordCacheHit();
    void RecordCacheMiss();
    void RecordPriceStaleness(TimeSpan staleness);
    void RecordCircuitBreakerState(CircuitBreakerState state);
    void RecordConnectionStateChanged(bool isConnected);
    
    MetricsResponse GetMetrics();
    List<Alert> GetActiveAlerts();
    bool IsPricesStale();
}

public class MetricsCollector : IMetricsCollector
{
    private readonly IExchangeAdapter _exchangeAdapter;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<MetricsCollector> _logger;
    
    private bool _wsConnected;
    private int _reconnectCount;
    private long _totalLatencyMs;
    private int _latencySampleCount;
    private int _apiRequestsTotal;
    private int _rateLimitRemaining;
    private long _cacheHits;
    private long _cacheMisses;
    private DateTime _lastPriceUpdate;
    private CircuitBreakerState _circuitBreakerState = CircuitBreakerState.Closed;
    private DateTime _lastConnectionStateChange;
    private bool _lastKnownConnectionState;
    
    private readonly object _lock = new();
    
    private readonly TimeSpan _stalenessWarningThreshold = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _stalenessErrorThreshold = TimeSpan.FromSeconds(60);
    
    public MetricsCollector(IExchangeAdapter exchangeAdapter, IMarketDataCache cache, ILogger<MetricsCollector> logger)
    {
        _exchangeAdapter = exchangeAdapter;
        _cache = cache;
        _logger = logger;
        _lastPriceUpdate = DateTime.UtcNow;
        _lastConnectionStateChange = DateTime.UtcNow;
    }
    
    public void RecordWsConnected(bool connected)
    {
        lock (_lock)
        {
            _wsConnected = connected;
        }
    }
    
    public void RecordReconnect()
    {
        lock (_lock)
        {
            _reconnectCount++;
        }
    }
    
    public void RecordPriceUpdateLatency(TimeSpan latency)
    {
        lock (_lock)
        {
            _totalLatencyMs += (long)latency.TotalMilliseconds;
            _latencySampleCount++;
            _lastPriceUpdate = DateTime.UtcNow;
        }
    }
    
    public void RecordApiRequest()
    {
        lock (_lock)
        {
            _apiRequestsTotal++;
        }
    }
    
    public void RecordRateLimitRemaining(int remaining)
    {
        lock (_lock)
        {
            _rateLimitRemaining = remaining;
        }
    }
    
    public void RecordCacheHit()
    {
        lock (_lock)
        {
            _cacheHits++;
        }
    }
    
    public void RecordCacheMiss()
    {
        lock (_lock)
        {
            _cacheMisses++;
        }
    }
    
    public void RecordPriceStaleness(TimeSpan staleness)
    {
        lock (_lock)
        {
            _lastPriceUpdate = DateTime.UtcNow - staleness;
        }
    }
    
    public void RecordCircuitBreakerState(CircuitBreakerState state)
    {
        lock (_lock)
        {
            _circuitBreakerState = state;
        }
    }

    public void RecordConnectionStateChanged(bool isConnected)
    {
        lock (_lock)
        {
            _lastConnectionStateChange = DateTime.UtcNow;
            _lastKnownConnectionState = isConnected;
        }
    }
    
    public bool IsPricesStale()
    {
        lock (_lock)
        {
            return DateTime.UtcNow - _lastPriceUpdate > _stalenessWarningThreshold;
        }
    }
    
    public MetricsResponse GetMetrics()
    {
        lock (_lock)
        {
            var alerts = GetActiveAlerts();
            var avgLatency = _latencySampleCount > 0 
                ? (double)_totalLatencyMs / _latencySampleCount 
                : 0;
            
            var totalCacheRequests = _cacheHits + _cacheMisses;
            var cacheHitRate = totalCacheRequests > 0 
                ? (double)_cacheHits / totalCacheRequests * 100 
                : 0;
            
            return new MetricsResponse
            {
                IsHealthy = alerts.All(a => a.Severity != "error"),
                Metrics = new Dictionary<string, object>
                {
                    ["ws.connected"] = _wsConnected ? 1 : 0,
                    ["ws.reconnects.total"] = _reconnectCount,
                    ["ws.latency.ms"] = Math.Round(avgLatency, 2),
                    ["api.requests.total"] = _apiRequestsTotal,
                    ["api.rate_limit.remaining"] = _rateLimitRemaining,
                    ["cache.hits"] = _cacheHits,
                    ["cache.misses"] = _cacheMisses,
                    ["cache.hit_rate.percent"] = Math.Round(cacheHitRate, 2),
                    ["prices.staleness.seconds"] = (DateTime.UtcNow - _lastPriceUpdate).TotalSeconds,
                    ["circuit_breaker.state"] = (int)_circuitBreakerState,
                    ["exchange.connected"] = _exchangeAdapter.IsConnected,
                    ["exchange.reconnect_count"] = _exchangeAdapter.ReconnectCount,
                    ["exchange.circuit_breaker_state"] = _circuitBreakerState.ToString()
                },
                ActiveAlerts = alerts,
                Timestamp = DateTime.UtcNow
            };
        }
    }
    
    public List<Alert> GetActiveAlerts()
    {
        lock (_lock)
        {
            var alerts = new List<Alert>();
            
            var staleness = DateTime.UtcNow - _lastPriceUpdate;
            
            if (staleness > _stalenessErrorThreshold)
            {
                alerts.Add(new Alert
                {
                    Severity = "error",
                    Message = $"Price data stale for {staleness.TotalSeconds:F0} seconds",
                    Metric = "prices.staleness.seconds"
                });
                _logger.LogError("Price data is stale: {Staleness}s", staleness.TotalSeconds);
            }
            else if (staleness > _stalenessWarningThreshold)
            {
                alerts.Add(new Alert
                {
                    Severity = "warning",
                    Message = $"Price data stale for {staleness.TotalSeconds:F0} seconds",
                    Metric = "prices.staleness.seconds"
                });
                _logger.LogWarning("Price data is stale: {Staleness}s", staleness.TotalSeconds);
            }
            
            if (!_wsConnected)
            {
                var disconnectedDuration = DateTime.UtcNow - _lastConnectionStateChange;
                if (disconnectedDuration > TimeSpan.FromMinutes(5))
                {
                    alerts.Add(new Alert
                    {
                        Severity = "warning",
                        Message = $"WebSocket disconnected for {disconnectedDuration.TotalMinutes:F1} minutes",
                        Metric = "ws.connected"
                    });
                    _logger.LogWarning("WebSocket disconnected for {Duration}m", disconnectedDuration.TotalMinutes);
                }
            }
            
            if (_circuitBreakerState == CircuitBreakerState.Open)
            {
                alerts.Add(new Alert
                {
                    Severity = "error",
                    Message = "Circuit breaker is open - exchange API unavailable",
                    Metric = "circuit_breaker.state"
                });
                _logger.LogError("Circuit breaker is open");
            }
            
            if (_rateLimitRemaining > 0 && _rateLimitRemaining < 3)
            {
                alerts.Add(new Alert
                {
                    Severity = "warning",
                    Message = $"Rate limit nearly exhausted: {_rateLimitRemaining} requests remaining",
                    Metric = "api.rate_limit.remaining"
                });
                _logger.LogWarning("Rate limit nearly exhausted: {Remaining} remaining", _rateLimitRemaining);
            }
            
            return alerts;
        }
    }
}

using MarketDataService.Adapters;
using MarketDataService.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace MarketDataService.Tests;

public class CircuitBreakerTests
{
    private readonly Mock<ILogger<CircuitBreaker>> _mockLogger;
    private readonly CircuitBreakerOptions _options;

    public CircuitBreakerTests()
    {
        _mockLogger = new Mock<ILogger<CircuitBreaker>>();
        _options = new CircuitBreakerOptions
        {
            FailureThreshold = 3,
            OpenDuration = TimeSpan.FromSeconds(1),
            SuccessThreshold = 2
        };
    }

    [Fact]
    public void Constructor_InitiallyClosed()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
    }

    [Fact]
    public void CanExecute_WhenClosed_ReturnsTrue()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        Assert.True(circuitBreaker.CanExecute());
    }

    [Fact]
    public void CanExecute_WhenHalfOpen_ReturnsTrue()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        
        Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
        
        Thread.Sleep(1500);
        
        Assert.True(circuitBreaker.CanExecute());
        Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
    }

    [Fact]
    public void RecordFailure_OpensAfterThreshold()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        
        Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
        
        circuitBreaker.RecordFailure();
        
        Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
    }

    [Fact]
    public void RecordSuccess_InHalfOpenState_ClosesAfterThreshold()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
        
        Thread.Sleep(1500);
        circuitBreaker.CanExecute();
        Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
        
        circuitBreaker.RecordSuccess();
        Assert.Equal(CircuitBreakerState.HalfOpen, circuitBreaker.State);
        
        circuitBreaker.RecordSuccess();
        
        Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
    }

    [Fact]
    public void RecordSuccess_InClosedState_ResetsFailureCount()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        
        circuitBreaker.RecordSuccess();
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        
        Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
    }

    [Fact]
    public void RecordFailure_InHalfOpenState_Reopens()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        
        Thread.Sleep(1500);
        circuitBreaker.CanExecute();
        
        circuitBreaker.RecordFailure();
        
        Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
    }

    [Fact]
    public void CanExecute_WhenOpen_BeforeTimeout_ReturnsFalse()
    {
        var fastOptions = new CircuitBreakerOptions
        {
            FailureThreshold = 1,
            OpenDuration = TimeSpan.FromSeconds(10),
            SuccessThreshold = 1
        };
        
        var circuitBreaker = new CircuitBreaker(fastOptions, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        
        Assert.Equal(CircuitBreakerState.Open, circuitBreaker.State);
        Assert.False(circuitBreaker.CanExecute());
    }

    [Fact]
    public void Reset_ClosesCircuit()
    {
        var circuitBreaker = new CircuitBreaker(_options, _mockLogger.Object);
        
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        circuitBreaker.RecordFailure();
        
        circuitBreaker.Reset();
        
        Assert.Equal(CircuitBreakerState.Closed, circuitBreaker.State);
        Assert.True(circuitBreaker.CanExecute());
    }
}

public class MetricsCollectorTests
{
    private readonly Mock<ILogger<MetricsCollector>> _mockLogger;
    private readonly Mock<IExchangeAdapter> _mockAdapter;
    private readonly Mock<IMarketDataCache> _mockCache;

    public MetricsCollectorTests()
    {
        _mockLogger = new Mock<ILogger<MetricsCollector>>();
        _mockAdapter = new Mock<IExchangeAdapter>();
        _mockCache = new Mock<IMarketDataCache>();
        
        _mockAdapter.Setup(a => a.IsConnected).Returns(true);
        _mockAdapter.Setup(a => a.ReconnectCount).Returns(0);
        _mockAdapter.Setup(a => a.CircuitBreakerState).Returns(Services.CircuitBreakerState.Closed);
    }

    [Fact]
    public void GetMetrics_ReturnsHealthy_WhenNoAlerts()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        var metrics = collector.GetMetrics();
        
        Assert.True(metrics.IsHealthy);
    }

    [Fact]
    public void RecordWsConnected_UpdatesMetric()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordWsConnected(true);
        
        var metrics = collector.GetMetrics();
        Assert.Equal(1, metrics.Metrics["ws.connected"]);
    }

    [Fact]
    public void RecordReconnect_IncrementsCount()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordReconnect();
        collector.RecordReconnect();
        
        var metrics = collector.GetMetrics();
        Assert.Equal(2, metrics.Metrics["ws.reconnects.total"]);
    }

    [Fact]
    public void RecordCacheHit_IncrementsCount()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordCacheHit();
        collector.RecordCacheHit();
        collector.RecordCacheMiss();
        
        var metrics = collector.GetMetrics();
        Assert.Equal(2L, (long)metrics.Metrics["cache.hits"]);
        Assert.Equal(1L, (long)metrics.Metrics["cache.misses"]);
    }

    [Fact]
    public void RecordCircuitBreakerState_UpdatesMetric()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordCircuitBreakerState(Services.CircuitBreakerState.Open);
        
        var metrics = collector.GetMetrics();
        Assert.Equal((int)Services.CircuitBreakerState.Open, metrics.Metrics["circuit_breaker.state"]);
    }

    [Fact]
    public void GetActiveAlerts_ReturnsError_WhenPricesStale()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordPriceStaleness(TimeSpan.FromSeconds(65));
        
        var alerts = collector.GetActiveAlerts();
        
        Assert.Contains(alerts, a => a.Severity == "error" && a.Metric == "prices.staleness.seconds");
    }

    [Fact]
    public void GetActiveAlerts_ReturnsWarning_WhenPricesModeratelyStale()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordPriceStaleness(TimeSpan.FromSeconds(35));
        
        var alerts = collector.GetActiveAlerts();
        
        Assert.Contains(alerts, a => a.Severity == "warning" && a.Metric == "prices.staleness.seconds");
    }

    [Fact]
    public void GetActiveAlerts_ReturnsError_WhenCircuitBreakerOpen()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordCircuitBreakerState(Services.CircuitBreakerState.Open);
        
        var alerts = collector.GetActiveAlerts();
        
        Assert.Contains(alerts, a => a.Severity == "error" && a.Metric == "circuit_breaker.state");
    }

    [Fact]
    public void IsPricesStale_ReturnsTrue_WhenStale()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordPriceStaleness(TimeSpan.FromSeconds(35));
        
        Assert.True(collector.IsPricesStale());
    }

    [Fact]
    public void IsPricesStale_ReturnsFalse_WhenFresh()
    {
        var collector = new MetricsCollector(_mockAdapter.Object, _mockCache.Object, _mockLogger.Object);
        
        collector.RecordPriceUpdateLatency(TimeSpan.Zero);
        
        Assert.False(collector.IsPricesStale());
    }
}

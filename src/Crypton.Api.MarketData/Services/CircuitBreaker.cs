namespace MarketDataService.Services;

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

public class CircuitBreakerOptions
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);
    public int SuccessThreshold { get; set; } = 2;
}

public class CircuitBreaker
{
    private readonly CircuitBreakerOptions _options;
    private readonly ILogger<CircuitBreaker> _logger;
    
    private CircuitBreakerState _state = CircuitBreakerState.Closed;
    private int _failureCount;
    private int _successCount;
    private DateTime _lastFailureTime;
    private readonly object _lock = new();

    public CircuitBreakerState State => _state;
    public int FailureCount => _failureCount;
    public int SuccessCount => _successCount;

    public CircuitBreaker(CircuitBreakerOptions options, ILogger<CircuitBreaker> logger)
    {
        _options = options;
        _logger = logger;
    }

    public bool CanExecute()
    {
        lock (_lock)
        {
            return _state switch
            {
                CircuitBreakerState.Closed => true,
                CircuitBreakerState.HalfOpen => true,
                CircuitBreakerState.Open when DateTime.UtcNow - _lastFailureTime > _options.OpenDuration => TryHalfOpen(),
                _ => false
            };
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _successCount++;
                if (_successCount >= _options.SuccessThreshold)
                {
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                    _successCount = 0;
                    _logger.LogInformation("Circuit breaker closed after {SuccessCount} successful attempts",
                        _successCount);
                }
            }
            else if (_state == CircuitBreakerState.Closed)
            {
                _failureCount = 0;
            }
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            _failureCount++;
            _lastFailureTime = DateTime.UtcNow;

            if (_state == CircuitBreakerState.HalfOpen)
            {
                _state = CircuitBreakerState.Open;
                _successCount = 0;
                _logger.LogWarning("Circuit breaker opened after failure in half-open state");
            }
            else if (_state == CircuitBreakerState.Closed && _failureCount >= _options.FailureThreshold)
            {
                _state = CircuitBreakerState.Open;
                _logger.LogWarning("Circuit breaker opened after {FailureCount} consecutive failures",
                    _failureCount);
            }
        }
    }

    private bool TryHalfOpen()
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.Open)
            {
                _state = CircuitBreakerState.HalfOpen;
                _successCount = 0;
                _logger.LogInformation("Circuit breaker half-open - allowing test requests");
                return true;
            }
            return _state == CircuitBreakerState.HalfOpen;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _state = CircuitBreakerState.Closed;
            _failureCount = 0;
            _successCount = 0;
            _lastFailureTime = DateTime.MinValue;
        }
    }
}

public class CircuitBreakerOpenException : Exception
{
    public CircuitBreakerOpenException(string message) : base(message) { }
}

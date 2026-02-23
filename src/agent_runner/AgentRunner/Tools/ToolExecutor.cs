using System.Diagnostics;

namespace AgentRunner.Tools;

public class ToolExecutor
{
    private readonly Dictionary<string, Tool> _tools = new();
    private readonly int _defaultTimeoutSeconds;
    private readonly Dictionary<string, CircuitBreaker> _circuitBreakers = new();
    private readonly int _maxConcurrentExecutions;

    public ToolExecutor(int defaultTimeoutSeconds = 30, int maxConcurrentExecutions = 5)
    {
        _defaultTimeoutSeconds = defaultTimeoutSeconds;
        _maxConcurrentExecutions = maxConcurrentExecutions;
    }

    public void RegisterTool(Tool tool)
    {
        _tools[tool.Name] = tool;
        _circuitBreakers[tool.Name] = new CircuitBreaker(
            failureThreshold: 5,
            resetTimeoutSeconds: 60,
            halfOpenAttempts: 3);
    }

    public Tool? GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IReadOnlyDictionary<string, Tool> GetAllTools() => _tools;

    public CircuitBreaker? GetCircuitBreaker(string toolName)
    {
        return _circuitBreakers.TryGetValue(toolName, out var cb) ? cb : null;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(call.ToolName, out var tool))
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Tool '{call.ToolName}' not found"
            };
        }

        var circuitBreaker = _circuitBreakers[call.ToolName];
        
        if (circuitBreaker.IsOpen)
        {
            return new ToolResult
            {
                Success = false,
                Error = $"Circuit breaker is open for tool '{call.ToolName}'. Too many recent failures."
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var timeout = TimeSpan.FromSeconds(_defaultTimeoutSeconds);
            var result = await ExecuteWithTimeoutAsync(tool, call.Parameters, timeout, cancellationToken);
            stopwatch.Stop();

            result.Duration = stopwatch.Elapsed;
            circuitBreaker.RecordSuccess();

            return result;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            circuitBreaker.RecordFailure();
            
            return new ToolResult
            {
                Success = false,
                Error = "Tool execution was cancelled",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            circuitBreaker.RecordFailure();

            return new ToolResult
            {
                Success = false,
                Error = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    private async Task<ToolResult> ExecuteWithTimeoutAsync(
        Tool tool, 
        Dictionary<string, object> parameters, 
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        return await tool.ExecuteAsync(parameters, cts.Token);
    }

    public async Task<List<ToolResult>> ExecuteBatchAsync(IEnumerable<ToolCall> calls, CancellationToken cancellationToken = default)
    {
        var callList = calls.ToList();
        
        if (callList.Count == 0)
            return new List<ToolResult>();

        if (callList.Count == 1)
        {
            var result = await ExecuteAsync(callList[0], cancellationToken);
            return new List<ToolResult> { result };
        }

        var semaphore = new SemaphoreSlim(_maxConcurrentExecutions);
        var tasks = callList.Select(async call =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return await ExecuteAsync(call, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }
}

public class CircuitBreaker
{
    private readonly int _failureThreshold;
    private readonly int _resetTimeoutSeconds;
    private readonly int _halfOpenAttempts;
    
    private int _failureCount;
    private int _successCount;
    private DateTime _lastFailureTime;
    private CircuitBreakerState _state;
    private readonly object _lock = new();

    public CircuitBreaker(int failureThreshold = 5, int resetTimeoutSeconds = 60, int halfOpenAttempts = 3)
    {
        _failureThreshold = failureThreshold;
        _resetTimeoutSeconds = resetTimeoutSeconds;
        _halfOpenAttempts = halfOpenAttempts;
        _state = CircuitBreakerState.Closed;
    }

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                return _state == CircuitBreakerState.Open || _state == CircuitBreakerState.HalfOpen;
            }
        }
    }

    public CircuitBreakerState State
    {
        get
        {
            lock (_lock)
            {
                if (_state == CircuitBreakerState.Open)
                {
                    if ((DateTime.UtcNow - _lastFailureTime).TotalSeconds >= _resetTimeoutSeconds)
                    {
                        _state = CircuitBreakerState.HalfOpen;
                        _successCount = 0;
                    }
                }
                return _state;
            }
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == CircuitBreakerState.HalfOpen)
            {
                _successCount++;
                if (_successCount >= _halfOpenAttempts)
                {
                    _state = CircuitBreakerState.Closed;
                    _failureCount = 0;
                }
            }
            else
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
            }
            else if (_failureCount >= _failureThreshold)
            {
                _state = CircuitBreakerState.Open;
            }
        }
    }

    public CircuitBreakerStatus GetStatus()
    {
        lock (_lock)
        {
            return new CircuitBreakerStatus
            {
                State = _state,
                FailureCount = _failureCount,
                SuccessCount = _successCount,
                LastFailureTime = _lastFailureTime,
                FailureThreshold = _failureThreshold,
                ResetTimeoutSeconds = _resetTimeoutSeconds
            };
        }
    }
}

public enum CircuitBreakerState
{
    Closed,
    Open,
    HalfOpen
}

public class CircuitBreakerStatus
{
    public CircuitBreakerState State { get; set; }
    public int FailureCount { get; set; }
    public int SuccessCount { get; set; }
    public DateTime LastFailureTime { get; set; }
    public int FailureThreshold { get; set; }
    public int ResetTimeoutSeconds { get; set; }
}

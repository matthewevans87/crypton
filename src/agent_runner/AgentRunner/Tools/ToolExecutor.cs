using System.Diagnostics;

namespace AgentRunner.Tools;

public class ToolExecutor
{
    private readonly Dictionary<string, Tool> _tools = new();
    private readonly int _defaultTimeoutSeconds;
    private readonly Dictionary<string, CircuitBreaker> _circuitBreakers = new();

    public ToolExecutor(int defaultTimeoutSeconds = 30)
    {
        _defaultTimeoutSeconds = defaultTimeoutSeconds;
    }

    public void RegisterTool(Tool tool)
    {
        _tools[tool.Name] = tool;
        _circuitBreakers[tool.Name] = new CircuitBreaker();
    }

    public Tool? GetTool(string name)
    {
        return _tools.TryGetValue(name, out var tool) ? tool : null;
    }

    public IReadOnlyDictionary<string, Tool> GetAllTools() => _tools;

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
                Error = $"Circuit breaker is open for tool '{call.ToolName}'"
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
        var tasks = calls.Select(call => ExecuteAsync(call, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }
}

public class CircuitBreaker
{
    private const int FailureThreshold = 5;
    private const int ResetTimeoutSeconds = 60;
    
    private int _failureCount;
    private DateTime _lastFailureTime;
    private bool _isOpen;

    public bool IsOpen
    {
        get
        {
            if (_failureCount < FailureThreshold)
                return false;

            if ((DateTime.UtcNow - _lastFailureTime).TotalSeconds > ResetTimeoutSeconds)
            {
                _failureCount = 0;
                return false;
            }

            return true;
        }
    }

    public void RecordSuccess()
    {
        _failureCount = 0;
        _isOpen = false;
    }

    public void RecordFailure()
    {
        _failureCount++;
        _lastFailureTime = DateTime.UtcNow;
        if (_failureCount >= FailureThreshold)
        {
            _isOpen = true;
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;

namespace AgentRunner.Execution;

/// <summary>
/// Wraps every tool invocation with per-tool circuit-breaker, exponential-backoff retry,
/// and a configurable timeout. Each tool gets its own <see cref="CircuitBreaker"/> instance.
/// </summary>
public sealed class ResilientToolExecutor : IToolExecutor
{
    private readonly AgentRunnerConfig _config;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _circuitBreakers = new();

    public ResilientToolExecutor(AgentRunnerConfig config)
    {
        _config = config;
    }

    public async Task<T> ExecuteWithResilienceAsync<T>(
        string toolName,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default)
    {
        var cb = _circuitBreakers.GetOrAdd(toolName,
            _ => new CircuitBreaker(failureThreshold: 5, resetTimeoutSeconds: 60, halfOpenAttempts: 3));

        if (cb.IsOpen)
            throw new InvalidOperationException(
                $"Circuit breaker is open for tool '{toolName}'. Too many recent failures.");

        var maxRetries = _config.Tools.MaxRetries;
        var maxDelaySeconds = _config.Tools.MaxRetryDelaySeconds;
        var timeoutSeconds = _config.Tools.DefaultTimeoutSeconds;

        Exception? lastException = null;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var result = await work(cts.Token);
                cb.RecordSuccess();
                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Per-invocation timeout — treat as transient
                cb.RecordFailure();
                lastException = new TimeoutException($"Tool '{toolName}' timed out after {timeoutSeconds}s");
            }
            catch (Exception ex)
            {
                cb.RecordFailure();
                lastException = ex;

                if (!IsTransient(ex) || attempt >= maxRetries)
                    throw;
            }

            if (attempt < maxRetries)
            {
                var delaySeconds = Math.Min(Math.Pow(2, attempt) * 2, maxDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
            }
        }

        throw lastException!;
    }

    private static bool IsTransient(Exception ex)
    {
        var msg = ex.Message;
        return msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("network", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("connection", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("unavailable", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("429", StringComparison.Ordinal)
            || msg.Contains("502", StringComparison.Ordinal)
            || msg.Contains("503", StringComparison.Ordinal)
            || msg.Contains("504", StringComparison.Ordinal);
    }
}

/// <summary>
/// Simple circuit breaker: Opens after <paramref name="failureThreshold"/> consecutive failures.
/// Enters half-open after <paramref name="resetTimeoutSeconds"/>; closes after
/// <paramref name="halfOpenAttempts"/> successes.
/// </summary>
public sealed class CircuitBreaker
{
    private enum State { Closed, Open, HalfOpen }

    private readonly int _failureThreshold;
    private readonly TimeSpan _resetTimeout;
    private readonly int _halfOpenAttempts;

    private State _state = State.Closed;
    private int _failureCount;
    private int _halfOpenSuccessCount;
    private DateTimeOffset _openedAt;
    private readonly object _lock = new();

    public bool IsOpen
    {
        get
        {
            lock (_lock)
            {
                if (_state == State.Open && DateTimeOffset.UtcNow >= _openedAt + _resetTimeout)
                {
                    _state = State.HalfOpen;
                    _halfOpenSuccessCount = 0;
                }
                return _state == State.Open;
            }
        }
    }

    public CircuitBreaker(int failureThreshold, int resetTimeoutSeconds, int halfOpenAttempts)
    {
        _failureThreshold = failureThreshold;
        _resetTimeout = TimeSpan.FromSeconds(resetTimeoutSeconds);
        _halfOpenAttempts = halfOpenAttempts;
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == State.HalfOpen)
            {
                _halfOpenSuccessCount++;
                if (_halfOpenSuccessCount >= _halfOpenAttempts)
                {
                    _state = State.Closed;
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
            if (_failureCount >= _failureThreshold)
            {
                _state = State.Open;
                _openedAt = DateTimeOffset.UtcNow;
            }
        }
    }
}

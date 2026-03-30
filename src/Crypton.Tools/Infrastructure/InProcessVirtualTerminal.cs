using System.Collections.Concurrent;
using System.Diagnostics;
using Crypton.Tools.Configuration;
using Spectre.Console;

namespace Crypton.Tools.Infrastructure;

/// <summary>
/// Runs a registered tool command in-process by building a Spectre <c>CommandApp</c> wired to a
/// capturing <see cref="IAnsiConsole"/>.  Wraps every invocation with per-tool circuit-breaker,
/// exponential-backoff retry, and a configurable timeout — the resilience logic previously in
/// <c>ResilientToolExecutor</c>.
/// </summary>
public sealed class InProcessVirtualTerminal : IVirtualTerminal
{
    private readonly IToolRegistry _registry;
    private readonly ToolsConfig _config;
    private readonly ConcurrentDictionary<string, CircuitBreaker> _breakers = new();

    public InProcessVirtualTerminal(IToolRegistry registry, ToolsConfig config)
    {
        _registry = registry;
        _config = config;
    }

    public async Task<ToolResult> RunAsync(string toolName, string jsonArgs, CancellationToken ct = default)
    {
        if (!_registry.TryGetRegistration(toolName, out var registration))
            return new ToolResult(-1, $"Error: unknown tool '{toolName}'", 0);

        var breaker = _breakers.GetOrAdd(toolName,
            _ => new CircuitBreaker(failureThreshold: 5, resetTimeoutSeconds: 60, halfOpenAttempts: 3));

        if (breaker.IsOpen)
            return new ToolResult(-1, $"Error: circuit breaker open for '{toolName}'", 0);

        Exception? last = null;
        var maxRetries = _config.MaxRetries;
        var maxDelaySeconds = _config.MaxRetryDelaySeconds;
        var timeoutSeconds = _config.DefaultTimeoutSeconds;

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                var result = await InvokeOnceAsync(registration, jsonArgs, linked.Token);

                if (result.IsSuccess) breaker.RecordSuccess();
                else breaker.RecordFailure();

                return result;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                breaker.RecordFailure();
                last = new TimeoutException($"Tool '{toolName}' timed out after {timeoutSeconds}s");
            }
            catch (Exception ex)
            {
                breaker.RecordFailure();
                last = ex;
                if (!IsTransient(ex) || attempt >= maxRetries) throw;
            }

            if (attempt < maxRetries)
            {
                var delay = Math.Min(Math.Pow(2, attempt) * 2, maxDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(delay), ct);
            }
        }

        return new ToolResult(-1, $"Error: {last?.Message ?? "unknown"}", 0);
    }

    private static async Task<ToolResult> InvokeOnceAsync(
        ToolRegistration registration, string jsonArgs, CancellationToken ct)
    {
        // Create a capturing console that writes to a StringWriter; no ANSI so output is plain text.
        var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });

        var args = ToolSchemaGenerator.JsonToArgs(registration.SettingsType, jsonArgs);
        var app = registration.AppFactory(console);

        var sw = Stopwatch.StartNew();
        int exitCode;
        try
        {
            exitCode = await app.RunAsync(args);
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ToolResult(-1, $"Error: {ex.Message}", sw.ElapsedMilliseconds);
        }

        sw.Stop();
        return new ToolResult(exitCode, writer.ToString().TrimEnd(), sw.ElapsedMilliseconds);
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
/// Per-tool circuit breaker: opens after <paramref name="failureThreshold"/> consecutive
/// failures; enters half-open after <paramref name="resetTimeoutSeconds"/>; closes after
/// <paramref name="halfOpenAttempts"/> consecutive successes.
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

    public CircuitBreaker(int failureThreshold, int resetTimeoutSeconds, int halfOpenAttempts)
    {
        _failureThreshold = failureThreshold;
        _resetTimeout = TimeSpan.FromSeconds(resetTimeoutSeconds);
        _halfOpenAttempts = halfOpenAttempts;
    }

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

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_state == State.HalfOpen)
            {
                if (++_halfOpenSuccessCount >= _halfOpenAttempts)
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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.Models;
using Crypton.Api.ExecutionService.Strategy.Conditions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Strategy;

/// <summary>
/// Current state of the active strategy.
/// </summary>
public enum StrategyState
{
    None,        // No strategy loaded yet
    Active,      // Strategy is valid and within the validity window
    Expired,     // Validity window has passed; no new entries
    Invalid      // Last load attempt failed validation
}

/// <summary>
/// Manages the active strategy: file watching, hot-reload, validation, and validity window enforcement.
/// Implements ES-SM-001 and ES-SM-004.
/// </summary>
public sealed class StrategyService : IHostedService, IDisposable, IStrategyService
{
    private readonly StrategyConfig _config;
    private readonly StrategyValidator _validator;
    private readonly ConditionParser _conditionParser;
    private readonly IEventLogger _eventLogger;
    private readonly ILogger<StrategyService> _logger;

    private readonly Lock _lock = new();
    private FileSystemWatcher? _watcher;
    private StrategyDocument? _activeStrategy;
    private StrategyState _state = StrategyState.None;
    private string? _activeStrategyId;
    private CancellationTokenSource? _cts;
    private Task? _validityMonitorTask;

    // Callbacks for strategy changes
    public event Func<StrategyDocument, Task>? OnStrategyLoaded;
    public event Func<StrategyState, Task>? OnStateChanged;

    public StrategyDocument? ActiveStrategy { get { lock (_lock) { return _activeStrategy; } } }
    public StrategyState State { get { lock (_lock) { return _state; } } }
    public string? ActiveStrategyId { get { lock (_lock) { return _activeStrategyId; } } }

    public StrategyService(
        IOptions<ExecutionServiceConfig> config,
        StrategyValidator validator,
        ConditionParser conditionParser,
        IEventLogger eventLogger,
        ILogger<StrategyService> logger)
    {
        _config = config.Value.Strategy;
        _validator = validator;
        _conditionParser = conditionParser;
        _eventLogger = eventLogger;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Try to load the strategy file on startup
        var path = _config.WatchPath;
        if (File.Exists(path))
            _ = Task.Run(() => TryLoadStrategyAsync(path, _cts.Token), _cts.Token);

        // Set up FileSystemWatcher
        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var file = Path.GetFileName(path);

        _watcher = new FileSystemWatcher(dir, file)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, e) => OnFileChanged(e.FullPath);
        _watcher.Created += (_, e) => OnFileChanged(e.FullPath);

        // Validity window monitor
        _validityMonitorTask = Task.Run(() => MonitorValidityWindowAsync(_cts.Token), _cts.Token);

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _watcher?.Dispose();
        _cts?.Cancel();
        if (_validityMonitorTask is not null)
        {
            try
            {
                await _validityMonitorTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException) { /* monitor did not stop in time, continue */ }
            catch (OperationCanceledException) { /* expected on shutdown */ }
        }
    }

    private void OnFileChanged(string path)
    {
        if (_cts is null) return;
        var token = _cts.Token;
        _ = Task.Run(async () =>
        {
            // Short debounce to avoid partial-write reads
            await Task.Delay(_config.ReloadLatencyMs, token);
            if (!token.IsCancellationRequested)
                await TryLoadStrategyAsync(path, token);
        }, token);
    }

    private async Task TryLoadStrategyAsync(string path, CancellationToken token)
    {
        string? raw = null;
        try
        {
            // Retry read a few times in case the file is still being written
            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    raw = await File.ReadAllTextAsync(path, token);
                    break;
                }
                catch (IOException) when (attempt < 2)
                {
                    await Task.Delay(500, token);
                }
            }

            if (raw is null)
                raw = await File.ReadAllTextAsync(path, token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read strategy file at {Path}", path);
            return;
        }

        StrategyDocument strategy;
        try
        {
            strategy = JsonSerializer.Deserialize<StrategyDocument>(raw,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Deserialized to null.");
        }
        catch (Exception ex)
        {
            await LogRejectionAsync(path, $"JSON parse error: {ex.Message}", token);
            return;
        }

        var errors = _validator.Validate(strategy);
        if (errors.Count > 0)
        {
            var errorSummary = string.Join("; ", errors.Select(e => $"{e.Field}: {e.Message}"));
            await LogRejectionAsync(path, errorSummary, token);
            return;
        }

        // Pre-compile all conditions
        foreach (var pos in strategy.Positions)
        {
            if (!string.IsNullOrWhiteSpace(pos.EntryCondition))
            {
                try { _conditionParser.Parse(pos.EntryCondition); }
                catch (ConditionParseException ex)
                {
                    await LogRejectionAsync(path, $"Position '{pos.Id}' entry_condition parse error: {ex.Message}", token);
                    return;
                }
            }
            if (!string.IsNullOrWhiteSpace(pos.InvalidationCondition))
            {
                try { _conditionParser.Parse(pos.InvalidationCondition); }
                catch (ConditionParseException ex)
                {
                    await LogRejectionAsync(path, $"Position '{pos.Id}' invalidation_condition parse error: {ex.Message}", token);
                    return;
                }
            }
        }

        // Compute strategy ID
        strategy.Id = ComputeId(raw);

        string? previousId;
        lock (_lock)
        {
            previousId = _activeStrategyId;
            _activeStrategy = strategy;
            _activeStrategyId = strategy.Id;
            _state = strategy.ValidityWindow > DateTimeOffset.UtcNow
                ? StrategyState.Active
                : StrategyState.Expired;
        }

        var eventType = previousId is null ? EventTypes.StrategyLoaded : EventTypes.StrategySwapped;
        await _eventLogger.LogAsync(eventType, "active", new Dictionary<string, object?>
        {
            ["strategy_id"] = strategy.Id,
            ["previous_id"] = previousId,
            ["posture"] = strategy.Posture,
            ["validity_window"] = strategy.ValidityWindow.ToString("O")
        }, token);

        if (OnStrategyLoaded is not null) await OnStrategyLoaded(strategy);
    }

    private async Task LogRejectionAsync(string path, string reason, CancellationToken token)
    {
        _logger.LogWarning("Strategy file at {Path} rejected: {Reason}", path, reason);
        await _eventLogger.LogAsync(EventTypes.StrategyRejected, CurrentModeString(), new Dictionary<string, object?>
        {
            ["path"] = path,
            ["reason"] = reason
        }, token);
    }

    private async Task MonitorValidityWindowAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_config.ValidityCheckIntervalMs), token);

                StrategyDocument? s;
                StrategyState before;
                lock (_lock)
                {
                    s = _activeStrategy;
                    before = _state;
                }

                if (s is null || before != StrategyState.Active) continue;
                if (s.ValidityWindow > DateTimeOffset.UtcNow) continue;

                lock (_lock) { _state = StrategyState.Expired; }

                await _eventLogger.LogAsync(EventTypes.StrategyExpired, "expired", new Dictionary<string, object?>
                {
                    ["strategy_id"] = s.Id,
                    ["validity_window"] = s.ValidityWindow.ToString("O")
                }, token);

                if (OnStateChanged is not null) await OnStateChanged(StrategyState.Expired);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Error in validity monitor"); }
        }
    }

    private string CurrentModeString()
    {
        lock (_lock)
        {
            return _state switch
            {
                StrategyState.Active => "active",
                StrategyState.Expired => "expired",
                _ => "none"
            };
        }
    }

    private static string ComputeId(string raw)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant()[..16];
    }

    /// <summary>
    /// Immediately re-reads the strategy file from disk (bypasses the reload debounce).
    /// </summary>
    public Task ForceReloadAsync(CancellationToken ct = default)
    {
        var path = _config.WatchPath;
        return File.Exists(path) ? TryLoadStrategyAsync(path, ct) : Task.CompletedTask;
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _cts?.Dispose();
    }
}

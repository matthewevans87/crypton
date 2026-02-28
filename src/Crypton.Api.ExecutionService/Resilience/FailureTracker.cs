using System.Text.Json;
using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Resilience;

/// <summary>
/// Tracks consecutive order placement failures and triggers safe mode when the configured
/// threshold is reached. Persists state atomically to disk so a restart that finds
/// count &gt;= threshold enters safe mode without needing new failures.
/// Thread-safe.
/// </summary>
public sealed class FailureTracker
{
    private readonly SafetyConfig _config;
    private readonly string _persistPath;
    private readonly ILogger<FailureTracker> _logger;
    private readonly Lock _lock = new();
    private int _consecutiveFailures;
    private bool _safeModeTriggered;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true
    };

    /// <summary>
    /// Raised (once) when consecutive failures first reach the configured threshold.
    /// Subscribers should activate safe mode.
    /// </summary>
    public event Func<string, Task>? OnSafeModeTriggered;

    public int ConsecutiveFailures { get { lock (_lock) { return _consecutiveFailures; } } }
    public bool SafeModeTriggered { get { lock (_lock) { return _safeModeTriggered; } } }

    public FailureTracker(IOptions<ExecutionServiceConfig> config, ILogger<FailureTracker> logger)
    {
        _config = config.Value.Safety;
        _logger = logger;
        _persistPath = Path.Combine(_config.ResilienceStatePath, "failure_count.json");
        Load();
    }

    /// <summary>Record a failure. Fires <see cref="OnSafeModeTriggered"/> if threshold is reached.</summary>
    public void RecordFailure()
    {
        bool triggerSafeMode;
        lock (_lock)
        {
            _consecutiveFailures++;
            Persist();
            triggerSafeMode = !_safeModeTriggered && _consecutiveFailures >= _config.ConsecutiveFailureThreshold;
            if (triggerSafeMode)
                _safeModeTriggered = true;
        }

        if (triggerSafeMode)
        {
            _logger.LogWarning(
                "Consecutive failure threshold of {Threshold} reached. Triggering safe mode.",
                _config.ConsecutiveFailureThreshold);
            _ = OnSafeModeTriggered?.Invoke("consecutive_failures");
        }
    }

    /// <summary>Record a successful order. Resets the consecutive failure counter.</summary>
    public void RecordSuccess()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            Persist();
        }
    }

    /// <summary>Reset the counter and clear the safe mode triggered flag (called on safe mode deactivation).</summary>
    public void Reset()
    {
        lock (_lock)
        {
            _consecutiveFailures = 0;
            _safeModeTriggered = false;
            Persist();
        }
    }

    private void Load()
    {
        if (!File.Exists(_persistPath)) return;
        try
        {
            var json = File.ReadAllText(_persistPath);
            var state = JsonSerializer.Deserialize<FailureState>(json, JsonOpts);
            if (state is not null)
            {
                _consecutiveFailures = state.ConsecutiveFailures;
                if (_consecutiveFailures >= _config.ConsecutiveFailureThreshold)
                    _safeModeTriggered = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load failure tracker state from {Path}", _persistPath);
        }
    }

    private void Persist()
    {
        try
        {
            var dir = Path.GetDirectoryName(_persistPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var state = new FailureState
            {
                ConsecutiveFailures = _consecutiveFailures,
                LastFailureUtc = DateTimeOffset.UtcNow
            };
            var tmp = _persistPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(state, JsonOpts));
            File.Move(tmp, _persistPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist failure tracker state to {Path}", _persistPath);
        }
    }

    private sealed class FailureState
    {
        [JsonPropertyName("consecutive_failures")]
        public int ConsecutiveFailures { get; set; }

        [JsonPropertyName("last_failure_utc")]
        public DateTimeOffset LastFailureUtc { get; set; }
    }
}

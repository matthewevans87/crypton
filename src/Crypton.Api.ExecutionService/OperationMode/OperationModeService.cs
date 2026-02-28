using System.Text.Json;
using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Logging;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.OperationMode;

/// <summary>
/// Manages the persisted operation mode (paper vs live).
/// State file: {ResilienceStatePath}/operation_mode.json
/// Default mode: "paper"
/// Thread-safe; persists atomically on every transition.
/// </summary>
public sealed class OperationModeService : IOperationModeService
{
    private readonly string _stateFilePath;
    private readonly IEventLogger _eventLogger;
    private readonly Lock _lock = new();
    private string _currentMode = "paper";

    public string CurrentMode
    {
        get { lock (_lock) { return _currentMode; } }
        private set { lock (_lock) { _currentMode = value; } }
    }

    public OperationModeService(IOptions<ExecutionServiceConfig> config, IEventLogger eventLogger)
    {
        var basePath = config.Value.Safety.ResilienceStatePath;
        _stateFilePath = Path.Combine(basePath, "operation_mode.json");
        _eventLogger = eventLogger;
    }

    // Internal constructor for tests that supply paths directly.
    internal OperationModeService(string stateFilePath, IEventLogger eventLogger)
    {
        _stateFilePath = stateFilePath;
        _eventLogger = eventLogger;
    }

    /// <summary>
    /// Reads the state file and initialises <see cref="CurrentMode"/>.
    /// Falls back to "paper" when the file does not exist.
    /// </summary>
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_stateFilePath))
        {
            CurrentMode = "paper";
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, ct);
            var state = JsonSerializer.Deserialize<OperationModeState>(json);
            CurrentMode = state?.Mode ?? "paper";
        }
        catch
        {
            CurrentMode = "paper";
        }
    }

    /// <summary>
    /// Transitions to "live" mode, persists the change, and emits a mode_changed event.
    /// </summary>
    public async Task PromoteToLiveAsync(string operatorNote = "", CancellationToken ct = default)
    {
        string previous;
        lock (_lock)
        {
            previous = _currentMode;
            _currentMode = "live";
        }

        await PersistAsync("live", ct);
        await _eventLogger.LogAsync(EventTypes.ModeChanged, "live", new Dictionary<string, object?>
        {
            ["new_mode"] = "live",
            ["previous_mode"] = previous,
            ["operator_note"] = operatorNote
        }, ct);
    }

    /// <summary>
    /// Transitions to "paper" mode, persists the change, and emits a mode_changed event.
    /// </summary>
    public async Task DemoteToPaperAsync(string operatorNote = "", CancellationToken ct = default)
    {
        string previous;
        lock (_lock)
        {
            previous = _currentMode;
            _currentMode = "paper";
        }

        await PersistAsync("paper", ct);
        await _eventLogger.LogAsync(EventTypes.ModeChanged, "paper", new Dictionary<string, object?>
        {
            ["new_mode"] = "paper",
            ["previous_mode"] = previous,
            ["operator_note"] = operatorNote
        }, ct);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task PersistAsync(string mode, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_stateFilePath)!;
        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var state = new OperationModeState
        {
            Mode = mode,
            ChangedAt = DateTimeOffset.UtcNow,
            ChangedBy = "operator"
        };

        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });

        // Write atomically via a uniquely-named temp file (safe for concurrent callers).
        var tmp = _stateFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, _stateFilePath, overwrite: true);
    }

    // -------------------------------------------------------------------------
    // Private DTO
    // -------------------------------------------------------------------------

    private sealed class OperationModeState
    {
        [JsonPropertyName("mode")]
        public string Mode { get; init; } = "paper";

        [JsonPropertyName("changed_at")]
        public DateTimeOffset ChangedAt { get; init; }

        [JsonPropertyName("changed_by")]
        public string ChangedBy { get; init; } = "operator";
    }
}

using System.Text.Json;
using AgentRunner.StateMachine;

namespace AgentRunner.StateMachine;

public class StatePersistence
{
    private readonly string _stateFilePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public StatePersistence(string stateFilePath)
    {
        _stateFilePath = stateFilePath;
        var directory = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    public async Task SaveStateAsync(LoopState currentState, CycleContext? cycleContext)
    {
        var state = new PersistedState
        {
            CurrentState = currentState,
            CycleContext = cycleContext,
            LastUpdated = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(state, JsonOptions);
        await File.WriteAllTextAsync(_stateFilePath, json);
    }

    public async Task<(LoopState State, CycleContext? Context)?> LoadStateAsync()
    {
        if (!File.Exists(_stateFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
            return (state?.CurrentState ?? LoopState.Idle, state?.CycleContext);
        }
        catch
        {
            return null;
        }
    }

    public async Task ClearStateAsync()
    {
        if (File.Exists(_stateFilePath))
        {
            await Task.Run(() => File.Delete(_stateFilePath));
        }
    }

    private class PersistedState
    {
        public LoopState CurrentState { get; set; }
        public CycleContext? CycleContext { get; set; }
        public DateTime LastUpdated { get; set; }
    }
}

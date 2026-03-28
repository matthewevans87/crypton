using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRunner.Abstractions;
using AgentRunner.Domain;

namespace AgentRunner.Infrastructure;

/// <summary>Persists loop state + cycle context to a JSON file so the loop can survive restarts.</summary>
public sealed class JsonStatePersistence : IStatePersistence
{
    private readonly string _stateFilePath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonStatePersistence(string stateFilePath)
    {
        _stateFilePath = stateFilePath;
        var dir = Path.GetDirectoryName(stateFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public async Task<(LoopState State, CycleContext? Cycle)?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_stateFilePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, ct);
            var persisted = JsonSerializer.Deserialize<PersistedState>(json, JsonOptions);
            if (persisted is null)
                return null;
            return (persisted.CurrentState, persisted.Cycle);
        }
        catch
        {
            return null;
        }
    }

    public async Task SaveAsync(LoopState state, CycleContext? cycle, CancellationToken ct = default)
    {
        var persisted = new PersistedState(state, cycle, DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
        var tmp = _stateFilePath + ".tmp";
        await File.WriteAllTextAsync(tmp, json, ct);
        File.Move(tmp, _stateFilePath, overwrite: true);
    }

    public async Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_stateFilePath))
            await Task.Run(() => File.Delete(_stateFilePath), ct);
    }

    private sealed record PersistedState(
        LoopState CurrentState,
        CycleContext? Cycle,
        DateTimeOffset LastUpdated);
}

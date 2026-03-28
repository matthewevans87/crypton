using System.Text.Json;
using AgentRunner.Abstractions;
using AgentRunner.Configuration;
using AgentRunner.Domain;

namespace AgentRunner.Infrastructure;

/// <summary>
/// File-system artifact store. Each cycle gets a timestamped subdirectory under
/// <c>artifacts/cycles/</c>. Agent memory files live under <c>artifacts/memory/</c>.
/// </summary>
public sealed class FileSystemArtifactStore : IArtifactStore
{
    private readonly string _basePath;
    private readonly string _cyclesPath;
    private readonly string _memoryPath;
    private readonly int _archiveRetentionCount;

    public FileSystemArtifactStore(StorageConfig config)
    {
        _basePath = Path.GetFullPath(config.BasePath);
        _cyclesPath = Path.Combine(_basePath, config.CyclesPath);
        _memoryPath = Path.Combine(_basePath, config.MemoryPath);
        _archiveRetentionCount = config.ArchiveRetentionCount;
        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(_basePath);
        Directory.CreateDirectory(_cyclesPath);
        Directory.CreateDirectory(_memoryPath);
        Directory.CreateDirectory(Path.Combine(_cyclesPath, "history"));
    }

    public string CreateCycleDirectory()
    {
        var cycleId = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(GetCycleDir(cycleId));
        return cycleId;
    }

    public void Save(string cycleId, string name, string content)
    {
        File.WriteAllText(GetArtifactPath(cycleId, name), content);
    }

    public string? Read(string cycleId, string name)
    {
        var path = GetArtifactPath(cycleId, name);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public bool Exists(string cycleId, string name) =>
        File.Exists(GetArtifactPath(cycleId, name));

    public string? ReadMemory(string agentName)
    {
        var path = Path.Combine(_memoryPath, $"{agentName.ToLowerInvariant()}_memory.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public string? ReadSharedMemory()
    {
        var path = Path.Combine(_memoryPath, "shared_memory.md");
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public IReadOnlyList<string> GetRecentEvaluations(int count)
    {
        if (!Directory.Exists(_cyclesPath))
            return [];

        return Directory.GetDirectories(_cyclesPath)
            .Where(d => !d.EndsWith("history", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.GetFileName(d)!)
            .OrderByDescending(id => id)
            .Select(cycleId => Read(cycleId, "evaluation.md"))
            .Where(c => c != null)
            .Cast<string>()
            .Take(count)
            .ToList();
    }

    public string? GetLatestCompletedCycleId()
    {
        if (!Directory.Exists(_cyclesPath))
            return null;

        return Directory.GetDirectories(_cyclesPath)
            .Where(d => !d.EndsWith("history", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.GetFileName(d)!)
            .OrderByDescending(id => id)
            .FirstOrDefault(cycleId => Exists(cycleId, "strategy.json"));
    }

    public IReadOnlyList<string> GetRecentCycleIds(int count)
    {
        if (!Directory.Exists(_cyclesPath))
            return [];

        return Directory.GetDirectories(_cyclesPath)
            .Where(d => !d.EndsWith("history", StringComparison.OrdinalIgnoreCase))
            .Select(d => Path.GetFileName(d)!)
            .OrderByDescending(id => id)
            .Take(count)
            .ToList();
    }

    public IReadOnlyList<string> GetCycleArtifactNames(string cycleId)
    {
        var dir = GetCycleDir(cycleId);
        if (!Directory.Exists(dir))
            return [];
        return Directory.GetFiles(dir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .ToList();
    }

    public void ArchiveOldCycles()
    {
        if (!Directory.Exists(_cyclesPath))
            return;

        var historyDir = Path.Combine(_cyclesPath, "history");
        Directory.CreateDirectory(historyDir);

        var cycles = Directory.GetDirectories(_cyclesPath)
            .Where(d => !d.EndsWith("history", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d)
            .ToList();

        foreach (var dir in cycles.Skip(_archiveRetentionCount))
        {
            var dest = Path.Combine(historyDir, Path.GetFileName(dir));
            if (!Directory.Exists(dest))
                Directory.Move(dir, dest);
            else
                Directory.Delete(dir, recursive: true);
        }
    }

    private string GetCycleDir(string cycleId) => Path.Combine(_cyclesPath, cycleId);
    private string GetArtifactPath(string cycleId, string name) => Path.Combine(GetCycleDir(cycleId), name);
}

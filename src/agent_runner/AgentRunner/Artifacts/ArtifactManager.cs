using AgentRunner.Configuration;
using System.IO.Compression;

namespace AgentRunner.Artifacts;

public class ArtifactManager
{
    private readonly string _basePath;
    private readonly string _cyclesPath;
    private readonly string _memoryPath;
    private readonly int _archiveRetentionCount;

    public ArtifactManager(StorageConfig config)
    {
        _basePath = config.BasePath;
        _cyclesPath = config.CyclesPath;
        _memoryPath = config.MemoryPath;
        _archiveRetentionCount = config.ArchiveRetentionCount;

        EnsureDirectoriesExist();
    }

    private void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(GetBaseDirectory());
        Directory.CreateDirectory(GetCyclesDirectory());
        Directory.CreateDirectory(GetMemoryDirectory());
        Directory.CreateDirectory(GetHistoryDirectory());
    }

    public string GetBaseDirectory() => Path.GetFullPath(_basePath);
    public string GetCyclesDirectory() => Path.Combine(GetBaseDirectory(), _cyclesPath);
    public string GetMemoryDirectory() => Path.Combine(GetBaseDirectory(), _memoryPath);
    public string GetHistoryDirectory() => Path.Combine(GetCyclesDirectory(), "history");

    public string CreateCycleDirectory()
    {
        var cycleId = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var cycleDir = Path.Combine(GetCyclesDirectory(), cycleId);
        Directory.CreateDirectory(cycleDir);
        return cycleId;
    }

    public string GetCycleDirectory(string cycleId)
    {
        return Path.Combine(GetCyclesDirectory(), cycleId);
    }

    public string GetArtifactPath(string cycleId, string artifactName)
    {
        return Path.Combine(GetCycleDirectory(cycleId), artifactName);
    }

    public void SaveArtifact(string cycleId, string artifactName, string content)
    {
        var path = GetArtifactPath(cycleId, artifactName);
        File.WriteAllText(path, content);
    }

    public string? ReadArtifact(string cycleId, string artifactName)
    {
        var path = GetArtifactPath(cycleId, artifactName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public bool ArtifactExists(string cycleId, string artifactName)
    {
        var path = GetArtifactPath(cycleId, artifactName);
        return File.Exists(path);
    }

    public List<string> GetRecentCycles(int count = 10)
    {
        var cyclesDir = GetCyclesDirectory();
        if (!Directory.Exists(cyclesDir))
            return new List<string>();

        return Directory.GetDirectories(cyclesDir)
            .Where(d => !d.EndsWith("history"))
            .Select(d => Path.GetFileName(d))
            .OrderByDescending(d => d)
            .Take(count)
            .ToList();
    }

    public void ArchiveOldCycles()
    {
        var cycles = GetRecentCycles(int.MaxValue);
        var toArchive = cycles.Skip(_archiveRetentionCount).ToList();

        foreach (var cycleId in toArchive)
        {
            var cycleDir = GetCycleDirectory(cycleId);
            var historyDir = GetHistoryDirectory();
            var destDir = Path.Combine(historyDir, cycleId);
            
            if (Directory.Exists(cycleDir) && !Directory.Exists(destDir))
            {
                Directory.Move(cycleDir, destDir);
            }
        }
        
        CompressOldCycles();
    }

    public void CompressOldCycles()
    {
        var historyDir = GetHistoryDirectory();
        if (!Directory.Exists(historyDir))
            return;
        
        var cycleDirs = Directory.GetDirectories(historyDir);
        
        foreach (var cycleDir in cycleDirs)
        {
            var cycleId = Path.GetFileName(cycleDir);
            var zipPath = Path.Combine(historyDir, $"{cycleId}.zip");
            
            if (File.Exists(zipPath) || Directory.GetFiles(cycleDir).Length == 0)
                continue;
            
            try
            {
                if (File.Exists(zipPath.Replace(".zip", "")))
                    continue;
                    
                ZipFile.CreateFromDirectory(cycleDir, zipPath, CompressionLevel.Optimal, false);
                
                // Remove original directory after successful compression
                Directory.Delete(cycleDir, true);
            }
            catch
            {
                // Skip if compression fails
            }
        }
    }

    public void ExtractCycle(string cycleId)
    {
        var historyDir = GetHistoryDirectory();
        var zipPath = Path.Combine(historyDir, $"{cycleId}.zip");
        var extractDir = GetCycleDirectory(cycleId);
        
        if (File.Exists(zipPath) && !Directory.Exists(extractDir))
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
        }
    }

    public string GetMemoryPath(string agentName)
    {
        var dir = Path.Combine(GetMemoryDirectory(), agentName);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "memory.md");
    }

    public void AppendToMemory(string agentName, string content)
    {
        var path = GetMemoryPath(agentName);
        var existing = File.Exists(path) ? File.ReadAllText(path) : "";
        var separator = string.IsNullOrEmpty(existing) ? "" : "\n\n---\n\n";
        File.WriteAllText(path, existing + separator + content);
    }

    public string? ReadMemory(string agentName)
    {
        var path = GetMemoryPath(agentName);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public List<string> GetRecentEvaluations(int count = 3)
    {
        var cycles = GetRecentCycles(count * 2);
        var evaluations = new List<string>();

        foreach (var cycleId in cycles)
        {
            var evalPath = GetArtifactPath(cycleId, "evaluation.md");
            if (File.Exists(evalPath))
            {
                evaluations.Add(File.ReadAllText(evalPath));
                if (evaluations.Count >= count)
                    break;
            }
        }

        return evaluations;
    }

    public List<MemorySearchResult> SearchMemory(string agentName, string query, int maxResults = 10)
    {
        var results = new List<MemorySearchResult>();
        var path = GetMemoryPath(agentName);
        
        if (!File.Exists(path))
            return results;

        var content = File.ReadAllText(path);
        var lines = content.Split('\n');
        
        var currentEntry = new System.Text.StringBuilder();
        var currentEntryStart = 0;
        
        for (int i = 0; i < lines.Length; i++)
        {
            currentEntry.AppendLine(lines[i]);
            
            if (lines[i].TrimStart().StartsWith("---") || i == lines.Length - 1)
            {
                var entryText = currentEntry.ToString();
                if (entryText.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(new MemorySearchResult
                    {
                        AgentName = agentName,
                        Content = entryText.Trim(),
                        MatchedQuery = query,
                        Timestamp = GetMemoryEntryTimestamp(entryText, currentEntryStart)
                    });
                }
                currentEntry.Clear();
                currentEntryStart = i + 1;
            }
        }
        
        return results.Take(maxResults).ToList();
    }

    public List<MemorySearchResult> SearchAllMemory(string query, int maxResults = 10)
    {
        var allResults = new List<MemorySearchResult>();
        var agentNames = new[] { "plan", "research", "analysis", "evaluation" };
        
        foreach (var agent in agentNames)
        {
            var results = SearchMemory(agent, query, maxResults);
            allResults.AddRange(results);
        }
        
        return allResults.Take(maxResults).ToList();
    }

    private DateTime GetMemoryEntryTimestamp(string entry, int lineNumber)
    {
        var timestampPattern = new System.Text.RegularExpressions.Regex(
            @"(\d{4}-\d{2}-\d{2}|\d{2}:\d{2}:\d{2})",
            System.Text.RegularExpressions.RegexOptions.RightToLeft);
        
        var match = timestampPattern.Match(entry);
        if (match.Success && DateTime.TryParse(match.Value, out var timestamp))
        {
            return timestamp;
        }
        
        return DateTime.UtcNow;
    }
}

public class MemorySearchResult
{
    public string AgentName { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string MatchedQuery { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

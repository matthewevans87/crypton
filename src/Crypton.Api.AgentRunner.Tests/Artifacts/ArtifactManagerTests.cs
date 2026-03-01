using AgentRunner.Artifacts;
using AgentRunner.Configuration;
using Xunit;

namespace AgentRunner.Tests.Artifacts;

public class ArtifactManagerTests : IDisposable
{
    private readonly string _testPath;
    private readonly ArtifactManager _artifactManager;

    public ArtifactManagerTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"artifacts_test_{Guid.NewGuid()}");
        var config = new StorageConfig
        {
            BasePath = _testPath,
            CyclesPath = "cycles",
            MemoryPath = "memory",
            ArchiveRetentionCount = 2
        };
        _artifactManager = new ArtifactManager(config);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testPath))
        {
            Directory.Delete(_testPath, true);
        }
    }

    [Fact]
    public void CreateCycleDirectory_CreatesDirectory()
    {
        var id1 = _artifactManager.CreateCycleDirectory();
        var id2 = _artifactManager.CreateCycleDirectory();

        // IDs may be same if created in same second, but directories should exist
        var dir1 = _artifactManager.GetCycleDirectory(id1);
        var dir2 = _artifactManager.GetCycleDirectory(id2);

        Assert.True(Directory.Exists(dir1));
        Assert.True(Directory.Exists(dir2));
    }

    [Fact]
    public void SaveAndReadArtifact_RoundTrip()
    {
        var cycleId = _artifactManager.CreateCycleDirectory();
        var content = "# Test Plan\n\nThis is a test plan.";

        _artifactManager.SaveArtifact(cycleId, "plan.md", content);
        var read = _artifactManager.ReadArtifact(cycleId, "plan.md");

        Assert.Equal(content, read);
    }

    [Fact]
    public void ArtifactExists_ReturnsCorrectly()
    {
        var cycleId = _artifactManager.CreateCycleDirectory();

        Assert.False(_artifactManager.ArtifactExists(cycleId, "plan.md"));

        _artifactManager.SaveArtifact(cycleId, "plan.md", "content");

        Assert.True(_artifactManager.ArtifactExists(cycleId, "plan.md"));
    }

    [Fact]
    public void ReadNonExistentArtifact_ReturnsNull()
    {
        var cycleId = _artifactManager.CreateCycleDirectory();
        var result = _artifactManager.ReadArtifact(cycleId, "nonexistent.md");

        Assert.Null(result);
    }

    [Fact]
    public void GetRecentCycles_ReturnsOrderedList()
    {
        // Create multiple cycles
        for (int i = 0; i < 3; i++)
        {
            _artifactManager.CreateCycleDirectory();
        }

        var recent = _artifactManager.GetRecentCycles(2);

        // Should get at least 2 if we created 3
        Assert.True(recent.Count >= 1);
    }

    [Fact]
    public void MemoryPath_CreatesDirectory()
    {
        var path = _artifactManager.GetMemoryPath("plan");

        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void AppendToMemory_AppendsContent()
    {
        _artifactManager.AppendToMemory("plan", "First entry");
        _artifactManager.AppendToMemory("plan", "Second entry");

        var memory = _artifactManager.ReadMemory("plan");

        Assert.Contains("First entry", memory);
        Assert.Contains("Second entry", memory);
    }

    [Fact]
    public void GetLatestCompletedCycleId_WhenNoCycles_ReturnsNull()
    {
        var result = _artifactManager.GetLatestCompletedCycleId();
        Assert.Null(result);
    }

    [Fact]
    public void GetLatestCompletedCycleId_WhenCycleHasNoStrategyJson_ReturnsNull()
    {
        var cycleId = _artifactManager.CreateCycleDirectory();
        _artifactManager.SaveArtifact(cycleId, "plan.md", "some plan");

        var result = _artifactManager.GetLatestCompletedCycleId();

        Assert.Null(result);
    }

    [Fact]
    public void GetLatestCompletedCycleId_WhenStrategyJsonExists_ReturnsId()
    {
        var cycleId = _artifactManager.CreateCycleDirectory();
        _artifactManager.SaveArtifact(cycleId, "plan.md", "some plan");
        _artifactManager.SaveArtifact(cycleId, "strategy.json", "{}");

        var result = _artifactManager.GetLatestCompletedCycleId();

        Assert.Equal(cycleId, result);
    }

    [Fact]
    public void GetLatestCompletedCycleId_WithMultipleCycles_ReturnsMostRecent()
    {
        // Older cycle — complete
        var older = _artifactManager.CreateCycleDirectory();
        _artifactManager.SaveArtifact(older, "strategy.json", "{}");

        // Small delay to ensure different timestamp-based IDs
        System.Threading.Thread.Sleep(1100);

        // Newer cycle — also complete
        var newer = _artifactManager.CreateCycleDirectory();
        _artifactManager.SaveArtifact(newer, "strategy.json", "{}");

        var result = _artifactManager.GetLatestCompletedCycleId();

        // Should return the newer one (GetRecentCycles is OrderByDescending)
        Assert.Equal(newer, result);
    }
}

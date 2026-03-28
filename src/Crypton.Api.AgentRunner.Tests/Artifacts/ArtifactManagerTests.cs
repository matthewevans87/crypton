using AgentRunner.Configuration;
using AgentRunner.Infrastructure;
using Xunit;

namespace AgentRunner.Tests.Artifacts;

public class FileSystemArtifactStoreTests : IDisposable
{
    private readonly string _testPath;
    private readonly FileSystemArtifactStore _store;

    public FileSystemArtifactStoreTests()
    {
        _testPath = Path.Combine(Path.GetTempPath(), $"artifacts_test_{Guid.NewGuid()}");
        _store = new FileSystemArtifactStore(new StorageConfig
        {
            BasePath = _testPath,
            CyclesPath = "cycles",
            MemoryPath = "memory",
            ArchiveRetentionCount = 2
        });
    }

    public void Dispose()
    {
        if (Directory.Exists(_testPath)) Directory.Delete(_testPath, true);
    }

    [Fact]
    public void CreateCycleDirectory_ReturnsNonEmptyId()
    {
        var cycleId = _store.CreateCycleDirectory();
        Assert.False(string.IsNullOrEmpty(cycleId));
    }

    [Fact]
    public void Save_And_Read_RoundTrip()
    {
        var cycleId = _store.CreateCycleDirectory();
        _store.Save(cycleId, "plan.md", "# My Plan");
        Assert.Equal("# My Plan", _store.Read(cycleId, "plan.md"));
    }

    [Fact]
    public void Read_NonExistentArtifact_ReturnsNull()
    {
        var cycleId = _store.CreateCycleDirectory();
        Assert.Null(_store.Read(cycleId, "nonexistent.md"));
    }

    [Fact]
    public void GetLatestCompletedCycleId_NoArchive_ReturnsNull()
    {
        Assert.Null(_store.GetLatestCompletedCycleId());
    }

    [Fact]
    public void ReadMemory_NonExistent_ReturnsNull()
    {
        Assert.Null(_store.ReadMemory("missing"));
    }

    [Fact]
    public void ReadSharedMemory_NonExistent_ReturnsNull()
    {
        Assert.Null(_store.ReadSharedMemory());
    }

    [Fact]
    public void GetRecentCycleIds_ReturnsEmpty_WhenNoHistory()
    {
        var ids = _store.GetRecentCycleIds(5);
        Assert.Empty(ids);
    }

    [Fact]
    public void GetCycleArtifactNames_UnknownCycle_ReturnsEmpty()
    {
        var names = _store.GetCycleArtifactNames("nonexistent_cycle");
        Assert.Empty(names);
    }
}

using Crypton.Api.ExecutionService.Logging;
using Crypton.Api.ExecutionService.OperationMode;
using FluentAssertions;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.OperationMode;

public sealed class OperationModeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _stateFile;
    private readonly InMemoryEventLogger _eventLogger = new();

    public OperationModeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"opmode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _stateFile = Path.Combine(_tempDir, "operation_mode.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* ignore */ }
    }

    private OperationModeService CreateService() =>
        new(_stateFile, _eventLogger);

    // -----------------------------------------------------------------------
    // LoadAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_DefaultsToPaper_WhenNoStateFile()
    {
        var sut = CreateService();

        await sut.LoadAsync();

        sut.CurrentMode.Should().Be("paper");
    }

    [Fact]
    public async Task LoadAsync_ReturnsPersistedMode_WhenFileExists()
    {
        // Pre-seed the state file as "live"
        await File.WriteAllTextAsync(_stateFile,
            """{"mode":"live","changed_at":"2025-01-01T00:00:00+00:00","changed_by":"operator"}""");

        var sut = CreateService();
        await sut.LoadAsync();

        sut.CurrentMode.Should().Be("live");
    }

    // -----------------------------------------------------------------------
    // PromoteToLiveAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task PromoteToLiveAsync_SetsCurrentModeLive_AndPersists()
    {
        var sut = CreateService();

        await sut.PromoteToLiveAsync("unit test");

        sut.CurrentMode.Should().Be("live");
        File.Exists(_stateFile).Should().BeTrue();

        // Reload in a new instance to confirm persistence
        var sut2 = CreateService();
        await sut2.LoadAsync();
        sut2.CurrentMode.Should().Be("live");
    }

    [Fact]
    public async Task PromoteToLiveAsync_EmitsModeChangedEvent()
    {
        var sut = CreateService();

        await sut.PromoteToLiveAsync("promote test");

        _eventLogger.Events.Should().ContainSingle(e => e.EventType == EventTypes.ModeChanged);
        var evt = _eventLogger.Events.Single(e => e.EventType == EventTypes.ModeChanged);
        evt.Data.Should().ContainKey("new_mode").WhoseValue.Should().Be("live");
        evt.Data.Should().ContainKey("previous_mode").WhoseValue.Should().Be("paper");
        evt.Data.Should().ContainKey("operator_note").WhoseValue.Should().Be("promote test");
    }

    // -----------------------------------------------------------------------
    // DemoteToPaperAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DemoteToPaperAsync_SetsCurrentModePaper_AndPersists()
    {
        var sut = CreateService();
        await sut.PromoteToLiveAsync();
        _eventLogger.Clear();

        await sut.DemoteToPaperAsync("back to paper");

        sut.CurrentMode.Should().Be("paper");

        var sut2 = CreateService();
        await sut2.LoadAsync();
        sut2.CurrentMode.Should().Be("paper");
    }

    [Fact]
    public async Task DemoteToPaperAsync_EmitsModeChangedEvent()
    {
        var sut = CreateService();
        await sut.PromoteToLiveAsync();
        _eventLogger.Clear();

        await sut.DemoteToPaperAsync("demote test");

        _eventLogger.Events.Should().ContainSingle(e => e.EventType == EventTypes.ModeChanged);
        var evt = _eventLogger.Events.Single(e => e.EventType == EventTypes.ModeChanged);
        evt.Data.Should().ContainKey("new_mode").WhoseValue.Should().Be("paper");
        evt.Data.Should().ContainKey("previous_mode").WhoseValue.Should().Be("live");
    }

    // -----------------------------------------------------------------------
    // Concurrency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentPromotions_OnlyOneSucceeds_CurrentModeIsLive()
    {
        var sut = CreateService();

        // Two concurrent promotions — both should complete without exception.
        await Task.WhenAll(
            sut.PromoteToLiveAsync("concurrent 1"),
            sut.PromoteToLiveAsync("concurrent 2"));

        sut.CurrentMode.Should().Be("live");
    }

    // -----------------------------------------------------------------------
    // Round-trip through new instance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task LoadAsync_AfterPromote_ReturnsPreviousMode()
    {
        var sut = CreateService();
        await sut.PromoteToLiveAsync("persist check");

        // New service instance pointing at the same file
        var sut2 = CreateService();
        await sut2.LoadAsync();

        sut2.CurrentMode.Should().Be("live");
    }
}

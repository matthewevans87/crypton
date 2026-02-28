using System.Text.Json;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Resilience;

public sealed class FailureTrackerTests : IDisposable
{
    private readonly string _tempDir;

    public FailureTrackerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private FailureTracker CreateSut(int threshold = 3) =>
        new(Options.Create(new ExecutionServiceConfig
        {
            Safety = new SafetyConfig
            {
                ConsecutiveFailureThreshold = threshold,
                ResilienceStatePath = _tempDir
            }
        }), NullLogger<FailureTracker>.Instance);

    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RecordFailure_IncrementsConsecutiveFailures()
    {
        var sut = CreateSut();
        sut.RecordFailure();
        sut.RecordFailure();
        sut.ConsecutiveFailures.Should().Be(2);
    }

    [Fact]
    public void RecordSuccess_ResetsConsecutiveFailuresToZero()
    {
        var sut = CreateSut();
        sut.RecordFailure();
        sut.RecordFailure();
        sut.RecordSuccess();
        sut.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void RecordFailure_SetsSafeModeTriggered_WhenThresholdReached()
    {
        var sut = CreateSut(threshold: 2);
        sut.RecordFailure();
        sut.SafeModeTriggered.Should().BeFalse();
        sut.RecordFailure();
        sut.SafeModeTriggered.Should().BeTrue();
    }

    [Fact]
    public void RecordFailure_PersistsCountToDisk()
    {
        var sut = CreateSut();
        sut.RecordFailure();
        sut.RecordFailure();

        var json = File.ReadAllText(Path.Combine(_tempDir, "failure_count.json"));
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("consecutive_failures").GetInt32().Should().Be(2);
    }

    [Fact]
    public void RecordSuccess_ResetsPersistedCountToZero()
    {
        var sut = CreateSut();
        sut.RecordFailure();
        sut.RecordFailure();
        sut.RecordSuccess();

        var json = File.ReadAllText(Path.Combine(_tempDir, "failure_count.json"));
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("consecutive_failures").GetInt32().Should().Be(0);
    }

    [Fact]
    public void Constructor_LoadsFromDisk_SetsSafeModeTriggeredWhenCountGEThreshold()
    {
        // Pre-write a state file with count == threshold
        File.WriteAllText(
            Path.Combine(_tempDir, "failure_count.json"),
            """{"consecutive_failures": 3, "last_failure_utc": "2024-01-01T00:00:00Z"}""");

        var sut = CreateSut(threshold: 3);

        sut.ConsecutiveFailures.Should().Be(3);
        sut.SafeModeTriggered.Should().BeTrue();
    }

    [Fact]
    public void Constructor_LoadsFromDisk_DoesNotTriggerSafeMode_WhenCountBelowThreshold()
    {
        File.WriteAllText(
            Path.Combine(_tempDir, "failure_count.json"),
            """{"consecutive_failures": 2, "last_failure_utc": "2024-01-01T00:00:00Z"}""");

        var sut = CreateSut(threshold: 3);

        sut.ConsecutiveFailures.Should().Be(2);
        sut.SafeModeTriggered.Should().BeFalse();
    }

    [Fact]
    public void Reset_ClearsConsecutiveFailures()
    {
        var sut = CreateSut();
        sut.RecordFailure();
        sut.RecordFailure();
        sut.Reset();
        sut.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Reset_ClearsSafeModeTriggered()
    {
        var sut = CreateSut(threshold: 1);
        sut.RecordFailure(); // triggers safe mode
        sut.SafeModeTriggered.Should().BeTrue();
        sut.Reset();
        sut.SafeModeTriggered.Should().BeFalse();
    }

    [Fact]
    public void Reset_PersistsZeroCountToDisk()
    {
        var sut = CreateSut();
        sut.RecordFailure();
        sut.Reset();

        var json = File.ReadAllText(Path.Combine(_tempDir, "failure_count.json"));
        var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("consecutive_failures").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task OnSafeModeTriggered_FiredExactlyOnce_WhenThresholdFirstReached()
    {
        var sut = CreateSut(threshold: 2);
        var tcs = new TaskCompletionSource<string>();
        sut.OnSafeModeTriggered += reason =>
        {
            tcs.TrySetResult(reason);
            return Task.CompletedTask;
        };

        sut.RecordFailure(); // count = 1, below threshold
        sut.RecordFailure(); // count = 2, hits threshold → event fires

        var reason = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        reason.Should().Be("consecutive_failures");

        // A third failure must NOT fire the event again
        int callCount = 0;
        sut.OnSafeModeTriggered += _ => { callCount++; return Task.CompletedTask; };
        sut.RecordFailure();
        await Task.Delay(50);
        callCount.Should().Be(0);
    }
}

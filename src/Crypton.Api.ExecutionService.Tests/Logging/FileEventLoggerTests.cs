using System.Text.Json;
using Crypton.Api.ExecutionService.Configuration;
using Crypton.Api.ExecutionService.Logging;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Crypton.Api.ExecutionService.Tests.Logging;

public sealed class FileEventLoggerTests : IAsyncDisposable
{
    private readonly string _tempDir;

    public FileEventLoggerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    private FileEventLogger CreateLogger(bool rotateDaily = false, string? fileName = null)
    {
        var logPath = Path.Combine(_tempDir, fileName ?? "events.ndjson");
        var config = new ExecutionServiceConfig
        {
            Logging = new LoggingConfig
            {
                EventLogPath = logPath,
                RotateDaily = rotateDaily
            }
        };
        return new FileEventLogger(Options.Create(config));
    }

    // ── basic write ──────────────────────────────────────────────────────────

    [Fact]
    public async Task WriteEvent_FileIsCreated()
    {
        await using var logger = CreateLogger();
        await logger.LogAsync(EventTypes.ServiceStarted, "paper");

        var files = Directory.GetFiles(_tempDir);
        files.Should().HaveCount(1);
    }

    [Fact]
    public async Task WriteEvent_FileContainsValidJson()
    {
        await using var logger = CreateLogger();
        await logger.LogAsync(EventTypes.ServiceStarted, "paper");

        var filePath = Directory.GetFiles(_tempDir).Single();
        var line = (await File.ReadAllLinesAsync(filePath)).Single();

        var doc = JsonDocument.Parse(line);
        doc.RootElement.GetProperty("event_type").GetString().Should().Be(EventTypes.ServiceStarted);
        doc.RootElement.GetProperty("mode").GetString().Should().Be("paper");
        doc.RootElement.TryGetProperty("timestamp", out _).Should().BeTrue();
    }

    [Fact]
    public async Task WriteEvent_TimestampAndModeAreCorrect()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await using var logger = CreateLogger();
        await logger.LogAsync(EventTypes.StrategyLoaded, "live",
            new Dictionary<string, object?> { ["strategy_id"] = "sha256abc" });
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        var filePath = Directory.GetFiles(_tempDir).Single();
        var json = await File.ReadAllTextAsync(filePath);
        var doc = JsonDocument.Parse(json.Trim());

        var ts = doc.RootElement.GetProperty("timestamp").GetDateTimeOffset();
        ts.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);

        doc.RootElement.GetProperty("mode").GetString().Should().Be("live");
    }

    // ── NDJSON ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleEvents_EachOnSeparateLine()
    {
        await using var logger = CreateLogger();
        await logger.LogAsync(EventTypes.ServiceStarted, "paper");
        await logger.LogAsync(EventTypes.StrategyLoaded, "paper");
        await logger.LogAsync(EventTypes.OrderPlaced, "paper");

        var filePath = Directory.GetFiles(_tempDir).Single();
        var lines = (await File.ReadAllLinesAsync(filePath))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        lines.Should().HaveCount(3);

        foreach (var line in lines)
        {
            var act = () => JsonDocument.Parse(line);
            act.Should().NotThrow("each line must be valid JSON");
        }
    }

    [Fact]
    public async Task MultipleEvents_AreInOrder()
    {
        await using var logger = CreateLogger();
        await logger.LogAsync(EventTypes.ServiceStarted, "paper");
        await logger.LogAsync(EventTypes.StrategyLoaded, "paper");
        await logger.LogAsync(EventTypes.OrderPlaced, "paper");

        var filePath = Directory.GetFiles(_tempDir).Single();
        var lines = (await File.ReadAllLinesAsync(filePath))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToArray();

        var types = lines.Select(l => JsonDocument.Parse(l).RootElement
            .GetProperty("event_type").GetString()).ToArray();

        types.Should().ContainInOrder(
            EventTypes.ServiceStarted,
            EventTypes.StrategyLoaded,
            EventTypes.OrderPlaced);
    }

    // ── daily rotation ────────────────────────────────────────────────────────

    [Fact]
    public async Task DailyRotation_FileNameIncludesDate()
    {
        await using var logger = CreateLogger(rotateDaily: true, fileName: "events.ndjson");
        await logger.LogAsync(EventTypes.ServiceStarted, "paper");

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var expected = $"events.{today:yyyy-MM-dd}.ndjson";

        var files = Directory.GetFiles(_tempDir).Select(Path.GetFileName).ToArray();
        files.Should().Contain(expected);
    }

    [Fact]
    public async Task NoRotation_UsesOriginalFilename()
    {
        await using var logger = CreateLogger(rotateDaily: false, fileName: "mylog.ndjson");
        await logger.LogAsync(EventTypes.ServiceStarted, "paper");

        var files = Directory.GetFiles(_tempDir).Select(Path.GetFileName).ToArray();
        files.Should().Contain("mylog.ndjson");
    }

    // ── data field ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EventWithData_DataFieldPersistedInFile()
    {
        await using var logger = CreateLogger();
        await logger.LogAsync(EventTypes.OrderPlaced, "paper",
            new Dictionary<string, object?> { ["order_id"] = "X1", ["qty"] = 2.5 });

        var filePath = Directory.GetFiles(_tempDir).Single();
        var line = (await File.ReadAllLinesAsync(filePath)).First(l => !string.IsNullOrWhiteSpace(l));
        var doc = JsonDocument.Parse(line);

        doc.RootElement.TryGetProperty("data", out var data).Should().BeTrue();
        data.GetProperty("order_id").GetString().Should().Be("X1");
    }

    public async ValueTask DisposeAsync()
    {
        // Clean up temp directory
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}

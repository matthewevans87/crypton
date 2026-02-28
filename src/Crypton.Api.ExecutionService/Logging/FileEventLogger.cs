using System.Text.Json;
using System.Text.Json.Serialization;
using Crypton.Api.ExecutionService.Configuration;
using Microsoft.Extensions.Options;

namespace Crypton.Api.ExecutionService.Logging;

/// <summary>
/// Writes events to an NDJSON file on disk. Thread-safe via internal SemaphoreSlim.
/// Supports daily rotation. On write failure, logs to stderr and surfaces an alert
/// rather than crashing.
/// </summary>
public sealed class FileEventLogger : IEventLogger, IAsyncDisposable
{
    private readonly LoggingConfig _config;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private StreamWriter? _writer;
    private DateOnly _currentDate;
    private bool _hasWriteError;

    // In-memory ring buffer for GetRecentAsync
    private readonly List<ExecutionEvent> _recent = [];
    private const int MaxRecent = 1000;

    public bool HasWriteError => _hasWriteError;

    /// <inheritdoc/>
    public event Func<ExecutionEvent, Task>? OnEventLogged;

    public FileEventLogger(IOptions<ExecutionServiceConfig> config)
        => _config = config.Value.Logging;

    public async Task LogAsync(
        string eventType,
        string mode,
        IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default)
    {
        var evt = new ExecutionEvent
        {
            Timestamp = DateTimeOffset.UtcNow,
            EventType = eventType,
            Mode = mode,
            Data = data
        };

        var line = JsonSerializer.Serialize(evt);

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await EnsureWriterAsync();
            if (_writer is null) return;
            await _writer.WriteLineAsync(line);
            await _writer.FlushAsync(cancellationToken);

            // Keep ring buffer
            _recent.Add(evt);
            if (_recent.Count > MaxRecent)
                _recent.RemoveAt(0);
        }
        catch (Exception ex)
        {
            _hasWriteError = true;
            await Console.Error.WriteLineAsync($"[EventLogger] Write failure: {ex.Message}");
            return;
        }
        finally
        {
            _lock.Release();
        }

        // Raise event outside the lock
        if (OnEventLogged is not null)
        {
            try { await OnEventLogged(evt); }
            catch { /* subscribers must not crash the logger */ }
        }
    }

    public Task<IReadOnlyList<ExecutionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        _lock.Wait(cancellationToken);
        try
        {
            var result = _recent.TakeLast(limit).ToList();
            return Task.FromResult<IReadOnlyList<ExecutionEvent>>(result);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task EnsureWriterAsync()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (_writer is not null && _currentDate == today) return;

        // Rotate or open new file
        if (_writer is not null) await _writer.DisposeAsync();

        var path = _config.RotateDaily
            ? Path.ChangeExtension(_config.EventLogPath, $".{today:yyyy-MM-dd}.ndjson")
            : _config.EventLogPath;

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _writer = new StreamWriter(path, append: true, encoding: System.Text.Encoding.UTF8);
        _currentDate = today;
    }

    public async ValueTask DisposeAsync()
    {
        if (_writer is not null) await _writer.DisposeAsync();
        _lock.Dispose();
    }
}

// AOT-friendly JSON serialization context
[System.Text.Json.Serialization.JsonSerializable(typeof(ExecutionEvent))]
internal sealed partial class ExecutionEventJsonContext : JsonSerializerContext { }


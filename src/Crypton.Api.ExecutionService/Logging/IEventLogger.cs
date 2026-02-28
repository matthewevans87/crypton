namespace Crypton.Api.ExecutionService.Logging;

/// <summary>
/// Writes structured events to the append-only execution event log.
/// Implementations must be thread-safe and serialize writes to prevent
/// interleaved or corrupt NDJSON lines.
/// </summary>
public interface IEventLogger
{
    /// <summary>
    /// Write a structured event to the log. This call is synchronous relative
    /// to the action it describes — the caller awaits this before considering
    /// the action complete.
    /// </summary>
    Task LogAsync(string eventType, string mode, IReadOnlyDictionary<string, object?>? data = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the most recent <paramref name="limit"/> events in chronological order.
    /// </summary>
    Task<IReadOnlyList<ExecutionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Raised after each event is successfully written. May be null if no subscribers.
    /// Implementors must invoke this outside any internal locks.
    /// </summary>
    event Func<ExecutionEvent, Task>? OnEventLogged;
}

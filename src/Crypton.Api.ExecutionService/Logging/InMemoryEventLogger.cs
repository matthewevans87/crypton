namespace Crypton.Api.ExecutionService.Logging;

/// <summary>
/// In-memory event logger for use in tests. Thread-safe.
/// Captures all events in an ordered list for assertion.
/// </summary>
public sealed class InMemoryEventLogger : IEventLogger
{
    private readonly List<ExecutionEvent> _events = [];
    private readonly Lock _lock = new();

    /// <inheritdoc/>
    public event Func<ExecutionEvent, Task>? OnEventLogged;

    public IReadOnlyList<ExecutionEvent> Events
    {
        get { lock (_lock) { return _events.ToList(); } }
    }

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
        lock (_lock) { _events.Add(evt); }

        if (OnEventLogged is not null)
        {
            try { await OnEventLogged(evt); }
            catch { /* subscribers must not crash the logger */ }
        }
    }

    public Task<IReadOnlyList<ExecutionEvent>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            IReadOnlyList<ExecutionEvent> result = _events.TakeLast(limit).ToList();
            return Task.FromResult(result);
        }
    }

    public void Clear() { lock (_lock) { _events.Clear(); } }
}

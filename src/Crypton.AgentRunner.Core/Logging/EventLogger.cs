using System.Text.Json;
using System.Text.Json.Serialization;
using AgentRunner.Abstractions;
using AgentRunner.Domain;

namespace AgentRunner.Logging;

/// <summary>
/// File-system event logger. Writes a rolling text log, a structured JSONL event stream,
/// and (when enabled) per-cycle prompt snapshots, tool call journals, and invocation manifests
/// for post-run audit and replay.
/// </summary>
public sealed class EventLogger : IEventLogger
{
    private readonly string _logPath;
    private readonly string _structuredLogPath;
    private readonly string? _cyclesBasePath;
    private readonly bool _capturePrompts;
    private readonly long _maxFileSizeBytes;
    private readonly int _maxFileCount;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public EventLogger(
        string logPath,
        string? cyclesBasePath,
        int maxFileSizeMb = 20,
        int maxFileCount = 5,
        bool capturePrompts = true)
    {
        _logPath = logPath;
        _cyclesBasePath = cyclesBasePath;
        _capturePrompts = capturePrompts;
        _maxFileSizeBytes = (long)maxFileSizeMb * 1024 * 1024;
        _maxFileCount = maxFileCount;

        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        _structuredLogPath = Path.Combine(dir!, "events.jsonl");
    }

    public void LogInfo(string message) => Log("INFO", message);
    public void LogWarning(string message) => Log("WARN", message);
    public void LogError(string message) => Log("ERROR", message);

    public void LogStateTransition(LoopState from, LoopState to)
    {
        Log("STATE", $"Transition: {from} -> {to}");
        LogStructured("state_transition", details: $"{{\"from\":\"{from}\",\"to\":\"{to}\"}}");
    }

    public void LogAgentInvocation(string agentName, string cycleId)
    {
        Log("AGENT", $"Starting {agentName} for cycle {cycleId}");
        LogStructured("agent_invocation", agentName, agentName, cycleId);
    }

    public void LogAgentCompletion(string agentName, string cycleId, bool success)
    {
        var status = success ? "completed" : "failed";
        Log("AGENT", $"{agentName} for cycle {cycleId} {status}");
        LogStructured("agent_completion", agentName, agentName, cycleId,
            $"{{\"success\":{success.ToString().ToLowerInvariant()}}}");
    }

    public void LogToolExecution(string toolName, string parameters, long durationMs)
    {
        Log("TOOL", $"Executed {toolName} in {durationMs}ms");
        LogStructured("tool_execution", details:
            $"{{\"tool\":\"{toolName}\",\"duration_ms\":{durationMs}}}");
    }

    public void LogMailboxDelivery(string fromAgent, string toAgent, string content)
    {
        var preview = content.Length > 100 ? content[..100] + "..." : content;
        Log("MAILBOX", $"Delivered to {toAgent} from {fromAgent}: {preview}");
        LogStructured("mailbox_delivery", details:
            $"{{\"from\":\"{fromAgent}\",\"to\":\"{toAgent}\"}}");
    }

    public void LogRetryAttempt(string agentName, string step, int attempt, int maxAttempts)
    {
        Log("RETRY", $"Attempt {attempt}/{maxAttempts} for {agentName} / {step}");
        LogStructured("retry_attempt", agentName, step, details:
            $"{{\"attempt\":{attempt},\"max_attempts\":{maxAttempts}}}");
    }

    public void LogPromptSnapshot(string agentName, string cycleId, string systemPrompt, string userPrompt)
    {
        if (!_capturePrompts || _cyclesBasePath is null)
            return;

        var dir = GetCycleDir(cycleId);
        var path = Path.Combine(dir, $"{agentName.ToLowerInvariant()}_prompt_snapshot.md");

        var content =
            $"# Prompt Snapshot — {agentName} / {cycleId}\n\n" +
            $"Captured: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC\n\n" +
            "## System Prompt\n\n" + systemPrompt +
            "\n\n## User Prompt\n\n" + userPrompt + "\n";

        lock (_lock)
        {
            try { File.WriteAllText(path, content); }
            catch { /* non-fatal */ }
        }

        Log("SNAPSHOT", $"Prompt snapshot written for {agentName} cycle {cycleId}");
        LogStructured("prompt_snapshot", agentName, agentName, cycleId,
            $"{{\"path\":\"{path.Replace('\\', '/')}\"}}");
    }

    public void LogToolCallJournal(
        string agentName, string cycleId, int iteration,
        string toolName, string parameters, string result, long durationMs)
    {
        if (!_capturePrompts || _cyclesBasePath is null)
            return;

        var dir = GetCycleDir(cycleId);
        var path = Path.Combine(dir, $"{agentName.ToLowerInvariant()}_tool_log.jsonl");

        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            iteration,
            tool = toolName,
            parameters,
            result,
            duration_ms = durationMs
        };

        lock (_lock)
        {
            try { File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine); }
            catch { /* non-fatal */ }
        }
    }

    public void LogInvocationManifest(string agentName, string cycleId, InvocationManifest manifest)
    {
        if (_cyclesBasePath is null)
            return;

        var dir = GetCycleDir(cycleId);
        var path = Path.Combine(dir, $"{agentName.ToLowerInvariant()}_run_manifest.json");

        lock (_lock)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(manifest, SnakeCaseOptions)); }
            catch { /* non-fatal */ }
        }

        Log("MANIFEST",
            $"{agentName} cycle {cycleId}: success={manifest.Success}, " +
            $"iterations={manifest.IterationsUsed}/{manifest.MaxIterations}, duration={manifest.DurationMs}ms");
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private string GetCycleDir(string cycleId)
    {
        var dir = Path.Combine(_cyclesBasePath!, cycleId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void LogStructured(
        string eventType, string agent = "", string step = "",
        string cycleId = "", string details = "")
    {
        var entry = new StructuredEntry(
            DateTimeOffset.UtcNow, eventType, agent, step, cycleId, details);

        var json = JsonSerializer.Serialize(entry, SnakeCaseOptions);

        lock (_lock)
        {
            try
            {
                RotateIfNeeded(_structuredLogPath);
                File.AppendAllText(_structuredLogPath, json + Environment.NewLine);
            }
            catch { /* non-fatal */ }
        }
    }

    private void Log(string level, string message)
    {
        var ts = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var line = $"[{ts}] [{level,-7}] {message}";

        lock (_lock)
        {
            try
            {
                RotateIfNeeded(_logPath);
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch { /* non-fatal */ }
        }
    }

    private void RotateIfNeeded(string path)
    {
        if (!File.Exists(path))
            return;
        if (new FileInfo(path).Length < _maxFileSizeBytes)
            return;

        var oldest = $"{path}.{_maxFileCount}";
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (var i = _maxFileCount - 1; i >= 1; i--)
        {
            var src = $"{path}.{i}";
            var dst = $"{path}.{i + 1}";
            if (File.Exists(src))
                File.Move(src, dst);
        }

        File.Move(path, $"{path}.1");
    }

    private sealed record StructuredEntry(
        [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
        [property: JsonPropertyName("event_type")] string EventType,
        [property: JsonPropertyName("agent")] string Agent,
        [property: JsonPropertyName("step")] string Step,
        [property: JsonPropertyName("cycle_id")] string CycleId,
        [property: JsonPropertyName("details")] string Details);
}

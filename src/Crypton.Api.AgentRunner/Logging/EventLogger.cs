using AgentRunner.StateMachine;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AgentRunner.Logging;

public interface IEventLogger
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogStateTransition(LoopState from, LoopState to);
    void LogAgentInvocation(string agentName, string cycleId);
    void LogAgentCompletion(string agentName, string cycleId, bool success);
    void LogToolExecution(string toolName, string parameters, long durationMs);
    void LogMailboxDelivery(string fromAgent, string toAgent, string content);
    void LogRetryAttempt(string agentName, string step, int attempt, int maxAttempts);

    /// <summary>Writes the full system + user prompts to {cycleId}/{agent}_prompt_snapshot.md for post-run analysis.</summary>
    void LogPromptSnapshot(string agentName, string cycleId, string systemPrompt, string userPrompt);

    /// <summary>Appends one line to {cycleId}/{agent}_tool_log.jsonl capturing the full parameters and result of each tool call.</summary>
    void LogToolCallJournal(string agentName, string cycleId, int iteration, string toolName, string parameters, string result, long durationMs);

    /// <summary>Writes {cycleId}/{agent}_run_manifest.json summarising the invocation: model, temperature, iterations, duration, success.</summary>
    void LogInvocationManifest(string agentName, string cycleId, InvocationManifest manifest);
}

/// <summary>Summary record written to {agent}_run_manifest.json after every agent invocation.</summary>
public sealed record InvocationManifest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("temperature")] double Temperature,
    [property: JsonPropertyName("num_ctx")] int NumCtx,
    [property: JsonPropertyName("iterations_used")] int IterationsUsed,
    [property: JsonPropertyName("max_iterations")] int MaxIterations,
    [property: JsonPropertyName("duration_ms")] long DurationMs,
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("error")] string? Error);

public class EventLogger : IEventLogger
{
    private readonly string _logPath;
    private readonly string _structuredLogPath;
    private readonly string? _cyclesBasePath;
    private readonly bool _capturePrompts;
    private readonly object _lock = new();
    private readonly long _maxFileSizeBytes;
    private readonly int _maxFileCount;

    private static readonly JsonSerializerOptions SnakeCaseOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <param name="logPath">Path to the rolling text log file (e.g. ./logs/agent_runner.log).</param>
    /// <param name="cyclesBasePath">Base path for per-cycle artifact directories (e.g. ./artifacts/cycles). Required for prompt/tool/manifest capture; pass null to disable.</param>
    /// <param name="maxFileSizeMb">Maximum size in MB before the text log is rotated.</param>
    /// <param name="maxFileCount">Number of rotated log files to retain.</param>
    /// <param name="capturePrompts">When true (default), writes prompt snapshots and tool journals to the cycle directory.</param>
    public EventLogger(string logPath, string? cyclesBasePath, int maxFileSizeMb = 100, int maxFileCount = 5, bool capturePrompts = true)
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

    private void RotateIfNeeded(string path)
    {
        if (!File.Exists(path)) return;
        if (new FileInfo(path).Length < _maxFileSizeBytes) return;

        // Delete the oldest file if it exists
        var oldest = $"{path}.{_maxFileCount}";
        if (File.Exists(oldest)) File.Delete(oldest);

        // Shift existing rotated files up by one
        for (var i = _maxFileCount - 1; i >= 1; i--)
        {
            var src = $"{path}.{i}";
            var dst = $"{path}.{i + 1}";
            if (File.Exists(src)) File.Move(src, dst);
        }

        // Rotate the current log to .1
        File.Move(path, $"{path}.1");
    }

    public void LogInfo(string message) => Log("INFO", message);
    public void LogWarning(string message) => Log("WARN", message);
    public void LogError(string message) => Log("ERROR", message);

    public void LogStateTransition(LoopState from, LoopState to)
    {
        Log("STATE", $"Transition: {from} -> {to}");
        LogStructured("state_transition", details: $"{{\"from\": \"{from}\", \"to\": \"{to}\"}}");
    }

    public void LogAgentInvocation(string agentName, string cycleId)
    {
        Log("AGENT", $"Starting {agentName} for cycle {cycleId}");
        LogStructured("agent_invocation", agentName, step: agentName, cycleId, "started");
    }

    public void LogAgentCompletion(string agentName, string cycleId, bool success)
    {
        var status = success ? "completed" : "failed";
        Log("AGENT", $"{agentName} for cycle {cycleId} {status}");
        LogStructured("agent_completion", agentName, step: agentName, cycleId, success ? "success" : "failed");
    }

    public void LogToolExecution(string toolName, string parameters, long durationMs)
    {
        Log("TOOL", $"Executed {toolName} in {durationMs}ms");
        LogStructured("tool_execution", details: $"{{\"tool\": \"{toolName}\", \"duration_ms\": {durationMs}}}");
    }

    public void LogMailboxDelivery(string fromAgent, string toAgent, string content)
    {
        var preview = content.Length > 100 ? content[..100] + "..." : content;
        Log("MAILBOX", $"Delivered to {toAgent} from {fromAgent}: {preview}");
        LogStructured("mailbox_delivery", details: $"{{\"from\": \"{fromAgent}\", \"to\": \"{toAgent}\"}}");
    }

    public void LogRetryAttempt(string agentName, string step, int attempt, int maxAttempts)
    {
        Log("RETRY", $"Attempt {attempt}/{maxAttempts} for {agentName}");
        LogStructured("retry_attempt", agentName, step, details: $"{{\"attempt\": {attempt}, \"max_attempts\": {maxAttempts}}}");
    }

    public void LogPromptSnapshot(string agentName, string cycleId, string systemPrompt, string userPrompt)
    {
        if (!_capturePrompts || _cyclesBasePath == null) return;

        var dir = GetCycleDir(cycleId);
        var path = Path.Combine(dir, $"{agentName.ToLower()}_prompt_snapshot.md");

        var content =
            $"# Prompt Snapshot \u2014 {agentName} / {cycleId}\n\n" +
            $"Captured: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC\n\n" +
            "## System Prompt\n\n" +
            systemPrompt +
            "\n\n## User Prompt\n\n" +
            userPrompt + "\n";

        lock (_lock)
        {
            try { File.WriteAllText(path, content); }
            catch { }
        }

        var normalizedPath = path.Replace('\\', '/');
        Log("SNAPSHOT", $"Prompt snapshot written for {agentName} cycle {cycleId}");
        LogStructured("prompt_snapshot", agentName, agentName, cycleId,
            $"{{\"path\": \"{normalizedPath}\"}}");
    }

    public void LogToolCallJournal(string agentName, string cycleId, int iteration, string toolName, string parameters, string result, long durationMs)
    {
        if (!_capturePrompts || _cyclesBasePath == null) return;

        var dir = GetCycleDir(cycleId);
        var path = Path.Combine(dir, $"{agentName.ToLower()}_tool_log.jsonl");

        var entry = new
        {
            timestamp = DateTime.UtcNow,
            iteration,
            tool = toolName,
            parameters,
            result,
            duration_ms = durationMs
        };

        lock (_lock)
        {
            try { File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine); }
            catch { }
        }
    }

    public void LogInvocationManifest(string agentName, string cycleId, InvocationManifest manifest)
    {
        if (_cyclesBasePath == null) return;

        var dir = GetCycleDir(cycleId);
        var path = Path.Combine(dir, $"{agentName.ToLower()}_run_manifest.json");

        lock (_lock)
        {
            try { File.WriteAllText(path, JsonSerializer.Serialize(manifest, SnakeCaseOptions)); }
            catch { }
        }

        Log("MANIFEST", $"{agentName} cycle {cycleId}: success={manifest.Success}, iterations={manifest.IterationsUsed}/{manifest.MaxIterations}, duration={manifest.DurationMs}ms");
        LogStructured("invocation_manifest", agentName, agentName, cycleId,
            $"{{\"success\": {manifest.Success.ToString().ToLower()}, \"iterations\": {manifest.IterationsUsed}, \"duration_ms\": {manifest.DurationMs}}}");
    }

    private string GetCycleDir(string cycleId)
    {
        var dir = Path.Combine(_cyclesBasePath!, cycleId);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void LogStructured(string eventType, string agent = "", string step = "", string cycleId = "", string details = "")
    {
        var entry = new StructuredLogEntry
        {
            Timestamp = DateTime.UtcNow,
            EventType = eventType,
            Agent = agent,
            Step = step,
            CycleId = cycleId,
            Details = details
        };

        var json = JsonSerializer.Serialize(entry);

        lock (_lock)
        {
            try
            {
                RotateIfNeeded(_structuredLogPath);
                File.AppendAllText(_structuredLogPath, json + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    private void Log(string level, string message)
    {
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var logLine = $"[{timestamp}] [{level}] {message}";

        lock (_lock)
        {
            try
            {
                RotateIfNeeded(_logPath);
                File.AppendAllText(_logPath, logLine + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}

public class StructuredLogEntry
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Level { get; set; } = "INFO";
    public string EventType { get; set; } = string.Empty;
    public string Agent { get; set; } = string.Empty;
    public string Step { get; set; } = string.Empty;
    public string CycleId { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

using AgentRunner.StateMachine;
using System.Text.Json;

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
}

public class EventLogger : IEventLogger
{
    private readonly string _logPath;
    private readonly string _structuredLogPath;
    private readonly object _lock = new();

    public EventLogger(string logPath)
    {
        _logPath = logPath;
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        _structuredLogPath = Path.Combine(dir, "events.jsonl");
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

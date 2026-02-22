using AgentRunner.StateMachine;

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
}

public class EventLogger : IEventLogger
{
    private readonly string _logPath;
    private readonly object _lock = new();

    public EventLogger(string logPath)
    {
        _logPath = logPath;
        var dir = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public void LogInfo(string message) => Log("INFO", message);
    public void LogWarning(string message) => Log("WARN", message);
    public void LogError(string message) => Log("ERROR", message);

    public void LogStateTransition(LoopState from, LoopState to)
    {
        Log("STATE", $"Transition: {from} -> {to}");
    }

    public void LogAgentInvocation(string agentName, string cycleId)
    {
        Log("AGENT", $"Starting {agentName} for cycle {cycleId}");
    }

    public void LogAgentCompletion(string agentName, string cycleId, bool success)
    {
        var status = success ? "completed" : "failed";
        Log("AGENT", $"{agentName} for cycle {cycleId} {status}");
    }

    public void LogToolExecution(string toolName, string parameters, long durationMs)
    {
        Log("TOOL", $"Executed {toolName} in {durationMs}ms");
    }

    public void LogMailboxDelivery(string fromAgent, string toAgent, string content)
    {
        var preview = content.Length > 100 ? content[..100] + "..." : content;
        Log("MAILBOX", $"Delivered to {toAgent} from {fromAgent}: {preview}");
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

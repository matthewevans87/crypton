namespace AgentRunner.Configuration;

public class AgentRunnerConfig
{
    public CycleConfig Cycle { get; set; } = new();
    public ResilienceConfig Resilience { get; set; } = new();
    public AgentConfig Agents { get; set; } = new();
    public ToolConfig Tools { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public ApiConfig Api { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();
}

public class CycleConfig
{
    public int MinDurationMinutes { get; set; } = 60;
    public int MaxDurationMinutes { get; set; } = 1440;
    public int ScheduleIntervalMinutes { get; set; } = 360;
    public bool EnableParallelExecution { get; set; } = false;
    public string Schedule { get; set; } = string.Empty;
    public Dictionary<string, int> StepTimeoutOverrides { get; set; } = new();
}

public class ResilienceConfig
{
    public int MaxRestartAttempts { get; set; } = 5;
    public int BaseRestartDelayMinutes { get; set; } = 1;
    public int MaxRestartDelayMinutes { get; set; } = 30;
    public int StallWarningMinutes { get; set; } = 10;
    public int StallCriticalMinutes { get; set; } = 30;
}

public class AgentConfig
{
    public AgentSettings Plan { get; set; } = new();
    public AgentSettings Research { get; set; } = new();
    public AgentSettings Analyze { get; set; } = new();
    public AgentSettings Synthesis { get; set; } = new();
    public AgentSettings Evaluation { get; set; } = new();
}

public class AgentSettings
{
    public string Model { get; set; } = "gpt-4";
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutMinutes { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    /// <summary>Maximum number of LLM ↔ tool-call iterations per agent invocation.</summary>
    public int MaxIterations { get; set; } = 50;
    public string SystemPromptPath { get; set; } = string.Empty;
}

public class ToolConfig
{
    public BraveSearchConfig BraveSearch { get; set; } = new();
    public ExecutionServiceConfig ExecutionService { get; set; } = new();
    public MarketDataServiceConfig MarketDataService { get; set; } = new();
    public int DefaultTimeoutSeconds { get; set; } = 30;
    public int CacheTtlSeconds { get; set; } = 60;
    /// <summary>Maximum number of retry attempts for a failed tool call.</summary>
    public int MaxRetries { get; set; } = 3;
    /// <summary>Maximum delay in seconds applied between retries (exponential backoff is capped here).</summary>
    public int MaxRetryDelaySeconds { get; set; } = 30;
}

public class OllamaConfig
{
    /// <summary>Base URL for the Ollama API. Override via env var: Ollama__BaseUrl</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Request timeout in seconds when calling the Ollama API.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}

public class BraveSearchConfig
{
    /// <summary>Injected at runtime via env var Tools__BraveSearch__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public class ExecutionServiceConfig
{
    public string BaseUrl { get; set; } = "http://localhost:5000";
}

public class MarketDataServiceConfig
{
    public string BaseUrl { get; set; } = "http://localhost:5002";
}

public class StorageConfig
{
    public string BasePath { get; set; } = "./artifacts";
    public string CyclesPath { get; set; } = "cycles";
    public string MailboxesPath { get; set; } = "mailboxes";
    public string MemoryPath { get; set; } = "memory";
    public int MaxMailboxMessages { get; set; } = 5;
    public int ArchiveRetentionCount { get; set; } = 30;
}

public class ApiConfig
{
    public string Host { get; set; } = "0.0.0.0";
    public int Port { get; set; } = 5003;
    /// <summary>Injected at runtime via env var Api__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public class LoggingConfig
{
    public string Level { get; set; } = "Information";
    public string OutputPath { get; set; } = "./logs";
    public int MaxFileSizeMb { get; set; } = 20;
    public int MaxFileCount { get; set; } = 5;
}

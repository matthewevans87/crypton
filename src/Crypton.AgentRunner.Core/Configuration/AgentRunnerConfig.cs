namespace AgentRunner.Configuration;

public class AgentRunnerConfig
{
    public CycleConfig Cycle { get; set; } = new();
    public ResilienceConfig Resilience { get; set; } = new();

    /// <summary>Per-agent settings keyed by lowercase agent name (e.g. "plan", "research").</summary>
    public Dictionary<string, AgentSettings> Agents { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ToolConfig Tools { get; set; } = new();
    public OllamaConfig Ollama { get; set; } = new();
    public StorageConfig Storage { get; set; } = new();
    public ApiConfig Api { get; set; } = new();
    public LoggingConfig Logging { get; set; } = new();

    /// <summary>Returns the settings for the given agent, falling back to default <see cref="AgentSettings"/> if not configured.</summary>
    public AgentSettings GetAgentSettings(string agentName)
    {
        if (Agents.TryGetValue(agentName, out var settings))
            return settings;
        throw new InvalidOperationException(
            $"No agent configuration found for '{agentName}'. Add an entry under AgentRunner:Agents:{agentName} in appsettings.");
    }
}

public class CycleConfig
{
    public int MinDurationMinutes { get; set; } = 60;
    public int MaxDurationMinutes { get; set; } = 1440;
    public int ScheduleIntervalMinutes { get; set; } = 360;
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

public class AgentSettings
{
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public int TimeoutMinutes { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    /// <summary>Maximum number of LLM ↔ tool-call iterations per agent invocation.</summary>
    public int MaxIterations { get; set; } = 50;
    /// <summary>Per-agent context window override. When null, falls back to <see cref="OllamaConfig.NumCtx"/>.</summary>
    public int? NumCtx { get; set; }
    public string SystemPromptPath { get; set; } = string.Empty;
}

public class ToolConfig
{
    public BraveSearchConfig BraveSearch { get; set; } = new();
    public BirdConfig Bird { get; set; } = new();
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

    /// <summary>
    /// Default context window size (num_ctx) passed to Ollama. May be overridden per agent
    /// via <see cref="AgentSettings.NumCtx"/>. Must be large enough to hold the full prompt +
    /// thinking tokens + response. The qwen3.5:35b KV cache uses q4_0 quantisation, costing
    /// ~1.6 GiB per 16k tokens on an RTX 5090 (32.6 GiB):
    ///   16k → ~24.6 GiB total  ✓
    ///   64k → ~29.4 GiB total  ✓  ← default
    ///  128k → ~35.8 GiB total  ✗  OOM
    /// Override via env var: Ollama__NumCtx
    /// </summary>
    public int NumCtx { get; set; } = 65536;
}

public class BraveSearchConfig
{
    /// <summary>Injected at runtime via env var Tools__BraveSearch__ApiKey.</summary>
    public string ApiKey { get; set; } = string.Empty;
}

public class BirdConfig
{
    /// <summary>Base URL for the bird HTTP server. Override via env var: AGENTRUNNER__TOOLS__BIRD__BASEURL</summary>
    public string BaseUrl { get; set; } = "http://localhost:11435";
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
    /// <summary>
    /// When true (default), writes a prompt snapshot, tool call journal, and invocation manifest
    /// to the cycle directory for every agent run.
    /// Override via env var: AGENTRUNNER__LOGGING__CAPTUREPROMPTS
    /// </summary>
    public bool CapturePrompts { get; set; } = true;
}

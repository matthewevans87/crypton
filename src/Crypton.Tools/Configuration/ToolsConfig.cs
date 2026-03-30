namespace Crypton.Tools.Configuration;

/// <summary>Configuration for all tools. Bind from <c>AgentRunner:Tools</c>.</summary>
public class ToolsConfig
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

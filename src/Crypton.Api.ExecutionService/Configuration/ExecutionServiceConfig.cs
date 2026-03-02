namespace Crypton.Api.ExecutionService.Configuration;

public sealed class ExecutionServiceConfig
{
    public PaperTradingConfig PaperTrading { get; init; } = new();
    public StrategyConfig Strategy { get; init; } = new();
    public SafetyConfig Safety { get; init; } = new();
    public LoggingConfig Logging { get; init; } = new();
    public ApiConfig Api { get; init; } = new();
    public StreamingConfig Streaming { get; init; } = new();
    public KrakenAdapterConfig Kraken { get; init; } = new();
    /// <summary>Base URL of the MarketData SignalR hub, e.g. http://market-data-service:5002</summary>
    public string MarketDataServiceUrl { get; init; } = "http://localhost:5002";
}

public sealed class KrakenAdapterConfig
{
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    public string WsBaseUrl { get; init; } = "wss://ws.kraken.com/v2";
    public string RestBaseUrl { get; init; } = "https://api.kraken.com";
    public int MaxReconnectAttempts { get; init; } = 5;
    public int ReconnectDelaySeconds { get; init; } = 2;
}

public sealed class PaperTradingConfig
{
    public decimal InitialBalanceUsd { get; init; } = 10_000m;
    public decimal SlippagePct { get; init; } = 0.001m;
    public decimal CommissionRate { get; init; } = 0.0026m;
    /// <summary>Path to the JSON file used to persist paper trading state (orders) across restarts.
    /// Override via <c>ExecutionService__PaperTrading__StatePath</c> env var for container deployments.</summary>
    public string StatePath { get; init; } = "artifacts/paper_state.json";
}

public sealed class StrategyConfig
{
    public string WatchPath { get; init; } = "artifacts/strategy.json";
    public int ReloadLatencyMs { get; init; } = 5000;
    public int ValidityCheckIntervalMs { get; init; } = 5000;
    public string OnLoadTriggerMode { get; init; } = "fresh_crossing";
    /// <summary>When true, a FileSystemWatcher hot-reloads strategy.json from <see cref="WatchPath"/>.</summary>
    public bool EnableFileWatcher { get; init; } = true;
    /// <summary>When true, the POST /strategy/push REST endpoint accepts strategy documents directly.</summary>
    public bool EnableRestEndpoint { get; init; } = true;
}

public sealed class SafetyConfig
{
    public int ConsecutiveFailureThreshold { get; init; } = 3;
    public string ResilienceStatePath { get; init; } = "artifacts/resilience";
    public int DmsTimeoutSeconds { get; init; } = 60;
    public int DmsCheckIntervalSeconds { get; init; } = 5;
}

public sealed class LoggingConfig
{
    public string EventLogPath { get; init; } = "logs/execution_events.ndjson";
    public bool RotateDaily { get; init; } = true;
    public int RetainFiles { get; init; } = 7;
}

public sealed class ApiConfig
{
    public string ApiKey { get; init; } = string.Empty;
}

public sealed class StreamingConfig
{
    public int MetricsUpdateHz { get; init; } = 1;
}

namespace MonitoringDashboard.Models;

public class ServiceHealthReason
{
    /// <summary>Stable machine-readable code, e.g. "marketdata.price_stale".</summary>
    public string Code { get; init; } = "unknown";

    /// <summary>Human-readable explanation of why this service is not fully healthy.</summary>
    public string Summary { get; init; } = "";

    /// <summary>"warning" | "critical"</summary>
    public string Severity { get; init; } = "warning";

    /// <summary>Category for triage: connectivity, config, dependency, capacity, bug, external-provider, etc.</summary>
    public string Category { get; init; } = "unknown";

    /// <summary>What operators should do next. Read-only guidance in phase 1.</summary>
    public string RecommendedAction { get; init; } = "";

    /// <summary>Whether this likely can be fixed by operators, not a code bug.</summary>
    public bool IsUserActionable { get; init; }

    /// <summary>True when this likely indicates a product bug rather than runtime conditions.</summary>
    public bool BugSuspected { get; init; }
}

/// <summary>
/// Overall health + rich state for a single backing service.
/// </summary>
public class ServiceHealth
{
    /// <summary>Human-readable service name, e.g. "MarketData".</summary>
    public string Name { get; init; } = "";

    /// <summary>"online" | "degraded" | "offline"</summary>
    public string Status { get; init; } = "offline";

    /// <summary>One-line human-friendly description of the current state.</summary>
    public string Detail { get; init; } = "";

    /// <summary>UTC time of this check.</summary>
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Whether the MonitoringDashboard has an active SignalR connection to this service (where applicable).</summary>
    public bool? SignalRConnected { get; init; }

    /// <summary>Request-scoped correlation ID for joining UI diagnostics to backend logs.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Key service-specific metrics to surface in the diagnostics panel.</summary>
    public Dictionary<string, object?> Metrics { get; init; } = new();

    /// <summary>Structured reasons that explain degraded/offline states.</summary>
    public IReadOnlyList<ServiceHealthReason> Reasons { get; init; } = [];
}

/// <summary>
/// Aggregated system status returned by GET /api/system/status.
/// </summary>
public class SystemStatus
{
    public IReadOnlyList<ServiceHealth> Services { get; init; } = [];
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

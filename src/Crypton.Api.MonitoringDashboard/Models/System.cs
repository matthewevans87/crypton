namespace MonitoringDashboard.Models;

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

    /// <summary>Key service-specific metrics to surface in the diagnostics panel.</summary>
    public Dictionary<string, object?> Metrics { get; init; } = new();
}

/// <summary>
/// Aggregated system status returned by GET /api/system/status.
/// </summary>
public class SystemStatus
{
    public IReadOnlyList<ServiceHealth> Services { get; init; } = [];
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

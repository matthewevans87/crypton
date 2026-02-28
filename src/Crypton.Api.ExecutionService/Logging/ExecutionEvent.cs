using System.Text.Json.Serialization;

namespace Crypton.Api.ExecutionService.Logging;

/// <summary>
/// A single entry in the structured execution event log.
/// All events are serialized as NDJSON (one JSON object per line).
/// </summary>
public sealed class ExecutionEvent
{
    [JsonPropertyName("timestamp")]
    public required DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("mode")]
    public required string Mode { get; init; }  // "paper" | "live" | "safe" | "safe_idle"

    [JsonPropertyName("service_version")]
    public string ServiceVersion { get; init; } = typeof(ExecutionEvent).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    [JsonPropertyName("data")]
    public IReadOnlyDictionary<string, object?>? Data { get; init; }
}

/// <summary>Well-known event type constants.</summary>
public static class EventTypes
{
    public const string ServiceStarted = "service_started";
    public const string ServiceStopped = "service_stopped";
    public const string StrategyLoaded = "strategy_loaded";
    public const string StrategyRejected = "strategy_rejected";
    public const string StrategyExpired = "strategy_expired";
    public const string StrategySwapped = "strategy_swapped";
    public const string EntryTriggered = "entry_triggered";
    public const string EntrySkipped = "entry_skipped";
    public const string ExitTriggered = "exit_triggered";
    public const string ExitSkipped = "exit_skipped";
    public const string OrderPlaced = "order_placed";
    public const string OrderFilled = "order_filled";
    public const string OrderPartiallyFilled = "order_partially_filled";
    public const string OrderCancelled = "order_cancelled";
    public const string OrderRejected = "order_rejected";
    public const string PositionOpened = "position_opened";
    public const string PositionClosed = "position_closed";
    public const string PositionReconciled = "position_reconciled";
    public const string RiskLimitBreached = "risk_limit_breached";
    public const string SafeModeEntered = "safe_mode_entered";
    public const string SafeModeCleared = "safe_mode_cleared";
    public const string DmsTriggered = "dms_triggered";
    public const string DmsReset = "dms_reset";
    public const string RateLimitBackoffStarted = "rate_limit_backoff_started";
    public const string RateLimitBackoffEnded = "rate_limit_backoff_ended";
    public const string OperatorCommand = "operator_command";
    public const string ModePromoted = "mode_promoted";
    public const string ModeDemoted = "mode_demoted";
    public const string ModeChanged = "mode_changed";
    public const string ReconciliationSummary = "reconciliation_summary";
    public const string SafeModeActivated = "safe_mode_activated";
    public const string SafeModeDeactivated = "safe_mode_deactivated";
}

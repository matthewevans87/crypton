using AgentRunner.Domain;

namespace AgentRunner.Domain.Events;

/// <summary>Base type for all structured events emitted by the agent runner.</summary>
public abstract record AgentEvent;

// ─── Orchestration-level events ───────────────────────────────────────────────

/// <summary>Fired when an agent step begins executing.</summary>
public sealed record StepStartedEvent(
    string StepName,
    string CycleId,
    DateTimeOffset StartedAt) : AgentEvent;

/// <summary>Fired when an agent step finishes (success or failure).</summary>
public sealed record StepCompletedEvent(
    string StepName,
    string CycleId,
    bool Success,
    string? ErrorMessage,
    TimeSpan Duration) : AgentEvent;

/// <summary>Fired once all steps in a cycle complete successfully.</summary>
public sealed record CycleCompletedEvent(
    string CycleId,
    DateTimeOffset CompletedAt) : AgentEvent;

/// <summary>Fired on every state machine transition.</summary>
public sealed record LoopStateChangedEvent(
    LoopState State,
    string? CycleId) : AgentEvent;

/// <summary>Fired by the health monitor when a stall or anomaly is detected.</summary>
public sealed record LoopHealthEvent(
    string Message,
    LoopState State,
    bool IsCritical) : AgentEvent;

/// <summary>Fired when an unrecoverable error causes the loop to enter the Failed state.</summary>
public sealed record LoopErrorEvent(
    string Message,
    string? CycleId) : AgentEvent;

// ─── Execution-level events ────────────────────────────────────────────────────

/// <summary>Fired for each streaming token received from the LLM.</summary>
public sealed record TokenReceivedEvent(
    string Token,
    string StepName) : AgentEvent;

/// <summary>
/// Fired just before a tool is invoked. Carries the full serialised input parameters.
/// The paired <see cref="ToolCallCompletedEvent"/> carries only output — the UI must merge on <see cref="Id"/>.
/// </summary>
public sealed record ToolCallStartedEvent(
    Guid Id,
    string ToolName,
    string InputJson,
    string StepName) : AgentEvent;

/// <summary>
/// Fired after a tool invocation completes. Does not repeat the input — merge with
/// <see cref="ToolCallStartedEvent"/> using <see cref="Id"/>.
/// </summary>
public sealed record ToolCallCompletedEvent(
    Guid Id,
    string ToolName,
    string Output,
    TimeSpan Duration,
    bool IsError,
    string StepName) : AgentEvent;

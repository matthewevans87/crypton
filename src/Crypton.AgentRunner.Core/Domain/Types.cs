namespace AgentRunner.Domain;

/// <summary>Input passed to <see cref="Abstractions.IAgentExecutor.ExecuteAsync"/>. Pure data — no methods.</summary>
public sealed record AgentInput(
    string AgentName,
    string CycleId,
    string SystemPrompt,
    string UserPrompt,
    IReadOnlyList<string> AvailableTools);

/// <summary>Result returned by <see cref="Abstractions.IAgentExecutor.ExecuteAsync"/>.</summary>
public sealed record AgentOutput(
    bool Success,
    string? Output,
    string? Error,
    int Iterations);

/// <summary>Static definition of an agent's context rules. Registered as DI singleton keyed by <see cref="LoopState"/>.</summary>
public sealed record AgentStateDefinition(
    string AgentName,
    string PromptFile,
    string TemplateFile,
    string[] InputArtifacts,
    string[] AvailableTools,
    bool IncludeMemory = true,
    bool IncludeRecentEvaluations = false,
    int RecentEvaluationCount = 0);

/// <summary>Running context for a single cycle.</summary>
public sealed record CycleContext
{
    public required string CycleId { get; init; }
    public DateTimeOffset StartTime { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? EndTime { get; init; }
    public LoopState CurrentState { get; init; } = LoopState.Idle;
    public IReadOnlyDictionary<LoopState, StepRecord> Steps { get; init; } =
        new Dictionary<LoopState, StepRecord>();
    public int RestartCount { get; init; }
    public bool IsPaused { get; init; }
    public DateTimeOffset? PausedAt { get; init; }
    public string? PauseReason { get; init; }
}

/// <summary>Record of a single step execution within a cycle.</summary>
public sealed record StepRecord(
    LoopState Step,
    DateTimeOffset StartTime,
    DateTimeOffset? EndTime = null,
    StepOutcome Outcome = StepOutcome.Success,
    string? ErrorMessage = null);

/// <summary>A message deposited into an agent's mailbox.</summary>
public sealed record MailboxMessage(
    string FromAgent,
    string ToAgent,
    string Content,
    DateTimeOffset Timestamp);

/// <summary>Result of a startup validation pass.</summary>
public sealed record StartupValidationResult(
    bool IsValid,
    bool IsDegraded,
    IReadOnlyList<string> Errors);

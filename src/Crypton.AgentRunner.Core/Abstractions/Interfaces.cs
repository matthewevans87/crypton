using AgentRunner.Configuration;
using AgentRunner.Domain;
using AgentRunner.Domain.Events;
using Microsoft.Extensions.AI;

namespace AgentRunner.Abstractions;

/// <summary>
/// Executes a single agent invocation against the LLM. Receives a pre-built <see cref="AgentInput"/>
/// and publishes structured events to the per-call <paramref name="sink"/>. The orchestrator
/// is entirely blind to LLM or tool details.
/// </summary>
public interface IAgentExecutor
{
    Task<AgentOutput> ExecuteAsync(AgentInput input, IAgentEventSink sink, CancellationToken ct = default);
}

/// <summary>
/// Drives the full learning loop: Plan → Research → Analyze → Synthesize → Evaluate → wait → repeat.
/// Owns lifecycle (start/stop/pause/resume) and publishes all orchestration events.
/// </summary>
public interface ICycleOrchestrator
{
    Task StartAsync(CancellationToken ct = default);
    Task StopAsync();
    Task PauseAsync(string? reason = null);
    Task ResumeAsync();
    Task AbortAsync();
    void ForceNewCycle();
    void InjectContext(string agentName, string content);

    LoopState CurrentState { get; }
    CycleContext? CurrentCycle { get; }
    int RestartCount { get; }
    DateTimeOffset NextScheduledAt { get; }
}

/// <summary>Thread-safe state machine governing valid loop state transitions.</summary>
public interface ILoopStateMachine
{
    LoopState CurrentState { get; }
    bool CanTransitionTo(LoopState newState);
    bool TransitionTo(LoopState newState);

    event EventHandler<StateTransitionEventArgs> StateTransition;
    event EventHandler<LoopState> StateEntered;
    event EventHandler<LoopState> StateExited;
}

/// <summary>Builds a fully-formed <see cref="AgentInput"/> for the given loop state and cycle.</summary>
public interface IAgentContextProvider
{
    AgentInput BuildContext(LoopState state, string cycleId, string? previousCycleId = null);
}

// ─── Storage ──────────────────────────────────────────────────────────────────

/// <summary>File-system artifact store for cycle outputs, memory, and evaluations.</summary>
public interface IArtifactStore
{
    string CreateCycleDirectory();
    void Save(string cycleId, string name, string content);
    string? Read(string cycleId, string name);
    bool Exists(string cycleId, string name);
    string? ReadMemory(string agentName);
    string? ReadSharedMemory();
    IReadOnlyList<string> GetRecentEvaluations(int count);
    string? GetLatestCompletedCycleId();
    IReadOnlyList<string> GetRecentCycleIds(int count);
    IReadOnlyList<string> GetCycleArtifactNames(string cycleId);
}

/// <summary>Per-agent mailbox — delivers short messages between agents in the same cycle.</summary>
public interface IMailboxService
{
    IReadOnlyList<MailboxMessage> GetMessages(string agentName, int maxCount);
    void Send(MailboxMessage message);
}

/// <summary>Persists loop state across process restarts.</summary>
public interface IStatePersistence
{
    Task<(LoopState State, CycleContext? Cycle)?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(LoopState state, CycleContext? cycle, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
}

// ─── Scheduling & health ──────────────────────────────────────────────────────

/// <summary>Determines when the next cycle is due and supports forced advancement.</summary>
public interface ICycleScheduler
{
    bool IsDue(DateTimeOffset lastCycleEnd);
    TimeSpan TimeUntilNext(DateTimeOffset lastCycleEnd);
    void ForceNext();
    DateTimeOffset NextScheduledAt { get; }
}

// ─── Execution-layer ──────────────────────────────────────────────────────────

/// <summary>A single named capability a LLM agent can invoke.</summary>
public interface IAgentTool
{
    string Name { get; }
    string Description { get; }
    AIFunction AsAIFunction();
}

/// <summary>
/// Returns the <see cref="AIFunction"/> list for a given agent, with telemetry decoration.
/// The <paramref name="sink"/> is per-call so each invocation captures events independently.
/// </summary>
public interface IToolProvider
{
    IReadOnlyList<AIFunction> GetFunctionsForAgent(
        string agentName,
        IReadOnlyList<string> allowedToolNames,
        IAgentEventSink sink);
}

/// <summary>Wraps a tool invocation with retry, circuit-breaker, and timeout logic.</summary>
public interface IToolExecutor
{
    Task<T> ExecuteWithResilienceAsync<T>(
        string toolName,
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct = default);
}

// ─── Infrastructure ───────────────────────────────────────────────────────────

/// <summary>Typed event bus. Implementations fan out to SignalR, console, or test capture.</summary>
public interface IAgentEventSink
{
    void Publish(AgentEvent evt);
}

/// <summary>Validates all external dependencies before the loop starts.</summary>
public interface IStartupValidator
{
    Task<StartupValidationResult> ValidateAsync(AgentRunnerConfig config, CancellationToken ct = default);
}

/// <summary>Structured event logging to disk (rolling text log + per-cycle JSONL journal).</summary>
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
    void LogRetryAttempt(string agentName, string step, int attempt, int maxAttempts);
    void LogPromptSnapshot(string agentName, string cycleId, string systemPrompt, string userPrompt);
    void LogToolCallJournal(string agentName, string cycleId, int iteration, string toolName, string parameters, string result, long durationMs);
    void LogInvocationManifest(string agentName, string cycleId, InvocationManifest manifest);
}

/// <summary>Summary record written to {agent}_run_manifest.json after every agent invocation.</summary>
public sealed record InvocationManifest(
    string Model,
    double Temperature,
    int NumCtx,
    int IterationsUsed,
    int MaxIterations,
    long DurationMs,
    bool Success,
    string? Error);

/// <summary>Event args for state machine transitions.</summary>
public sealed record StateTransitionEventArgs(LoopState From, LoopState To, DateTimeOffset Timestamp);

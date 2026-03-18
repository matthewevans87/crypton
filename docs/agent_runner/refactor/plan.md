# AgentRunner Refactor Plan

**Status:** Complete  
**Scope:** `src/Crypton.Api.AgentRunner` and `src/Crypton.Api.AgentRunner.Tests` only  
**Goal:** Improve readability and correctness. No behaviour changes. No new features.

---

## TL;DR

`AgentRunnerService` is a ~700-line god class mixing lifecycle control, step execution,
cycle scheduling, mailbox routing, and event publishing. This refactor decomposes it into
focused single-responsibility classes, fixes two correctness bugs, and eliminates duplicated
patterns in `AgentContextBuilder`. All existing behaviour is preserved exactly.

---

## Phase 1 — Correctness Fixes ✅

These are independent of each other and can be done in any order.

### 1.1 — Atomic state persistence ✅
**File:** `StateMachine/StatePersistence.cs`  
**Problem:** `state.json` is written in-place. An unclean shutdown mid-write corrupts the file
and prevents resumption.  
**Fix:** Write to a temp file (e.g. `state.json.tmp`) then `File.Move` with `overwrite: true`.
This is an atomic OS-level rename on Linux (single filesystem). No format change.

### 1.2 — Swallowed compression exceptions ✅
**File:** `Artifacts/ArtifactManager.cs`, method `CompressOldCycles()`  
**Problem:** Exceptions from `ZipFile.CreateFromDirectory` are caught and silently discarded.
Failed archiving produces orphaned partial `.zip` files with no indication of failure.  
**Fix:** Clean up partial zip on failure, then re-throw as `InvalidOperationException` so
the caller decides whether to log and continue or propagate.

### 1.3 — Dead code in AgentInvoker ✅
**File:** `Agents/AgentInvoker.cs`  
**Problem:** `Math.Max(config.Ollama.TimeoutSeconds, 30)` — the config already enforces minimums
at startup validation, so this guard is dead code that obscures intent.  
**Fix:** Remove the `Math.Max` wrapper; use `config.Ollama.TimeoutSeconds` directly.

---

## Phase 2 — Decompose `AgentRunnerService` ✅

Steps 2a and 2b can proceed in parallel. 2c depends on 2a being done first. 2d is independent.
2e is the final assembly step and depends on all prior steps.

### 2a — Extract `CycleStepExecutor` ✅
**New file:** `Agents/CycleStepExecutor.cs`

```csharp
public sealed record StepExecutionResult(
    StepOutcome Outcome,
    string? ErrorMessage,
    DateTime StartTime,
    DateTime EndTime);

public class CycleStepExecutor
{
    Task<StepExecutionResult> ExecuteAsync(
        LoopState state,
        CycleContext cycle,
        string? latestPreviousCycleId,
        Action<string>? onToken,
        Action<string>? onAgentEvent,
        CancellationToken ct);
}
```

Absorbs from `AgentRunnerService`:
- `ExecuteStepAsync` — the step dispatch logic
- `ExecuteAgentAsync` — context build + LLM invoke + artifact save
- `ValidateArtifact` — artifact validation logic  
- `GetArtifactNameForState` — static `LoopState → string` lookup table

**New interface:** `IAgentInvoker` and `IAgentContextBuilder` added to support unit testing.
`AgentInvoker` and `AgentContextBuilder` implement their respective interfaces.

**Key benefit:** `CycleContext` is always an explicit parameter — eliminates all
`if (_currentCycle != null)` null guard scatter in the current service.

**DI registration:** singleton in `Program.cs`.

### 2b — Extract `MailboxRouter` ✅
**New file:** `Mailbox/MailboxRouter.cs`

```csharp
public class MailboxRouter
{
    Task RouteAsync(LoopState state, AgentInvocationResult result);
}
```

Absorbs from `AgentRunnerService`:
- `HandleMailboxMessagesAsync`
- `GetMailboxRouting` — `LoopState → (forwardAgent, backwardAgent)` static lookup
- `ExtractMailboxContent`, `ExtractFeedbackContent`, `ExtractBroadcastContent`

`MailboxManager` is constructor-injected. `MailboxRouter` is injected into `CycleStepExecutor`.

**DI registration:** singleton.

### 2c — Extract `CycleScheduler` ✅
**New file:** `Agents/CycleScheduler.cs`

```csharp
public class CycleScheduler
{
    DateTime NextScheduledRunTime { get; }
    Task<(LoopState NextState, string NewCycleId)> WaitAsync(
        CycleContext? completedCycle,
        string? latestPreviousCycleId,
        Action<string>? onCycleCompleted,
        CancellationToken ct);
    void ForceNextCycle();
}
```

Absorbs from `AgentRunnerService`:
- `HandleNextCycleDelayAsync` — short-polling tick loop, interval re-read each tick

**Force-cycle mechanism:** `CycleScheduler` holds a private
`TaskCompletionSource<bool>? _forceSignal` reset on each `WaitAsync` call. `ForceNextCycle()`
calls `_forceSignal?.TrySetResult(true)` to interrupt the wait. The public `ForceNewCycle()`
on `AgentRunnerService` stays `void` and calls `_scheduler.ForceNextCycle()`.

**DI registration:** singleton.

### 2d — Extract `LoopRestartManager` ✅
**New file:** `Agents/LoopRestartManager.cs`

```csharp
public class LoopRestartManager
{
    // Returns false if max restarts exceeded (service should stop).
    // Returns true after applying exponential backoff delay.
    Task<bool> ShouldRestartAsync(int restartCount, CancellationToken ct);
}
```

Absorbs from `AgentRunnerService`:
- `HandleLoopExitAsync` — restart count check + exponential backoff

**DI registration:** singleton.

### 2e — Slim `AgentRunnerService` ✅
After extraction, `AgentRunnerService` becomes a **lifecycle facade only**:
- Public surface: `StartAsync`, `StopAsync`, `IsRunning`, `Pause`, `Resume`, `AbortAsync`,
  `ForceNewCycle`, `InjectContext`
- All lifecycle events remain on this class (hub broadcaster subscribes to them)
- `RunLoopAsync` shrinks to ~60 lines with no inline health-check blocks or timer arithmetic:

```
while not cancelled:
  CheckHealthAndRecoverIfCritical()
  if WaitingForNextCycle: await HandleWaitingForNextCycleAsync(ct); continue
  if ExceedsMaxCycleDuration(_currentCycle): await ForceTimeoutToEvaluateAsync(ct); continue
  nextState = _stateMachine.GetNextRequiredState()
  if cannot transition: if terminal, wait 1 min; else break
  _stateMachine.TransitionTo(nextState)
  await ExecuteCurrentStepAsync(ct)
  await _persistence.SaveStateAsync(...)
await HandleLoopExitAsync(ct)
```

---

## Phase 3 — Consolidate `AgentContextBuilder` ✅

**File:** `Agents/AgentContextBuilder.cs`

**Problem:** 5 near-identical `Build{Agent}ContextAsync` methods with copy-paste structure.

**Fix:** Replace with a single method:

```csharp
AgentContext BuildContext(LoopState state, string cycleId, string? previousCycleId = null);
```

A private static lookup maps each `LoopState` to its context definition:

```csharp
private record AgentContextDefinition(
    string AgentName,
    string PromptFile,
    string TemplateFile,
    string[] InputArtifacts,
    string[] AvailableTools,
    bool IncludeAgentMemory = true,
    bool IncludeRecentEvaluations = false,
    int RecentEvaluationCount = 0);
```

Evaluation's special case (reads from `previousCycleId`, not `cycleId`) is handled via the
`previousCycleId` parameter inside the single method body.

The old `Build{Agent}AgentContext` methods are removed. `CliRunner.cs` and `CycleStepExecutor`
are updated to use `BuildContext`.

---

## Phase 5 — Test Coverage ✅

All new classes require unit tests.

### New test files

**`Mailbox/MailboxRouterTests.cs`**
- `RouteAsync_PlanState_DepositsToResearchMailbox`
- `RouteAsync_EvaluateState_Broadcasts`
- `RouteAsync_FailedResult_DepositsNothing`
- `RouteAsync_EmptyOutput_DepositsNothing`

**`Agents/LoopRestartManagerTests.cs`**
- `ShouldRestartAsync_BelowMaxCount_ReturnsTrue`
- `ShouldRestartAsync_AtMaxCount_ReturnsFalse`

**`Agents/CycleSchedulerTests.cs`**
- `WaitAsync_ForceSignal_ReturnsBeforeInterval`
- `WaitAsync_NoPreviousCycle_ReturnsPlan`
- `WaitAsync_WithPreviousCycle_ReturnsEvaluate`

**`Agents/CycleStepExecutorTests.cs`**
- `ExecuteAsync_SuccessfulAgent_ReturnSuccess`
- `ExecuteAsync_FailedAgent_ReturnsFailed`

---

## Relevant Files

| File                                                                  | Change                                             |
| --------------------------------------------------------------------- | -------------------------------------------------- |
| `src/Crypton.Api.AgentRunner/Agents/AgentRunnerService.cs`            | Major — decomposed to lifecycle facade             |
| `src/Crypton.Api.AgentRunner/Agents/AgentContextBuilder.cs`           | Consolidate 5 builders to 1 method                 |
| `src/Crypton.Api.AgentRunner/Agents/AgentInvoker.cs`                  | Remove dead `Math.Max`; implements `IAgentInvoker` |
| `src/Crypton.Api.AgentRunner/StateMachine/StatePersistence.cs`        | Atomic file write                                  |
| `src/Crypton.Api.AgentRunner/Artifacts/ArtifactManager.cs`            | Fix swallowed compression exceptions               |
| `src/Crypton.Api.AgentRunner/Agents/CycleStepExecutor.cs`             | **New**                                            |
| `src/Crypton.Api.AgentRunner/Agents/CycleScheduler.cs`                | **New**                                            |
| `src/Crypton.Api.AgentRunner/Agents/LoopRestartManager.cs`            | **New**                                            |
| `src/Crypton.Api.AgentRunner/Mailbox/MailboxRouter.cs`                | **New**                                            |
| `src/Crypton.Api.AgentRunner/Cli/CliRunner.cs`                        | Update to use `BuildContext`                       |
| `src/Crypton.Api.AgentRunner/Program.cs`                              | Register 4 new singletons                          |
| `src/Crypton.Api.AgentRunner.Tests/Agents/CycleStepExecutorTests.cs`  | **New**                                            |
| `src/Crypton.Api.AgentRunner.Tests/Agents/CycleSchedulerTests.cs`     | **New**                                            |
| `src/Crypton.Api.AgentRunner.Tests/Agents/LoopRestartManagerTests.cs` | **New**                                            |
| `src/Crypton.Api.AgentRunner.Tests/Mailbox/MailboxRouterTests.cs`     | **New**                                            |

---

## Verification Checklist

- [ ] `build: Crypton.Api.AgentRunner` passes with zero warnings after each phase
- [ ] `test: Crypton.Api.AgentRunner.Tests` passes fully after Phase 5
- [ ] `dotnet run -- --cli status` returns clean output
- [ ] `dotnet run -- --cli run-step --step plan --verbose` executes without regression
- [ ] `LoopStateMachineTests.cs` passes unchanged (state machine not touched)
- [ ] No `[ExcludeFromCodeCoverage]` added to any business logic

---

## Scope Boundaries

**In scope**
- `Crypton.Api.AgentRunner` service layer
- `Crypton.Api.AgentRunner.Tests`

**Out of scope**
- All other services (ExecutionService, MarketData, MonitoringDashboard)
- Svelte frontend
- Strategy DSL
- Existing REST API surface and SignalR hubs (preserved as-is)
- `IEventLogger` → `ILogger<T>` migration (separate concern)
- `ArtifactManager.SearchMemory()` O(n) performance (separate concern)
- The evaluation-before-plan flow ambiguity (pre-existing behaviour, separate concern)

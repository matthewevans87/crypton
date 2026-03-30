# Crypton.AgentRunner.Core — Backlog

## Stall Detection

`StallWarningMinutes` and `StallCriticalMinutes` are present in `ResilienceConfig` and both the console and SignalR sinks handle `LoopHealthEvent`, but nothing ever fires it for a stall.

**Required:** a background monitoring task that runs concurrently with each step execution and publishes `LoopHealthEvent` when a step exceeds the warning/critical thresholds.

Sketch:
- In `ExecuteCurrentStepAsync`, start a `Task.Run` monitor alongside the step `Task`.
- At `StallWarningMinutes`, publish `new LoopHealthEvent("Step {state} stalled ({elapsed} min)", state, IsCritical: false)` and call `_logger.LogWarning(...)`.
- At `StallCriticalMinutes`, publish with `IsCritical: true` and call `_logger.LogError(...)`.
- Cancel the monitor task when the step completes normally.

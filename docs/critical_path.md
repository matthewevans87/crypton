> **Minimum viable outcome:**
> 1. The agentic loop works, or at least produces _some_ strategy (even if its not a good one).
> 2. The execution service picks up the strategy and starts running it in paper trading mode.
> 3. Market data telemetry is (WebSocket) live-streamed to the dashboard.
> 4. Execution service telemetry is live-streamed (WebSocket) to the dashboard.

---

## Status Key

- ✅ Implemented and working
- ⚠️ Partially implemented / has a known defect
- ❌ Not implemented

---

## Goal 1 — The agentic loop produces a strategy

The state machine, agent context building, LLM invocation, tool execution, artifact system, and
mailbox routing are all fully implemented. The loop runs Plan → Research → Analyze → Synthesize
and writes a validated `strategy.json` to disk each cycle. However, the loop **crashes on every
cycle** immediately after Synthesize due to an unimplemented state, preventing it from completing.

### 1a — Core loop correctness

| #    | Item                                                                                                                                                                                                                                                                                                                                                                                                                                                            | Status |
| ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 1a.1 | State machine, agent context builders, multi-turn LLM invocation, tool execution, artifact system, mailbox routing, per-agent/shared memory, loop resilience                                                                                                                                                                                                                                                                                                    | ✅      |
| 1a.2 | Tools: `web_search`, `web_fetch`, `bird`, `current_position`, `technical_indicators`                                                                                                                                                                                                                                                                                                                                                                            | ✅      |
| 1a.3 | API for runtime control: pause, abort, force-cycle, inject context                                                                                                                                                                                                                                                                                                                                                                                              | ✅      |
| 1a.4 | **`LoopState.Execute` is a named state with no implementation.** `AgentRunnerService`, `AgentContextBuilder`, `ArtifactValidators`, and `GetArtifactNameForState` all throw `InvalidOperationException` for Execute. Every cycle crashes after Synthesize and enters the restart loop. **Fix:** remove `Execute` from the transition table and connect Synthesize → Evaluate directly. The actual execution hand-off is a file drop (Goal 2), not a loop state. | ❌      |
| 1a.5 | **No `/api/override/resume` endpoint.** `AgentRunnerService.Resume()` is implemented but no HTTP route calls it. A paused loop cannot be unpaused externally.                                                                                                                                                                                                                                                                                                   | ❌      |
| 1a.6 | **`agent_prompts/` and `output_templates/` files must exist** for the LLM to receive meaningful context. If any file is absent the agent silently receives no identity or output template. Verify all five prompt files (`plan`, `research`, `analyze`, `synthesis`, `evaluation`) and all output templates are present and well-formed.                                                                                                                        | ⚠️      |
| 1a.7 | **`ExecuteParallelResearchAndAnalysisAsync` is sequential**, not parallel, and records incorrect `StepRecord` timestamps (Research start hardcoded to `UtcNow - 5 minutes`).                                                                                                                                                                                                                                                                                    | ⚠️      |

### 1b — Evaluation as Step 0

The intended behaviour is: Evaluate runs **before** Plan each cycle, but only when there is a
previous cycle's `strategy.json` and execution telemetry to assess. On the very first cycle
(no historical data) Evaluation must be skipped and the loop should jump straight to Plan.

The current state machine hardcodes Evaluate at the **end**:
`Execute → Evaluate → WaitingForNextCycle → Plan`

This means the first-run skip is absent (Evaluate fires even with no history), Evaluation output
cannot inform the Planning step that follows it in the same cycle, and there is no transition
from `WaitingForNextCycle → Evaluate`.

| #    | Item                                                                                                                                                                                                                                                                                                            | Status |
| ---- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 1b.1 | **Redesign the state machine cycle order** to: `WaitingForNextCycle → Evaluate (if history exists) → Plan → Research → Analyze → Synthesize → WaitingForNextCycle`. Remove the `Execute` state. Add a `WaitingForNextCycle → Evaluate` transition and keep `WaitingForNextCycle → Plan` as the first-run path.  | ❌      |
| 1b.2 | **Add "has previous cycle" guard in `AgentRunnerService`.** When transitioning out of `WaitingForNextCycle`, check whether a prior `strategy.json` and `evaluation.md` exist in the artifact store. If yes, proceed to Evaluate; if no (first run), skip directly to Plan.                                      | ❌      |
| 1b.3 | **Update `AgentContextBuilder.BuildEvaluationAgentContext`** to read the _previous_ cycle's artifacts (`strategy.json`, `analysis.md`) and any available execution telemetry from the portfolio tool, so the Evaluation agent has the data it needs at cycle start rather than relying on same-cycle artifacts. | ❌      |
| 1b.4 | **`GetNextScheduledRunTime()` in `StatusController` is hardcoded** to `UtcNow + 6 hours`. Should read from `ScheduleIntervalMinutes` / `Schedule` config.                                                                                                                                                       | ⚠️      |

### 1c — Live-configurable cycle interval

The cycle interval should be adjustable from the dashboard without restarting any service.

| #    | Item                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  | Status |
| ---- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 1c.1 | `POST /api/config/cycle-interval` on AgentRunner validates and updates `_config.Cycle.ScheduleIntervalMinutes` in-process.                                                                                                                                                                                                                                                                                                                                                                            | ✅      |
| 1c.2 | **The wait does not respond to mid-sleep interval changes.** `HandleNextCycleDelayAsync` calls `Task.Delay(interval, ct)` with the value captured at the start of the wait. A config update during a 6-hour sleep has no effect until the next cycle begins. Fix: replace the single `Task.Delay` with a short-polling loop (e.g. check every 30 s) that re-reads `_config.Cycle.ScheduleIntervalMinutes` on each tick, so a change from the dashboard takes effect within seconds.                   | ❌      |
| 1c.3 | **MonitoringDashboard has no endpoint or UI to set the cycle interval.** The AgentRunner `POST /api/config/cycle-interval` exists but there is no proxy route in `Crypton.Api.MonitoringDashboard` and no control widget in `Crypton.Web.Dashboard`. Fix: add `POST /api/agent/config/cycle-interval` to `AgentController`, proxy it to AgentRunner, and add an interval picker (e.g. in `LoopStatePanel`) in the web dashboard. This shares the `AgentRunnerClient` dependency with items 4c.2–4c.4. | ❌      |

### 1d — Ollama configuration

| #    | Item                                                                                                                                                                                                                                                                                                                                                                                                                     | Status |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------ |
| 1d.1 | Per-agent model name is configurable via `appsettings.json` (`Agents.{Name}.Model`).                                                                                                                                                                                                                                                                                                                                     | ✅      |
| 1d.2 | **Ollama base URL is hardcoded** to `http://localhost:11434/api/chat` in `AgentInvoker.CallLlmAsync`. There is no `Ollama` section in `AgentRunnerConfig` or `appsettings.json`. Fix: add an `OllamaConfig` class with at minimum `BaseUrl` (default `http://localhost:11434`) and bind it under `AgentRunnerConfig.Ollama`; read `_config.Ollama.BaseUrl` in `AgentInvoker` instead of the hardcoded string.            | ❌      |
| 1d.3 | **Ollama is not in `docker-compose.yml`.** The AgentRunner container cannot reach `localhost:11434` unless Ollama is on the host and exposed via `host-gateway`. Fix: either add an `ollama` service to `docker-compose.yml` (with a GPU-passthrough device profile for production) or add `Tools__Ollama__BaseUrl=http://host-gateway:11434` to the `agent-runner` environment block and document the host requirement. | ❌      |
| 1d.4 | **No per-agent Ollama override.** All agents share one base URL. Consider allowing a per-agent `BaseUrl` field in `AgentSettings` once the base config exists, to support routing lighter/heavier models to different endpoints.                                                                                                                                                                                         | ⚠️      |

---

## Goal 2 — Execution service picks up the strategy and runs paper trading

The Execution Service is structurally complete: paper trading adapter, position registry, entry/exit
evaluators, order router, strategy validator, and the full API surface are all implemented. There
are two critical gaps that prevent any order from ever executing in paper mode.

### 2a — Strategy handoff from AgentRunner to ExecutionService

| #    | Item                                                                                                                                                                                                                                                                                                                                                                                                                                                                                             | Status |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------ |
| 2a.1 | `StrategyService` uses a `FileSystemWatcher` to hot-reload `strategy.json` from a configurable path. Validation and DSL pre-compilation on load are fully implemented.                                                                                                                                                                                                                                                                                                                           | ✅      |
| 2a.2 | **Shared volume path mismatch in `docker-compose.yml`.** `agent-runner` mounts to `/home/matthew-evans/.config/crypton/artifacts` and `execution-service` mounts to `/home/matthew-evans/.config/crypton/execution/artifacts`. They are different host paths, so `strategy.json` written by AgentRunner is never visible to ExecutionService. Fix: use a single named volume (e.g. `crypton_artifacts`) mounted at the same container path in both services, or align the host bind-mount paths. | ❌      |
| 2a.3 | **`ExecutionServiceConfig.BaseUrl` is registered in DI but never used.** The file-watch approach is the intended handoff mechanism (no HTTP push from AgentRunner to ExecutionService). This config key should be removed from `AgentRunnerConfig` to avoid confusion, or repurposed if a push notification on strategy write is ever added.                                                                                                                                                     | ⚠️      |

### 2b — Market data reaching the paper trading adapter

| #    | Item                                                                                                                                                                                                                                                                                                                                                                                                                                  | Status |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 2b.1 | `PaperTradingAdapter` fill simulation (mid-price ± slippage, commission, synchronous ACK)                                                                                                                                                                                                                                                                                                                                             | ✅      |
| 2b.2 | `DelegatingExchangeAdapter` routes orders to paper adapter in paper mode                                                                                                                                                                                                                                                                                                                                                              | ✅      |
| 2b.3 | `OperationModeService` persists paper/live state and exposes mode-change events                                                                                                                                                                                                                                                                                                                                                       | ✅      |
| 2b.4 | **`IMarketDataSource` is wired to `NullMarketDataSource`**, which never emits snapshots. `PaperTradingAdapter` requires a cached `MarketSnapshot` to fill orders; without one it rejects every order with `"no_market_data"`. Fix: implement a real `IMarketDataSource` that connects to the `Crypton.Api.MarketData` SignalR hub (`/hubs/marketdata`) and populates the paper adapter's snapshot cache on each `OnPriceUpdate` tick. | ❌      |
| 2b.5 | **`DelegatingExchangeAdapter.SetLiveAdapters` is never called at startup.** `KrakenWebSocketAdapter` and `KrakenRestAdapter` are constructed manually inside `SetLiveAdapters` rather than being DI-registered and injected. Live mode is unreachable at runtime. (Not critical for paper mode, but the wiring must exist before live promotion is possible.)                                                                         | ⚠️      |

### 2c — Execution correctness gaps

| #    | Item                                                                                                                                                                                                                                 | Status |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------ |
| 2c.1 | Entry/exit evaluators, position sizing, portfolio risk enforcement                                                                                                                                                                   | ✅      |
| 2c.2 | `ReconciliationService` post-restart position sync                                                                                                                                                                                   | ✅      |
| 2c.3 | `SafeModeController` — market-sell all on activation                                                                                                                                                                                 | ✅      |
| 2c.4 | **`MetricsCollector` counters are never incremented.** `OrderRouter`, `EntryEvaluator`, and `ExitEvaluator` do not call any `_metrics.Record*()` method. `GET /metrics` and the SignalR `MetricsUpdate` stream always return zeroes. | ❌      |
| 2c.5 | **`ExecutionEngine.CurrentMode` is hardcoded to `"paper"`.** `OperationModeService` is never queried, so the event log incorrectly labels all events as `"paper"` even after live promotion.                                         | ⚠️      |
| 2c.6 | **Short-sell positions are not opened in `PositionRegistry`.** `OrderRouter.ApplyFillAsync` only calls `OpenPosition(...)` for `Buy` fills. A filled short-direction order leaves the registry empty for that position.              | ⚠️      |

---

## Goal 3 — Market data telemetry live-streamed to the dashboard

This goal is the most complete. The full pipeline from Kraken WebSocket → MarketData SignalR hub
→ MonitoringDashboard bridge → DashboardHub → Web Dashboard is wired and working for price ticks.

| #   | Item                                                                                                                                                                                                                                                                        | Status |
| --- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 3.1 | `KrakenExchangeAdapter` subscribes to `ticker`, `book`, and `trade` channels via `wss://ws.kraken.com` with auto-reconnect and circuit breaker                                                                                                                              | ✅      |
| 3.2 | `MarketDataHub` (`/hubs/marketdata`) broadcasts `OnPriceUpdate`, `OnOrderBookUpdate`, `OnTrade`, `OnBalanceUpdate`, `OnConnectionStatus`                                                                                                                                    | ✅      |
| 3.3 | `MonitoringDashboard` connects to MarketData SignalR on startup and bridges `OnPriceUpdate` → `DashboardHub.PriceUpdated`                                                                                                                                                   | ✅      |
| 3.4 | Web Dashboard `signalr.ts` subscribes to `DashboardHub` and delivers price ticks to `PriceTickerPanel` / `PriceChartPanel`                                                                                                                                                  | ✅      |
| 3.5 | REST proxy endpoints for prices, indicators, OHLCV, portfolio summary, holdings are live (no stubs)                                                                                                                                                                         | ✅      |
| 3.6 | **`OnOrderBookUpdate` and `OnTrade` events are not bridged** from MarketData hub to `DashboardHub`. The MonitoringDashboard's `Program.cs` only subscribes to `OnPriceUpdate`. If the dashboard needs order-book or trade-tape panels, these events must also be forwarded. | ⚠️      |
| 3.7 | **Macro signals endpoint (`GET /api/market/macro`) is fully hardcoded** (Fear & Greed = 65, BTC dominance = 52.3%). No live data source is connected.                                                                                                                       | ⚠️      |

---

## Goal 4 — Execution service telemetry live-streamed to the dashboard

The Execution Service SignalR infrastructure (`ExecutionHub` + `ExecutionHubBroadcaster`) is fully
implemented and pushes five real-time streams. The MonitoringDashboard has no connection to it
at all; every agent, strategy, performance, and execution panel in the Web Dashboard is fed by
hardcoded stubs.

### 4a — Connect MonitoringDashboard to ExecutionService

| #    | Item                                                                                                                                                                                                                                                                                                                                                                                                          | Status |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 4a.1 | `ExecutionHub` (`/hubs/execution`) with `StatusUpdate` (2 s), `MetricsUpdate`, `EventLog`, and `PositionUpdate` streams fully implemented                                                                                                                                                                                                                                                                     | ✅      |
| 4a.2 | `ExecutionHubBroadcaster` hosted service wires position, event, and timer callbacks to hub pushes                                                                                                                                                                                                                                                                                                             | ✅      |
| 4a.3 | **MonitoringDashboard has no `ExecutionServiceClient`** and no connection to the ExecutionService. Add an `ExecutionServiceClient` (analogous to `MarketDataServiceClient`) that: connects to `ExecutionHub`, subscribes to all four streams, and raises events for the dashboard layer to consume. The ExecutionService URL must also be injected via config and `docker-compose.yml` environment variables. | ❌      |
| 4a.4 | **`docker-compose.yml` does not wire `MonitoringDashboard` to `ExecutionService`.** No `ExecutionService__Url` environment variable or `depends_on` entry exists.                                                                                                                                                                                                                                             | ❌      |

### 4b — Wire live data to DashboardHub and backend controllers

| #    | Item                                                                                                                                                                                                                                                                      | Status             |
| ---- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------ |
| 4b.1 | `DashboardHub` interface declares `PositionUpdated`, `AgentStateChanged`, `ToolCallStarted`, `ToolCallCompleted`, `ReasoningUpdated`, `StrategyUpdated`, `CycleCompleted`, `EvaluationCompleted`                                                                          | ✅ (interface only) |
| 4b.2 | **None of the above `DashboardHub` events are ever pushed** from the server. After the `ExecutionServiceClient` is added (4a.3), its `OnPositionUpdate` and `OnStatusUpdate` callbacks must push `PositionUpdated` and the relevant status events through `DashboardHub`. | ❌                  |
| 4b.3 | **`GET /api/portfolio/positions` returns an empty list.** Must be backed by data from `ExecutionService GET /positions`.                                                                                                                                                  | ❌                  |
| 4b.4 | **`GET /api/portfolio/trades` returns an empty list.** Must be backed by data from `ExecutionService GET /trades`.                                                                                                                                                        | ❌                  |
| 4b.5 | **`GET /api/strategy/current` and `/api/strategy/history` are hardcoded stubs.** Must proxy `ExecutionService GET /strategy`.                                                                                                                                             | ❌                  |

### 4c — Connect MonitoringDashboard to AgentRunner

| #    | Item                                                                                                                                                                                                                                                                                                                                                       | Status |
| ---- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| 4c.1 | `AgentRunner` exposes full status, cycle history, mailboxes, errors, and metrics via REST                                                                                                                                                                                                                                                                  | ✅      |
| 4c.2 | **`MonitoringDashboard` has no `AgentRunnerClient`** and no connection to the AgentRunner service. Add a client that polls or subscribes to AgentRunner state and feeds the `AgentStateChanged`, `ToolCallStarted/Completed`, `ReasoningUpdated`, `CycleCompleted`, and `EvaluationCompleted` hub events. The AgentRunner URL must be injected via config. | ❌      |
| 4c.3 | **`docker-compose.yml` does not wire `MonitoringDashboard` to `AgentRunner`.** No `AgentRunner__Url` environment variable or `depends_on` entry exists.                                                                                                                                                                                                    | ❌      |
| 4c.4 | **`GET /api/agent/state`, `/api/agent/loop`, `/api/agent/toolcalls`, `/api/agent/reasoning` are all hardcoded stubs.** Must proxy or transform data from `AgentRunner GET /api/status` and `/api/cycles`.                                                                                                                                                  | ❌      |
| 4c.5 | **`GET /api/performance/cycle`, `/api/performance/lifetime`, `/api/performance/cycles`, `/api/performance/evaluation` are all hardcoded stubs.** Must be derived from AgentRunner cycle artifacts and ExecutionService metrics.                                                                                                                            | ❌      |

---

## Infrastructure / Cross-Cutting

| #   | Item                                                                                                                                                                                                                                                                                    | Status |
| --- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------ |
| I.1 | `docker-compose.yml` defines all five services with health checks and restart policies                                                                                                                                                                                                  | ✅      |
| I.2 | **Ollama LLM service is absent from `docker-compose.yml`.** Without it, `AgentInvoker` cannot reach the LLM. Add an `ollama` service or document and automate the host-gateway configuration.                                                                                           | ❌      |
| I.3 | **`strategy.json` shared volume is broken** (see 2a.2). The AgentRunner and ExecutionService mount different host paths, so the file-watch handoff never fires.                                                                                                                         | ❌      |
| I.4 | **`monitoring-dashboard-web` has no `VITE_API_BASE_URL` set** in `docker-compose.yml`. The React app's `api.ts` base URL is baked in at build time; if not set it defaults to `localhost` and will fail in Docker. Fix at build time via `--build-arg` or at runtime via nginx rewrite. | ⚠️      |
| I.5 | **`execution-service` has no `depends_on: agent-runner`** in `docker-compose.yml`, but it relies on the shared artifact volume being populated before a strategy can be loaded. This is not a hard runtime blocker but means the first startup ordering is undefined.                   | ⚠️      |
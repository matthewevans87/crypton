# Execution Service — Feature Backlog

The Execution Service is a long-running, deterministic trading engine that receives `strategy.json` from the Learning Loop and executes it against the Kraken exchange. It provides continuous position management, enforces risk rules, and handles real-time market events without any AI reasoning — all decisions are encoded in the strategy file.

---

## Platform & Technology Requirements

These requirements apply globally to every feature in this backlog. All features must be designed, implemented, and tested in conformance with these standards.

### Technology Stack

- **Language & Runtime:** C# on .NET 10. All projects target `net10.0`. Nullable reference types and implicit usings are enabled across all projects.
- **Project name:** `Crypton.Api.ExecutionService` (main service), `Crypton.Api.ExecutionService.Tests` (test project). Both live under `src/` alongside the other service projects.
- **Solution:** Both projects must be added to `crypton.slnx`.
- **Framework:** ASP.NET Core 10 (`Microsoft.NET.Sdk.Web`) used for the HTTP/WebSocket host. The background execution engine runs as a hosted `IHostedService` registered via the DI container.

### Hosting Modes

The service must support two distinct hosting modes, selected via a command-line argument or environment variable:

- **Service mode (default):** Starts the full ASP.NET Core host with the REST API, WebSocket hub, and all background workers. This is the production mode used in Docker.
- **CLI mode:** A lightweight command-line interface for operator tasks that do not require the full service to be running — for example, inspecting current state, force-loading a strategy file, or clearing safe mode from the command line. CLI mode must share the same business logic and service layer as service mode; it must not duplicate logic.

  Example CLI invocations:
  ```
  dotnet Crypton.Api.ExecutionService.dll status
  dotnet Crypton.Api.ExecutionService.dll safe-mode clear
  dotnet Crypton.Api.ExecutionService.dll strategy load ./strategy.json
  dotnet Crypton.Api.ExecutionService.dll promote-to-live --confirm
  ```

Mode selection: if the first argument is a recognized CLI command verb, CLI mode is activated. If no arguments are provided, or if `--service` is passed, service mode is activated.

### Streaming Status (WebSocket / SSE)

In addition to the polling REST API endpoints, the service must expose a real-time streaming interface for the Monitoring Dashboard and other internal services (e.g., Agent Runner) to receive push updates without polling:

- **Mechanism:** ASP.NET Core SignalR Hub at `/hubs/execution` (consistent with the pattern used by `Crypton.Api.MarketData` and `Crypton.Api.MonitoringDashboard`).
- **Streams:** The hub must push the following event streams to connected clients:
  - `StatusUpdate` — service mode, strategy state, DMS state — emitted on every state transition.
  - `MetricsUpdate` — full metrics snapshot (see ES-OAL-002) — emitted on every evaluation tick (throttled to a configurable max rate, default: 1 Hz).
  - `EventLog` — real-time forwarding of each structured event log entry as it is written.
  - `PositionUpdate` — per-position state (entry price, current price, unrealized P&L, exit state) — emitted on price tick for open positions.
- **Fallback:** The polling REST endpoints remain fully functional alongside the WebSocket hub; clients may use either.

### Test Coverage

- **100% line and branch coverage is required for all non-infrastructure code.** Every class, method, and branch in the service must be exercised by at least one unit or integration test.
- The test project is `Crypton.Api.ExecutionService.Tests`, using xUnit as the test framework. FluentAssertions is used for assertions. NSubstitute is used for mocking.
- Coverage is enforced via `coverlet` and reported in CI. PRs that reduce coverage below 100% on modified files must not be merged.
- **All exchange interactions (Kraken REST and WebSocket) must be tested exclusively through mock/stub implementations of `IExchangeAdapter`.** No live network calls are permitted in the test suite.
- Integration tests must be able to run the entire service in-process (no Docker required) by substituting the paper trading adapter and a fake filesystem. This is the primary testability requirement for running outside a container.
- Test helpers must expose a `TestServiceHost` builder that wires up the full DI container with mock adapters, an in-memory event log writer, and controllable time injection, so that full execution scenarios can be tested end-to-end in-process.

### Containerization

- The service must include a `Dockerfile` following the same multi-stage build pattern as the other service projects in this repository (build → publish → final, based on `mcr.microsoft.com/dotnet/aspnet:10.0`).
- The service listens on port **5004** (ASPNETCORE_HTTP_PORTS=5004) in the container.
- A service entry must be added to `docker-compose.yml` following the same structure as existing services: named volumes for `data/` and `logs/`, environment variable pass-through for `KRAKEN_API_KEY` and `KRAKEN_SECRET_KEY`, and a health check on `GET /health/live`.
- Data directories (`artifacts/`, `logs/`) are volume-mounted so that state survives container restarts.
- The service must be usable and fully testable without Docker: `dotnet run` in service mode and `dotnet test` in the test project must both work with no container dependency.

---

## Table of Contents

0. [Platform & Technology Requirements](#platform--technology-requirements)
1. [Strategy Management](#strategy-management)
2. [Condition Evaluation Engine](#condition-evaluation-engine)
3. [Order Management](#order-management)
4. [Position Management](#position-management)
5. [Exchange Abstraction Layer](#exchange-abstraction-layer)
6. [Kraken Exchange Integration](#kraken-exchange-integration)
7. [Operation Modes](#operation-modes)
8. [Resilience & Safety](#resilience--safety)
9. [Human Operator Interface](#human-operator-interface)
10. [Observability & Audit Logging](#observability--audit-logging)

---

## Strategy Management

### ES-SM-001 — Strategy File Watcher & Hot-Reload

**Priority:** p0

**Description:**
The Execution Service must monitor the `strategy.json` file on disk and automatically load any new or updated version without restarting the service. When the Synthesis Agent produces a new `strategy.json` at the end of each Learning Loop cycle, the service must detect the change and transition to executing the new strategy — seamlessly, without dropping open positions or skipping pending evaluations.

The file watcher must handle the transition atomically: finish any in-flight order operations under the old strategy before applying the new one, and reconcile any open positions against the new strategy's rules immediately upon load.

**Technical Requirements:**
- Use a filesystem watcher (e.g., `FileSystemWatcher` on .NET) or a polling fallback with a configurable interval.
- The hot-reload must be atomic: buffer the new strategy in memory, validate it fully (see ES-SM-002), then swap the active strategy reference in a thread-safe manner.
- Emit a structured log event and update internal state on every strategy swap.
- Expose the currently active strategy version (e.g., a hash or timestamp) via the REST API (ES-HOI-001).

**Acceptance Criteria:**
- [ ] A new `strategy.json` written to disk is detected and loaded within a configurable latency window (default: ≤5 seconds).
- [ ] The service continues operating on the old strategy if the new file fails validation; an error is logged and surfaced via the REST API.
- [ ] Strategy swap is thread-safe; no race conditions occur between evaluation loops and the reload.
- [ ] A log entry is emitted for every strategy load, including the prior strategy identifier, the new strategy identifier, and the UTC timestamp of the swap.
- [ ] If the service is restarted, it loads the most recently valid `strategy.json` on startup before resuming execution.

**Edge Cases:**
- `strategy.json` is partially written when the watcher fires (file still being written by the Synthesis Agent). The service must not attempt to parse a partially written file; use a read-retry with a short delay or wait for the write handle to close.
- `strategy.json` is deleted. The service must not crash; it should log a warning and continue executing the last valid strategy until the validity window expires.
- Rapid successive writes (e.g., two cycles completing back-to-back). Only the most recent valid file should be applied; intermediate versions may be skipped.

---

### ES-SM-002 — Strategy Schema Validation

**Priority:** p0

**Description:**
Every `strategy.json` loaded by the service must be validated against a strict schema before being accepted as the active strategy. The schema defines all mandatory and optional fields, their types, value ranges, and structural constraints. A strategy that fails validation must be rejected; the service must continue on the prior valid strategy and alert the operator.

The schema must accommodate the full range of strategy structures defined in the system contract, including all posture types (`aggressive`, `moderate`, `defensive`, `flat`, `exit_all`), all entry types (`market`, `limit`, `conditional`), scaled take-profit arrays, hard and trailing stop-loss specifications, optional time-based exits, and invalidation conditions.

**Technical Requirements:**
- Define a canonical JSON Schema (or equivalent strongly-typed model) for `strategy.json`.
- Validation must be synchronous and complete before the strategy pointer is updated.
- Validation errors must include the field path and reason for each failure.
- The schema must include: `mode`, `validity_window`, `posture`, `portfolio_risk` (max drawdown, daily loss limit, max exposure, max per-position size, safe-mode triggers), `positions[]`, and `strategy_rationale`.

**Acceptance Criteria:**
- [ ] A valid, well-formed `strategy.json` passes validation and is accepted.
- [ ] A file missing any mandatory top-level field is rejected with a descriptive error.
- [ ] A position entry with an unrecognized `entry_type` is rejected.
- [ ] A portfolio-level `max_drawdown_pct` value outside the range `(0, 1]` is rejected.
- [ ] An `exit_all` or `flat` posture with zero positions in the `positions` array is treated as valid.
- [ ] Validation errors are written to the structured event log with full field-level detail.

**Edge Cases:**
- `strategy.json` contains trailing commas or comments (non-standard JSON). The parser must either tolerate or clearly reject these with a useful error message.
- A position references an asset that is not currently supported by the connected exchange adapter. This must be treated as a validation warning (not a hard failure), with the affected position excluded from execution and a warning logged.
- Numeric fields expressed as strings (e.g., `"0.02"` instead of `0.02`). Strict mode should reject these; a lenient mode may coerce them.

---

### ES-SM-003 — Strategy DSL Condition Parser

**Priority:** p0

**Description:**
Entry conditions, exit conditions, and invalidation conditions in `strategy.json` are expressed as evaluable rules over live market data. These rules must be parsed and compiled into an executable form at strategy load time. The DSL must support a meaningful subset of conditions that can be evaluated deterministically against the real-time data stream.

Supported condition primitives must include: price comparisons (e.g., `price > 50000`), indicator comparisons (e.g., `RSI(14) < 30`), boolean composition (`AND`, `OR`, `NOT`), and threshold crossings (e.g., `MACD_histogram crosses_above 0`). Conditions must reference only data available from the live feed to avoid look-ahead.

**Technical Requirements:**
- Define a formal grammar or expression language for conditions (e.g., a simple prefix notation, JSONPath-style expressions, or a lightweight formula language).
- Parse and compile all conditions at strategy load time; surface parse errors immediately rather than at evaluation time.
- The compiled condition tree must be evaluated against a `MarketSnapshot` data structure populated from the live feed.
- Indicator references within conditions must map to the indicators computed and maintained by the technical indicator service.

**Acceptance Criteria:**
- [ ] A simple price threshold condition (`price(BTC/USD) > 45000`) parses and evaluates correctly.
- [ ] A compound condition using `AND` / `OR` / `NOT` parses and evaluates correctly.
- [ ] An indicator condition (`RSI(14, BTC/USD) < 30`) parses, resolves to the live indicator value, and evaluates correctly.
- [ ] A `crosses_above` / `crosses_below` crossing condition detects the transition correctly across consecutive market snapshots.
- [ ] A condition referencing an unknown indicator or asset produces a parse-time error, not a runtime panic.
- [ ] All compiled conditions are logged (in string form) to the event log when a strategy is loaded.

**Edge Cases:**
- An indicator has not yet computed a value (e.g., not enough historical bars). The condition evaluator must treat this as `false` (not triggered) and log a warning, never throw an exception.
- Floating-point imprecision in price comparisons. Use epsilon-tolerant comparisons for equality checks.
- Circular or self-referencing condition expressions. The parser must detect and reject these.

---

### ES-SM-004 — Strategy Validity Window Enforcement

**Priority:** p0

**Description:**
Every `strategy.json` includes a `validity_window` field specifying the UTC datetime at which the strategy expires. The Execution Service must track this window and enforce its expiry: when the window closes without a new strategy having been loaded, the service must stop opening new positions while continuing to manage existing ones. The operator and Monitoring Dashboard must be notified of the expired state.

**Technical Requirements:**
- A background timer or scheduled check must compare the current UTC time against `validity_window` on every evaluation tick.
- On expiry: set internal state to `StrategyExpired`; prevent any new entry orders from being placed; continue evaluating stop-loss and take-profit exits for open positions.
- Expose the validity window, time remaining, and expiry state via the REST API.

**Acceptance Criteria:**
- [ ] A strategy with a validity window in the past is loaded but immediately flagged as expired; no new entries are attempted.
- [ ] A valid strategy expires mid-execution; in-flight orders are allowed to complete, but no new entries are opened after expiry.
- [ ] The REST API reflects `"strategy_state": "expired"` within one evaluation tick of expiry.
- [ ] An expiry event is written to the structured event log.
- [ ] When a new valid strategy is loaded following expiry, the service returns to normal operation immediately.

**Edge Cases:**
- System clock drift causes the service to perceive the strategy as expired prematurely or belatedly. Use a reliable UTC source and document the tolerance.
- A new `strategy.json` arrives with a `validity_window` already in the past. It must be rejected with a validation error.
- The service restarts after a strategy has expired. On startup it must detect the expired state and begin in `StrategyExpired` mode rather than attempting new entries.

---

## Condition Evaluation Engine

### ES-CEE-001 — Continuous Entry Condition Evaluator

**Priority:** p0

**Description:**
The Condition Evaluation Engine polls the live market data stream and evaluates each pending position's entry condition on every tick. When an entry condition transitions from false to true, the evaluator dispatches an entry order request to the Order Manager. This is the core control loop of the Execution Service.

The evaluator must process all pending positions in parallel where possible and must complete each evaluation cycle before the next market tick, to avoid accumulating latency. It must also enforce strategy-level preconditions before triggering any entry: the strategy must be valid and not expired, the portfolio must be within its risk limits, and the daily loss limit must not have been breached.

**Technical Requirements:**
- The evaluator loop runs as a dedicated background worker, driven by market data events from the exchange adapter feed.
- Each position's entry condition is compiled at strategy load time (ES-SM-003) and evaluated against the current `MarketSnapshot`.
- Entry triggers are edge-detected (condition was false on the prior tick, true on the current tick) to avoid re-triggering an already-entered position.
- Portfolio-level preconditions (risk limits, daily loss limit, mode guards) are checked before dispatching any entry.

**Acceptance Criteria:**
- [ ] A `market` entry type position results in an immediate market order dispatch when the strategy is loaded (no condition to evaluate; fire immediately).
- [ ] A `conditional` entry fires exactly once per condition transition from false to true.
- [ ] If the daily loss limit is breached, no new entries are dispatched for the remainder of the trading day.
- [ ] If max total exposure is reached, no further entries are dispatched until exposure falls below the threshold.
- [ ] The evaluator does not dispatch entries for positions that already have an open or filled corresponding order.
- [ ] All entry trigger events are recorded in the structured event log with asset, condition snapshot, and UTC timestamp.

**Edge Cases:**
- Market data tick arrives while an order for the same position is already pending (not yet filled). The evaluator must not dispatch a duplicate order.
- The condition is true when the strategy is initially loaded (e.g., RSI is already below 30). Whether this constitutes a valid entry signal should be configurable (fire immediately vs. require a fresh crossing).
- A condition evaluates to true for a position in an `exit_all` strategy posture. No entries should be dispatched; the posture overrides individual position conditions.

---

### ES-CEE-002 — Continuous Exit Condition Evaluator

**Priority:** p0

**Description:**
For every open position, the evaluator continuously monitors stop-loss, take-profit, time-based exit, and invalidation conditions. When any exit condition is met, the evaluator dispatches a close order to the Order Manager with the appropriate reason code.

The evaluator must support multi-target (scaled) take-profits, where partial fills close a percentage of the position at each target. It must also support trailing stops, updating the stop price dynamically as the market moves in the position's favour.

**Technical Requirements:**
- Per-position exit state (current trailing stop level, targets already hit, partial fill quantities) must be persisted so it survives service restarts.
- Trailing stop updates must be applied on every price tick for open positions in profit.
- Time-based exits must be evaluated against UTC time, not wall-clock time, to avoid timezone bugs.
- Each take-profit target must record whether it has been triggered to prevent duplicate partial close orders.

**Acceptance Criteria:**
- [ ] A hard stop-loss triggers a market close order when `bid price ≤ stop_price` (for a long position).
- [ ] A trailing stop-loss updates its level correctly as price moves in favour; it does not move backward when price reverses.
- [ ] The first scaled take-profit target triggers a partial close for the correct percentage of the position.
- [ ] Subsequent take-profit targets trigger independently after the first is filled.
- [ ] A time-based exit triggers a close order at or after the configured UTC exit time.
- [ ] An invalidation condition breach triggers an immediate close order with reason code `invalidation`.
- [ ] All exit trigger events are recorded in the event log with full context.

**Edge Cases:**
- Price gaps through a stop-loss level (the bid never equals the stop exactly). The evaluator must trigger on `bid ≤ stop` (not `bid == stop`).
- A partial close order is placed but not filled before the next take-profit level is hit. The evaluator must wait for the fill confirmation before dispatching the next partial close.
- An `exit_all` posture is loaded while positions are open. The evaluator must immediately dispatch close orders for all open positions regardless of their individual exit conditions.

---

## Order Management

### ES-OM-001 — Order Router

**Priority:** p0

**Description:**
The Order Router accepts order requests from the Condition Evaluation Engine and dispatches them to the appropriate exchange adapter. It translates internal order requests (asset, direction, size, type, price) into the exchange-specific wire format and manages the full lifecycle of each order: placed → pending → partially filled → filled / cancelled / rejected.

**Technical Requirements:**
- Support order types: `market`, `limit`, and `conditional` (exchange-native stop/limit-stop).
- Maintain an internal order registry mapping internal order IDs to exchange order IDs.
- Handle partial fills: update position state proportionally as fills arrive.
- Apply a configurable order timeout: if a limit order is not filled within N seconds/minutes, cancel and optionally re-submit as a market order.

**Acceptance Criteria:**
- [ ] A `market` buy order is submitted, acknowledged by the exchange, and the fill is recorded in the position registry.
- [ ] A `limit` sell order is placed, remains pending, and the order registry reflects `pending` status until a fill event arrives.
- [ ] A partial fill updates the open position size and average fill price correctly.
- [ ] An order rejected by the exchange is logged with the rejection reason and surfaced via the REST API.
- [ ] An order timeout triggers a cancellation request followed by a re-evaluation of whether to re-submit.
- [ ] All order lifecycle events (placed, filled, partially filled, cancelled, rejected) are written to the structured event log.

**Edge Cases:**
- The exchange API call to place an order times out with no response. The Order Router must query the exchange to determine whether the order was accepted before resubmitting to avoid duplicates.
- A fill event arrives for an order that is not in the internal registry (e.g., a manual trade placed directly on the exchange). This must be reconciled into the position state and flagged as an external fill.
- Two exit conditions fire simultaneously for the same position (e.g., stop-loss and time exit at the same tick). Only one close order must be dispatched; the second must be suppressed.

---

### ES-OM-002 — Position Sizing Calculator

**Priority:** p0

**Description:**
Before any entry order is dispatched, the Position Sizing Calculator computes the exact order quantity from the strategy's capital allocation percentage, current available capital, and exchange-specific constraints (minimum order size, lot size increments, maximum order size). It also enforces the portfolio-level per-position size cap from `strategy.json`.

**Technical Requirements:**
- Fetch current available capital from the exchange account balance (via the REST adapter) or the paper trading ledger.
- Apply the allocation percentage from the position definition: `quantity = (available_capital * allocation_pct) / current_price`.
- Round quantity to the exchange's lot size increment; verify it meets the exchange's minimum order size.
- Clamp the computed quantity to the portfolio-level `max_per_position_pct` if exceeded.

**Acceptance Criteria:**
- [ ] A 10% allocation on a $10,000 portfolio at a $50,000 asset price results in an order quantity of 0.02 (adjusted for lot size).
- [ ] A quantity that would breach the per-position size cap is clamped to the cap value, and a warning is logged.
- [ ] A computed quantity below the exchange minimum lot size results in the order being skipped, with an event log entry explaining why.
- [ ] The computed quantity, input values, and any clamping applied are written to the event log for every order.

**Edge Cases:**
- Available capital changes between the sizing calculation and order dispatch (e.g., another order filled in the interim). Re-fetch balance immediately before submitting.
- The computed allocation produces a fractional lot below the minimum increment. Round down, not up, to avoid over-allocation.
- Available capital is zero or negative (all capital deployed). The order must be blocked and logged.

---

## Position Management

### ES-PM-001 — Position State Registry

**Priority:** p0

**Description:**
The Position State Registry is the authoritative in-process record of all open positions managed by the Execution Service. It tracks each position's asset, direction, average entry price, current quantity, fill history, open orders, unrealized P&L, and per-position exit state (trailing stop level, take-profit targets hit). The registry is persisted to disk on every mutation and reconciled against the exchange on startup.

**Technical Requirements:**
- Persisted as a structured file (e.g., JSON) updated transactionally on each state change.
- Unrealized P&L must be recomputed on every market tick using the mid-price from the live feed.
- The registry must support partial fills: quantity and average entry price update incrementally as fills arrive.
- Each position must carry a reference to the strategy cycle that opened it (for audit and evaluation purposes).

**Acceptance Criteria:**
- [ ] A newly filled order creates a position record in the registry with correct asset, direction, quantity, and average entry price.
- [ ] Unrealized P&L is updated every tick and accessible via the REST API.
- [ ] A position closed by a full fill is removed from the registry and added to the trade history log.
- [ ] The registry file is updated atomically (write to temp file, then rename) to prevent corruption on crash.
- [ ] On service restart, the registry is loaded from disk before any execution resumes.

**Edge Cases:**
- A position in the registry has no corresponding open order on the exchange (e.g., it was manually closed on the exchange between ticks). This must be detected during reconciliation (ES-R&S-004) and the registry updated accordingly.
- Two strategy cycles both have a position for the same asset in the same direction. These must be tracked as distinct positions (with distinct allocation contexts) rather than merged.
- An asset is delisted or suspended on the exchange while a position is open. The service must detect this, halt evaluation for that position, and raise an alert.

---

### ES-PM-002 — Portfolio Risk Limit Enforcement

**Priority:** p0

**Description:**
Before dispatching any order, and on every evaluation tick while positions are open, the service enforces portfolio-level risk constraints from `strategy.json`: maximum total exposure, maximum drawdown threshold, and daily loss limit. When any limit is breached, the appropriate response is triggered: suspending new entries (exposure cap, daily loss limit) or triggering safe mode (drawdown threshold).

**Technical Requirements:**
- Total exposure is computed as the sum of all open position notional values divided by total account equity.
- Drawdown is computed as `(peak_equity - current_equity) / peak_equity`, tracked from the start of the current strategy cycle.
- Daily loss is computed as the net realized + unrealized P&L since UTC midnight.
- Safe-mode trigger conditions specified in the strategy must be evaluated as first-class conditions, not just the above globals.

**Acceptance Criteria:**
- [ ] If total exposure reaches `max_exposure_pct`, no further entry orders are dispatched until exposure falls below the threshold.
- [ ] If `max_drawdown_pct` is breached, the service enters safe mode (ES-R&S-001) immediately.
- [ ] If `daily_loss_limit` is breached, new entries are suspended for the remainder of the UTC day; open positions continue to be managed.
- [ ] All limit breaches are written to the event log and surfaced via the REST API.
- [ ] Strategy-defined safe-mode trigger conditions (arbitrary DSL conditions) are evaluated and trigger safe mode when met.

**Edge Cases:**
- An aggressive market move causes multiple limits to be breached within a single tick. All breached limits must be logged; the most severe response (safe mode) takes precedence.
- A new strategy is loaded with relaxed limits while a prior limit breach is still active. The breach state must be re-evaluated against the new limits; it must not be silently cleared without re-computing.
- Unrealized P&L fluctuates around the daily loss limit threshold, triggering and un-triggering repeatedly. Implement hysteresis (e.g., require a configurable buffer below the threshold before resuming).

---

## Exchange Abstraction Layer

### ES-EAL-001 — Exchange Adapter Interface

**Priority:** p0

**Description:**
All exchange interactions must go through a formal `IExchangeAdapter` interface. This abstraction ensures that adding a new exchange (e.g., Coinbase, Binance) requires only a new adapter implementation, with zero changes to the core execution logic. The interface must cover the complete range of operations the Execution Service needs: market data subscription, order submission, order cancellation, order status query, account balance query, and position/trade history query.

**Technical Requirements:**
- Define an `IExchangeAdapter` interface (or abstract base) with the following logical methods:
  - `SubscribeToMarketData(assets, callback)` — real-time price/order book feed
  - `PlaceOrder(order)` → `OrderAcknowledgement`
  - `CancelOrder(orderId)` → `CancellationResult`
  - `GetOrderStatus(orderId)` → `OrderStatus`
  - `GetAccountBalance()` → `AccountBalance`
  - `GetOpenPositions()` → `IEnumerable<ExchangePosition>`
  - `GetTradeHistory(since)` → `IEnumerable<Trade>`
- All methods must be async, cancellation-token-aware, and throw typed exceptions for exchange-specific error conditions.
- The adapter in use must be configurable (e.g., via `config.yaml`) without code changes.

**Acceptance Criteria:**
- [ ] The Kraken adapter implements `IExchangeAdapter` in full.
- [ ] A `PaperTradingAdapter` (or mock adapter for testing) also implements `IExchangeAdapter` in full.
- [ ] The core execution engine has zero direct references to Kraken-specific types; all exchange interaction is through the interface.
- [ ] Swapping the active adapter in configuration (with a service restart) switches all exchange interaction to the new adapter without code changes.
- [ ] All adapter methods are covered by unit tests using a mock or stub adapter.

**Edge Cases:**
- An adapter method is not supported by a particular exchange (e.g., `GetOpenPositions` is not natively available). The adapter must either emulate it from available data or throw a clearly typed `NotSupportedException`.
- Two exchange adapters are configured simultaneously (e.g., split execution across exchanges). The interface must accommodate this without requiring the core engine to know about multi-exchange routing.

---

## Kraken Exchange Integration

### ES-KEI-001 — Kraken WebSocket Adapter (Market Data)

**Priority:** p0

**Description:**
The Kraken WebSocket adapter establishes and maintains a persistent WebSocket connection to the Kraken exchange for real-time market data. It subscribes to price feed (ticker), order book, and trade event channels for all assets referenced in the active strategy. All incoming market data events are translated into canonical `MarketSnapshot` objects and dispatched to the Condition Evaluation Engine.

**Technical Requirements:**
- Connect to the Kraken WebSocket v2 API (`wss://ws.kraken.com/v2`).
- Subscribe to `ticker`, `book` (configurable depth), and `trade` channels for required assets.
- Re-subscribe automatically when the active strategy changes to a different asset set.
- Implement automatic reconnection with exponential back-off on connection loss.
- Parse all incoming messages and deserialize into canonical internal models; log and discard unrecognized message types.

**Acceptance Criteria:**
- [ ] Subscribing to `BTC/USD` and `ETH/USD` ticker channels produces a continuous stream of `MarketSnapshot` updates.
- [ ] A WebSocket disconnection is detected within the configurable heartbeat timeout, and reconnection is attempted automatically.
- [ ] After reconnection, subscriptions are re-established and data flow resumes without manual intervention.
- [ ] The adapter emits a structured event log entry on each connect, disconnect, and reconnect.
- [ ] The most recent `MarketSnapshot` per asset is always accessible synchronously (via a cache) without awaiting the next tick.

**Edge Cases:**
- A subscription request is rejected by Kraken (e.g., invalid asset pair). The error must be logged and the remaining subscriptions must proceed unaffected.
- The WebSocket connection drops during a period when the Condition Evaluation Engine is mid-evaluation. The engine must complete the current evaluation on stale data, then pause until the feed is restored.
- Kraken publishes a snapshot followed by incremental order book updates. The adapter must maintain a correct local order book state by applying incremental updates to the snapshot.

---

### ES-KEI-002 — Kraken REST Adapter (Order Management & Account)

**Priority:** p0

**Description:**
The Kraken REST adapter implements the order management, account, and position query surfaces of `IExchangeAdapter` using the Kraken REST API. It handles authentication (API key / private key signing), request serialization, response parsing, and error handling for all REST-based operations.

**Technical Requirements:**
- Implement Kraken API key authentication using HMAC-SHA512 message signing per the Kraken API specification.
- Support the following Kraken REST endpoints: `AddOrder`, `CancelOrder`, `QueryOrders`, `OpenOrders`, `TradesHistory`, `Balance`, `OpenPositions` (for margin accounts).
- API credentials must be loaded from the service configuration and never logged or stored in plaintext outside of secure configuration.
- All REST calls must respect the Kraken API rate limit tier assigned to the connected account.

**Acceptance Criteria:**
- [ ] A `PlaceOrder` call produces a valid signed request, receives an order ID from Kraken, and returns an `OrderAcknowledgement`.
- [ ] A `CancelOrder` call successfully cancels a pending limit order.
- [ ] A `GetAccountBalance` call returns current asset balances matching the Kraken account state.
- [ ] An authentication failure (wrong key) produces a typed `AuthenticationException`, not an unhandled error.
- [ ] All REST responses are logged at debug level (excluding sensitive data) for diagnostics.

**Edge Cases:**
- Kraken returns a `EAPI:Invalid nonce` error due to clock skew. The adapter must detect this, resync the nonce, and retry.
- Kraken returns a temporary `EService:Unavailable` error. Apply retry with back-off; do not propagate a hard failure to the order router until the retry budget is exhausted.
- Order placement succeeds (HTTP 200, order ID returned) but the subsequent balance query temporarily shows no change (eventual consistency). The service must not double-submit.

---

### ES-KEI-003 — Rate Limit Detection & Adaptive Back-off

**Priority:** p1

**Description:**
The Kraken API enforces per-account rate limits, scoring each API call against a decay counter. Exceeding the limit results in a temporary API ban. The service must track its rate limit consumption, detect rate limit responses, and apply adaptive back-off to avoid bans or order failures.

**Technical Requirements:**
- Track the estimated rate limit counter for both the REST API and WebSocket API separately.
- On receiving a rate limit response (HTTP 429 or Kraken error `EAPI:Rate limit exceeded`), immediately pause all requests to that API surface and apply exponential back-off.
- Implement a token-bucket or leaky-bucket model to proactively throttle requests before the limit is reached.
- The back-off state must be observable via the REST API (ES-HOI-001): operator can see if the service is currently backed off and for how long.

**Acceptance Criteria:**
- [ ] A simulated rate limit response triggers a back-off period; no further requests are dispatched until the window expires.
- [ ] After the back-off window, requests resume and the rate limit counter resets.
- [ ] The service does not exceed Kraken's documented rate limit tiers under normal operating conditions (verified by test with a mock rate limiter).
- [ ] The current back-off state is exposed via the REST API.

**Edge Cases:**
- Multiple simultaneous outgoing requests hit the rate limit simultaneously (burst). Only one back-off timer must be set; the others must queue behind it.
- A rate limit event occurs during a stop-loss exit dispatch. Stop-loss retries must be prioritized over new entry requests in the back-off queue.

---

## Operation Modes

### ES-OM-001 — Paper Trading Mode

**Priority:** p0

**Description:**
Paper trading mode simulates all order execution against live market data with no real capital at risk. Instead of routing orders to the exchange, the service uses a simulated execution engine that applies orders against the live price feed, emulating fills, slippage, and exchange fees. All other service behaviour (position tracking, risk enforcement, event logging, API surface) is identical to live mode, enabling full validation of strategies without financial exposure.

The `Paper Trading Adapter` implements `IExchangeAdapter` and all methods operate against a simulated ledger rather than the real exchange. Paper trading is the default operating mode.

**Technical Requirements:**
- The paper trading adapter maintains a simulated account ledger (starting balance configurable in `config.yaml`).
- Market orders are filled immediately at the current mid-price plus a configurable simulated slippage factor.
- Limit orders are filled when the live price reaches the limit price; they join an in-memory pending order book.
- A configurable simulated commission rate is applied to all fills and deducted from the ledger.
- The simulated ledger must be persisted to disk and survive service restarts.

**Acceptance Criteria:**
- [ ] With paper mode active, no real orders are sent to the Kraken exchange (confirmed by mock adapter in tests).
- [ ] A market buy order is filled at `current_mid_price * (1 + slippage_pct)` with commission deducted.
- [ ] A limit buy order at a price below the current market remains pending until the live price falls to or below the limit.
- [ ] The simulated ledger balance, positions, and trade history are accessible via the REST API.
- [ ] The active mode (`paper` / `live`) is clearly indicated in the REST API and event log.

**Edge Cases:**
- A limit order is placed in paper mode, then the strategy is hot-reloaded. Previously pending paper orders must be cancelled and removed from the simulated order book.
- The simulated ledger balance falls below zero (extreme sequence of losses). The service must stop placing new paper orders and surface an alert.
- Paper mode is active but `strategy.json` specifies `"mode": "live"`. The service must refuse to switch to live mode via strategy file alone; live mode requires explicit operator promotion (ES-OM-003).

---

### ES-OM-002 — Live Trading Mode

**Priority:** p0

**Description:**
Live trading mode routes all orders to the real Kraken exchange using real capital. It is operationally identical to paper mode from the service's perspective — the only difference is the active `IExchangeAdapter` implementation. The critical distinction is that live mode requires explicit human promotion and cannot be activated automatically by a strategy file or configuration change alone.

**Technical Requirements:**
- Live mode uses the `KrakenRestAdapter` and `KrakenWebSocketAdapter` for all execution.
- The service must not enter live mode on startup unless it was explicitly last set to live mode by a previous operator `promote` command (persisted in service state).
- Live mode state is persisted so it survives restarts without requiring re-promotion.
- All live mode order placements must include an additional log tag (`"mode": "live"`) to make them unambiguously distinguishable in the audit log.

**Acceptance Criteria:**
- [ ] The service starts in paper mode on first launch.
- [ ] Live mode cannot be entered by modifying `strategy.json` or `config.yaml` alone.
- [ ] After an operator issues a `promote` command via the REST API and the service is restarted, it resumes in live mode.
- [ ] All live order dispatches are tagged `"mode": "live"` in the event log.
- [ ] Demoting from live to paper mode via the REST API immediately stops real order dispatch; pending real orders are left open (not cancelled) and must be reviewed by the operator.

**Edge Cases:**
- The service crashes mid-startup while in live mode. On restart it must reconcile open positions (ES-R&S-004) before resuming execution in live mode.
- The operator promotes to live mode while a strategy with `"mode": "paper"` is active. The promotion must take effect; the mode field in the strategy file is advisory, not authoritative.
- Clock skew causes the service to miss the strategy validity window while in live mode. Open live positions must still be managed via their exit conditions; no new entries are opened.

---

### ES-OM-003 — Paper-to-Live Promotion

**Priority:** p1

**Description:**
The promotion workflow is the controlled, audited process by which an operator explicitly transitions the service from paper trading to live trading. It must include a confirmation step, persist the new mode, and write an attribution-logged audit entry. It must not be possible to promote to live mode without operator authentication.

**Technical Requirements:**
- Exposed as a dedicated POST endpoint on the operator REST API: `POST /operator/promote-to-live`.
- The endpoint requires authentication (bearer token or equivalent) and an explicit confirmation body to prevent accidental invocation.
- On promotion: update persisted service mode state, log the operator identity and UTC timestamp, and reload the Kraken live adapter.
- A corresponding `POST /operator/demote-to-paper` endpoint is also required.

**Acceptance Criteria:**
- [ ] An unauthenticated promotion request returns `401 Unauthorized`.
- [ ] A promotion request missing the confirmation body returns `400 Bad Request` with a descriptive message.
- [ ] A successful promotion writes an event log entry: `{ event: "mode_promoted", from: "paper", to: "live", operator: "<id>", timestamp: "<utc>" }`.
- [ ] After promotion, the service state file reflects `"mode": "live"` and the live adapter is active.
- [ ] A demotion request while open live positions exist emits a warning in the response body listing the open positions; positions are not closed automatically.

**Edge Cases:**
- A promotion request arrives while the service is in safe mode. Promotion must be blocked until safe mode is cleared.
- Two simultaneous promotion requests arrive (race condition). Only one must succeed; the second must receive a `409 Conflict` or equivalent.

---

### ES-OM-004 — Safe Mode

**Priority:** p0

**Description:**
Safe mode is a protective operating state entered automatically when the service detects an unacceptable risk condition: N consecutive application failures, a max drawdown breach, a dead man's switch timeout, or an explicit operator command. In safe mode, the service suspends all new position entries and attempts an orderly, market-order exit of all open positions. After all positions are closed, the service idles in a degraded state until explicitly cleared by an operator.

**Technical Requirements:**
- Safe mode entry must be atomic: set the internal mode state before dispatching any close orders to prevent race conditions.
- Close order dispatch in safe mode must use market orders to guarantee fills regardless of slippage.
- If a close order is rejected or times out, retry with a configurable back-off up to N attempts; log all failures.
- Safe mode must persist across restarts: on startup, if the persisted state is `safe_mode`, resume safe mode behaviour (no new entries, re-attempt open position close-out) before accepting normal traffic.
- The reason for safe mode entry must be persisted and surfaced via the REST API.

**Acceptance Criteria:**
- [ ] Triggering safe mode via the REST API immediately suspends new entries and dispatches close orders for all open positions.
- [ ] Safe mode persists across a service restart.
- [ ] The `/status` REST endpoint reflects `"mode": "safe"` with the reason and entry timestamp.
- [ ] When all positions are closed successfully, the service transitions to `"mode": "safe_idle"` and stops dispatching further orders.
- [ ] An operator `clear-safe-mode` command (authenticated) resets the state and resumes normal operation.
- [ ] All safe mode entry and exit events are recorded in the structured event log with full context.

**Edge Cases:**
- A safe mode close order itself fails repeatedly. The service must not enter an infinite retry loop; after exhausting retries, it must log a critical alert and remain in `safe_mode` awaiting operator intervention.
- Safe mode is triggered while a strategy hot-reload is in progress. The reload must be aborted and safe mode takes precedence.
- The operator clears safe mode but the original trigger condition (e.g., drawdown) is still active. The service must re-enter safe mode on the next evaluation tick if the condition persists.

---

## Resilience & Safety

### ES-R&S-001 — Consecutive Failure Detection

**Priority:** p0

**Description:**
The service must track crashes and unclean exits and automatically enter safe mode when N consecutive failures are detected. A "failure" is defined as any unclean process exit (non-zero exit code, unhandled exception causing shutdown, or watchdog timeout). A clean, operator-initiated shutdown does not count as a failure. The failure counter must be persisted so it survives restarts.

**Technical Requirements:**
- On startup, read the persisted failure counter. If the counter equals or exceeds the configured threshold N, enter safe mode immediately before loading the strategy or resuming execution.
- On clean shutdown (operator command or graceful exit), reset the failure counter.
- On startup after a successful initialization and at least one full evaluation cycle, decrement or reset the failure counter to indicate a stable run.
- N must be configurable in `config.yaml`.

**Acceptance Criteria:**
- [ ] After N consecutive unclean exits (simulated in tests), the service enters safe mode on the next startup without loading a strategy.
- [ ] A clean shutdown between crashes resets the counter.
- [ ] The current failure count and threshold are exposed via the REST API.
- [ ] A safe mode entry due to consecutive failures writes a structured event log entry with the failure count.

**Edge Cases:**
- The failure counter file itself is corrupted. The service must treat this as a zero count (not an error), log a warning, and reset the file.
- The service crashes during the startup sequence (before the failure counter is read). This must still increment the counter on the next startup (the counter is incremented at start, decremented on clean init).

---

### ES-R&S-002 — Dead Man's Switch

**Priority:** p1

**Description:**
The service must detect sustained loss of market data connectivity and automatically enter safe mode if the feed is unresponsive for longer than a configurable duration. This prevents the service from continuing to hold open positions while flying blind, without the live data needed to evaluate exit conditions.

The dead man's switch monitors the timestamp of the most-recently-received market data event. If this timestamp is older than the configured threshold, connectivity is assumed lost and safe mode is triggered.

**Technical Requirements:**
- A background watchdog timer checks the last-received market data timestamp on a configurable interval (default: every 5 seconds).
- If the elapsed time since the last tick exceeds the configurable DMS threshold (default: 60 seconds), trigger safe mode with reason `connectivity_loss`.
- The watchdog must distinguish between a legitimate exchange maintenance window (e.g., scheduled downtime with advance notice) and an unplanned outage. If Kraken publishes a maintenance status via their system status API, the DMS timeout should be extended accordingly.

**Acceptance Criteria:**
- [ ] If no market data is received for longer than the configured DMS timeout, safe mode is triggered automatically.
- [ ] On recovering connectivity, the dead man's switch resets; the service does not remain in safe mode due to a transient disconnection that was shorter than the threshold.
- [ ] The current DMS state (armed, tripped) and the last received tick timestamp are exposed via the REST API.
- [ ] A DMS trigger event is written to the event log with the elapsed duration since the last tick.

**Edge Cases:**
- The DMS fires while safe mode close orders are in flight from a prior trigger. The service must not re-enter safe mode or dispatch duplicate close orders.
- The WebSocket connection is alive but Kraken is not sending data (a "silent" feed). This must be detected by the absence of heartbeat messages or ticks, not just a broken TCP connection.
- The DMS timeout is set very low (e.g., 5 seconds) and the exchange naturally has periods of low activity with no ticks. The service must not false-positive on quiet markets; the DMS check should apply only to the absence of heartbeat/ping messages, not the absence of trade ticks.

---

### ES-R&S-003 — Exchange Rate Limit Back-off

**Priority:** p1

*(See ES-KEI-003 — this feature is defined there as it is specific to the Kraken adapter. The resilience requirement here is that the back-off mechanism is part of the `IExchangeAdapter` contract, so all adapters must implement it.)*

**Description:**
As an adapter-level contract, every `IExchangeAdapter` implementation must include a built-in rate limit detection and back-off mechanism. The core execution engine must never need to handle rate limit logic directly; it is fully encapsulated within the adapter.

**Acceptance Criteria:**
- [ ] The `IExchangeAdapter` interface includes a method or event to query current rate limit state.
- [ ] The exchange adapter blocks (or returns a queued acknowledgment) when the rate limit back-off is active, rather than propagating a raw rate limit error to the core engine.
- [ ] The paper trading adapter trivially satisfies this contract (no rate limits to enforce).

---

### ES-R&S-004 — Post-Restart State Reconciliation

**Priority:** p0

**Description:**
After any restart (clean or unclean), the service must reconcile its local position and order state against the exchange's actual state before resuming order dispatch. This prevents the service from acting on stale or incorrect local state — for example, attempting to close a position that was already closed on the exchange, or failing to manage a position that was opened during a crash.

**Technical Requirements:**
- On startup, after loading the persisted position registry, call `GetOpenPositions()` and `GetOpenOrders()` on the exchange adapter.
- For each exchange position that is not in the local registry, add it as a new position with a `reconciled` origin tag.
- For each local position that is not on the exchange, mark it as `closed_external` and remove it from active management.
- For each open order on the exchange that is not in the local order registry, add it as a `reconciled_order`.
- Log a reconciliation summary (positions added, positions removed, orders reconciled) before resuming execution.

**Acceptance Criteria:**
- [ ] A position opened on the exchange during a crash (or external to the service) is detected and added to the registry on startup.
- [ ] A position in the local registry that is not present on the exchange (already closed externally) is removed and logged.
- [ ] The reconciliation summary is written to the event log before the first evaluation tick after startup.
- [ ] Reconciliation is skipped in paper trading mode (the paper ledger is authoritative).

**Edge Cases:**
- The exchange API is unavailable at startup (e.g., it is also restarting). The service must wait and retry reconciliation rather than proceeding with stale local state.
- Reconciliation finds a discrepancy in position size (exchange shows 0.1 BTC open; local registry shows 0.12 BTC). The exchange is authoritative; the local record must be corrected and the discrepancy logged as a critical event.
- The service restarts so quickly that open orders from before the restart are still pending on the exchange. These must be re-registered in the order registry and managed normally.

---

## Human Operator Interface

### ES-HOI-001 — Operator REST API

**Priority:** p0

**Description:**
The Execution Service exposes a REST API for operator commands and status queries. This API is the sole interface through which human operators interact with the service, and it is also consumed by the Monitoring Dashboard. All endpoints that modify service state require authentication. All commands are logged with operator attribution.

**Technical Requirements:**
- Implement using ASP.NET Core with JSON request/response bodies.
- Authentication: bearer token (API key) loaded from `config.yaml`; unauthenticated requests to mutating endpoints return `401`.
- All mutating endpoints must write an event log entry: `{ event: "<command>", operator: "<token_id>", timestamp: "<utc>", params: { ... } }`.

**Read Endpoints:**

| Method | Path         | Description                                                             |
| ------ | ------------ | ----------------------------------------------------------------------- |
| GET    | `/status`    | Service health, active mode, strategy state, DMS state, failure counter |
| GET    | `/strategy`  | Active strategy summary (posture, assets, validity window, rationale)   |
| GET    | `/positions` | All open positions with entry price, current price, unrealized P&L      |
| GET    | `/orders`    | All open and recently completed orders                                  |
| GET    | `/trades`    | Paginated trade history for the current strategy cycle                  |
| GET    | `/metrics`   | Portfolio metrics: equity, exposure, drawdown, daily P&L, win rate      |
| GET    | `/events`    | Recent entries from the structured event log (paginated)                |

**Write Endpoints:**

| Method | Path                                    | Description                                                 |
| ------ | --------------------------------------- | ----------------------------------------------------------- |
| POST   | `/operator/promote-to-live`             | Promote to live trading mode                                |
| POST   | `/operator/demote-to-paper`             | Demote to paper trading mode                                |
| POST   | `/operator/safe-mode`                   | Force safe mode entry                                       |
| POST   | `/operator/clear-safe-mode`             | Clear safe mode (operator confirms all conditions resolved) |
| POST   | `/operator/suspend-entries`             | Suspend new position entries without entering safe mode     |
| POST   | `/operator/resume-entries`              | Resume suspended entries                                    |
| POST   | `/operator/close-position/{positionId}` | Force-close a specific open position at market              |
| POST   | `/operator/override-strategy`           | Load a manually supplied strategy JSON payload              |

**Acceptance Criteria:**
- [ ] All read endpoints return correct data reflecting current service state.
- [ ] An unauthenticated write request returns `401 Unauthorized`.
- [ ] Every write request that succeeds writes a log entry with operator identity and timestamp.
- [ ] The `/status` endpoint returns `200` within 200ms under normal load.
- [ ] A `POST /operator/close-position/{positionId}` with a valid position ID dispatches a market close order and returns the resulting order ID.
- [ ] A `POST /operator/close-position/{unknownId}` returns `404 Not Found`.

**Edge Cases:**
- An operator issues `close-position` and `safe-mode` simultaneously (race condition). Only one close order must be dispatched per position.
- The service is in safe mode and an operator issues `override-strategy`. The strategy must be accepted and persisted but not executed until safe mode is cleared.
- The `/events` endpoint is queried during a high-throughput event burst. Pagination and cursor-based access must prevent response timeouts.

---

## Observability & Audit Logging

### ES-OAL-001 — Structured Event Log

**Priority:** p0

**Description:**
The Execution Service must emit a structured, append-only event log capturing every significant decision, order attempt, fill, rejection, and state transition with full context. This log is the primary audit trail for understanding exactly what the service did, when, and why. It is also the data source for the Evaluation Agent's trade review.

Every event must be a self-contained JSON object written to a line-delimited JSON (NDJSON) file. Events must never be deleted or mutated after writing.

**Technical Requirements:**
- Log file path is configurable in `config.yaml`; default is `logs/execution_events.ndjson`.
- Each event object must include at minimum: `timestamp` (ISO 8601 UTC), `event_type`, `service_version`, and `mode` (`paper`/`live`).
- Events must be written synchronously relative to the action they describe (i.e., the log entry is written before the action is considered complete).
- A log rotation policy must be configurable (e.g., rotate daily, keep last N files).

**Required Event Types:**
- `strategy_loaded`, `strategy_rejected`, `strategy_expired`, `strategy_swapped`
- `entry_triggered`, `entry_skipped` (with reason)
- `exit_triggered`, `exit_skipped`
- `order_placed`, `order_filled`, `order_partially_filled`, `order_cancelled`, `order_rejected`
- `position_opened`, `position_closed`, `position_reconciled`
- `risk_limit_breached` (with limit name and values)
- `safe_mode_entered`, `safe_mode_cleared`
- `dms_triggered`, `dms_reset`
- `rate_limit_backoff_started`, `rate_limit_backoff_ended`
- `operator_command` (for all write API calls)
- `mode_promoted`, `mode_demoted`
- `reconciliation_summary`
- `service_started`, `service_stopped`

**Acceptance Criteria:**
- [ ] Every order lifecycle event (placed → filled/cancelled/rejected) is written to the log with full context.
- [ ] The log file is NDJSON: one valid JSON object per line, no trailing commas or array wrappers.
- [ ] Log entries are never deleted or overwritten.
- [ ] On service restart, log writing resumes by appending to the current log file (not truncating).
- [ ] Log rotation produces a new file without dropping events that arrive during the rotation.
- [ ] All event types listed above are represented in the test suite with at least one example.

**Edge Cases:**
- The log file path is not writable (permissions error). The service must fail fast on startup with a clear error message rather than silently dropping log entries.
- The disk fills up during operation. The service must detect write failures, surface an alert via the REST API, and continue operating (accepting the loss of subsequent log entries with a warning) rather than crashing.
- Two threads write log entries simultaneously. The log writer must serialize writes to prevent interleaved or corrupt lines.

---

### ES-OAL-002 — Service Health & Metrics Exposure

**Priority:** p1

**Description:**
In addition to the event log, the service must expose real-time operational metrics via the REST API for consumption by the Monitoring Dashboard. These metrics provide a live snapshot of service health and portfolio state without requiring the dashboard to parse the event log.

**Technical Requirements:**
- All metrics are computed in-memory from the Position State Registry, Order Registry, and strategy state.
- Metrics are recomputed on every evaluation tick and cached for API reads.
- The metrics endpoint must respond within 100ms regardless of tick frequency.

**Metrics to Expose:**
- Current account equity (from paper ledger or live account balance)
- Total unrealized P&L (sum across all open positions)
- Total realized P&L for the current strategy cycle
- Current drawdown (from peak equity this cycle)
- Daily P&L (from UTC midnight)
- Total exposure percentage
- Win rate for the current cycle (completed trades)
- Count of open positions
- Count of pending orders
- Strategy mode (`paper` / `live` / `safe` / `safe_idle`)
- Strategy expiry time and time remaining
- Last evaluation tick timestamp
- WebSocket feed status and last tick timestamp per asset

**Acceptance Criteria:**
- [ ] The `/metrics` endpoint returns all metrics listed above in a single JSON response.
- [ ] Equity and P&L values are denominated in the base currency (USD).
- [ ] The endpoint responds within 100ms under normal load.
- [ ] Metrics correctly reflect a position being closed: unrealized P&L decreases and realized P&L increases by the correct amount.

**Edge Cases:**
- No trades have completed in the current cycle. Win rate must be returned as `null` or `0`, not a division-by-zero error.
- The account balance query to the exchange fails. Cached equity from the last successful query is returned with a `stale_since` timestamp indicating the cached value's age.

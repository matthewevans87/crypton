# Crypton.Api.ExecutionService — Implementation Reference

**Last updated:** February 2026  
**Runtime:** .NET 10, ASP.NET Core 10  
**Port:** 5004  
**Test count:** 215 (all passing)

---

## Table of Contents

1. [Overview](#overview)
2. [Directory Structure](#directory-structure)
3. [Running the Service](#running-the-service)
4. [Configuration Reference](#configuration-reference)
5. [Architecture](#architecture)
6. [Domain Models](#domain-models)
7. [Strategy Management](#strategy-management)
8. [Condition DSL](#condition-dsl)
9. [Execution Engine](#execution-engine)
10. [Order & Position Management](#order--position-management)
11. [Portfolio Risk Enforcement](#portfolio-risk-enforcement)
12. [Operation Modes](#operation-modes)
13. [Exchange Adapters](#exchange-adapters)
14. [Resilience & Safe Mode](#resilience--safe-mode)
15. [Structured Event Log](#structured-event-log)
16. [REST API](#rest-api)
17. [SignalR Streaming Hub](#signalr-streaming-hub)
18. [CLI Mode](#cli-mode)
19. [Dependency Injection Map](#dependency-injection-map)
20. [Testing](#testing)
21. [Containerisation](#containerisation)

---

## Overview

`Crypton.Api.ExecutionService` is the autonomous trade execution arm of the Crypton system. It receives a `strategy.json` document written by the Learning Loop (Agent Runner), validates it, watches for changes, evaluates entry and exit conditions on every market tick, and routes orders through a pluggable exchange adapter.

**Key design principles:**

- **Paper-first safety.** The service always starts in paper mode. Promotion to live requires explicit operator action via an authenticated REST call.
- **Persistence across restarts.** All mutable state (open positions, operation mode, safe mode, failure count) is atomically persisted to disk using a write-temp-then-rename pattern.
- **Zero silent failures.** Every significant event is appended to a structured NDJSON event log that cannot be bypassed.
- **Full interface abstraction.** No execution logic references any exchange-specific type directly; all exchange interaction goes through `IExchangeAdapter`.

---

## Directory Structure

```
Crypton.Api.ExecutionService/
├── Api/
│   ├── ApiKeyAuthFilter.cs       # IActionFilter that validates X-Api-Key on write endpoints
│   └── Controllers.cs            # All REST controllers (one file, multiple classes)
├── Cli/
│   └── CliRunner.cs              # CLI verb dispatch (status, safe-mode, strategy, …)
├── Configuration/
│   ├── AgentRunnerConfig.cs      # (reserved)
│   ├── ExecutionServiceConfig.cs # All config POCOs
│   └── ServiceCollectionExtensions.cs  # AddExecutionServiceCore() DI registration
├── Exchange/
│   ├── IExchangeAdapter.cs       # Core abstraction interface
│   ├── ExchangeAdapterException.cs  # AuthenticationException, RateLimitException, OrderNotFoundException
│   ├── KrakenWebSocketAdapter.cs # Kraken WS v2 — market data only
│   └── KrakenRestAdapter.cs      # Kraken REST v0 — orders & account
├── Execution/
│   ├── MarketDataHub.cs          # IHostedService: subscribes to exchange data, caches snapshots
│   ├── EntryEvaluator.cs         # Evaluates entry conditions on each tick
│   ├── ExitEvaluator.cs          # Evaluates all exit conditions on each tick
│   └── ExecutionEngine.cs        # IHostedService: ties market data → entry → exit pipeline
├── Hubs/
│   ├── ExecutionHub.cs           # SignalR hub (subscribe/unsubscribe to 4 streams)
│   └── ExecutionHubBroadcaster.cs  # IHostedService: pushes updates to hub groups
├── Logging/
│   ├── IEventLogger.cs           # LogAsync, GetRecentAsync, OnEventLogged event
│   ├── ExecutionEvent.cs         # NDJSON record + EventTypes constants
│   ├── FileEventLogger.cs        # Thread-safe, daily rotation, in-memory ring buffer
│   └── InMemoryEventLogger.cs    # Test double
├── Metrics/
│   └── MetricsCollector.cs       # Order/fill/rejection counters
├── Models/
│   ├── Strategy.cs               # StrategyDocument, PortfolioRisk, StrategyPosition, …
│   ├── MarketSnapshot.cs         # Bid, Ask, Mid, Indicators dict
│   └── ExchangeModels.cs         # PlaceOrderRequest, OrderAcknowledgement, AccountBalance, …
├── OperationMode/
│   ├── IOperationModeService.cs
│   ├── OperationModeService.cs   # Persisted paper/live mode state
│   ├── IMarketDataSource.cs      # Injected data feed abstraction
│   ├── PaperTradingAdapter.cs    # IExchangeAdapter simulation
│   └── DelegatingExchangeAdapter.cs  # Routes paper↔live at runtime
├── Orders/
│   ├── OrderRecord.cs            # Order lifecycle state
│   ├── OrderRouter.cs            # Dispatch, deduplication, fill handling
│   └── PositionSizingCalculator.cs  # Lot-rounded, capped order sizing
├── Positions/
│   ├── OpenPosition.cs           # Open position state
│   ├── ClosedTrade.cs            # Closed trade record
│   ├── PositionRegistry.cs       # Thread-safe authoritative position store
│   └── PortfolioRiskEnforcer.cs  # Drawdown/exposure/daily-loss enforcement
├── Resilience/
│   ├── FailureTracker.cs         # Persisted consecutive failure counter
│   ├── ISafeModeController.cs
│   ├── SafeModeController.cs     # Persisted safe mode state + close-all on activation
│   ├── ReconciliationService.cs  # Post-restart position sync with exchange
│   └── ResilienceWiring.cs       # IHostedService: wires FailureTracker → SafeModeController
├── Strategy/
│   ├── IStrategyService.cs
│   ├── StrategyService.cs        # FileSystemWatcher, hot-reload, validity window monitor
│   ├── StrategyValidator.cs      # Full schema validation
│   ├── CompiledStrategy.cs       # Pre-compiled ICondition trees per position
│   └── Conditions/
│       ├── ICondition.cs         # bool? Evaluate(snapshots)
│       ├── ConditionNode.cs      # PriceComparison, IndicatorComparison, Crossing, And, Or, Not
│       └── ConditionParser.cs    # Recursive-descent DSL parser
├── Program.cs                    # Dual-mode entry point
├── config.yaml                   # Default configuration (loaded as Content)
└── Dockerfile                    # Multi-stage build, port 5004
```

---

## Running the Service

### Service mode (default)

```bash
dotnet run --project src/Crypton.Api.ExecutionService
```

The service starts on port 5004. Configuration is read from `config.yaml` in the output directory, then overridden by environment variables and `appsettings.json`.

### CLI mode

The service detects CLI mode if the first argument is a known verb:

| Verb                          | Description                                                                  |
| ----------------------------- | ---------------------------------------------------------------------------- |
| `status`                      | Print current mode, safe-mode state, strategy state, and open position count |
| `safe-mode activate <reason>` | Activate safe mode                                                           |
| `safe-mode deactivate`        | Deactivate safe mode                                                         |
| `strategy reload`             | Force immediate re-read of the strategy file                                 |
| `promote-to-live <note>`      | Promote the service to live trading mode                                     |
| `demote-to-paper <note>`      | Demote back to paper trading                                                 |

To force service mode even when a verb is passed (e.g., in integration tests):

```bash
dotnet run -- status --service
```

### Docker

```bash
docker build -t crypton-execution-service .
docker run -p 5004:5004 \
  -e KRAKEN_API_KEY=<key> \
  -e KRAKEN_SECRET_KEY=<secret> \
  -e EXECUTION_SERVICE_API_KEY=<api-key> \
  -v $(pwd)/artifacts/execution:/app/artifacts \
  -v $(pwd)/artifacts/logs:/app/logs \
  crypton-execution-service
```

---

## Configuration Reference

All settings live under the `execution_service:` YAML key (or the `ExecutionService__` environment variable prefix).

### `kraken`

| Key                       | Type   | Default                  | Description                                         |
| ------------------------- | ------ | ------------------------ | --------------------------------------------------- |
| `api_key`                 | string | `""`                     | Kraken API key. Use env var `KRAKEN_API_KEY`.       |
| `api_secret`              | string | `""`                     | Kraken API secret. Use env var `KRAKEN_SECRET_KEY`. |
| `ws_base_url`             | string | `wss://ws.kraken.com/v2` | WebSocket base URL                                  |
| `rest_base_url`           | string | `https://api.kraken.com` | REST base URL                                       |
| `max_reconnect_attempts`  | int    | `5`                      | WS reconnect attempts before giving up              |
| `reconnect_delay_seconds` | int    | `2`                      | Seconds to wait between WS reconnect attempts       |

### `paper_trading`

| Key                   | Type    | Default  | Description                                                             |
| --------------------- | ------- | -------- | ----------------------------------------------------------------------- |
| `initial_balance_usd` | decimal | `10000`  | Starting simulated USD balance                                          |
| `slippage_pct`        | decimal | `0.001`  | Fill slippage fraction (buys fill at mid × 1.001, sells at mid × 0.999) |
| `commission_rate`     | decimal | `0.0026` | Commission fraction of notional value                                   |

### `strategy`

| Key                    | Type   | Default                   | Description                                                                                       |
| ---------------------- | ------ | ------------------------- | ------------------------------------------------------------------------------------------------- |
| `watch_path`           | string | `artifacts/strategy.json` | Path to the strategy JSON file to watch                                                           |
| `reload_latency_ms`    | int    | `5000`                    | Debounce delay after file change before re-loading                                                |
| `on_load_trigger_mode` | string | `fresh_crossing`          | `fresh_crossing` (require condition to transition false→true) or `immediate` (evaluate instantly) |

### `safety`

| Key                             | Type   | Default                | Description                                          |
| ------------------------------- | ------ | ---------------------- | ---------------------------------------------------- |
| `consecutive_failure_threshold` | int    | `3`                    | Order failures before automatic safe-mode activation |
| `dms_timeout_seconds`           | int    | `60`                   | Dead man's switch heartbeat timeout                  |
| `dms_check_interval_seconds`    | int    | `5`                    | How often the DMS timer is checked                   |
| `resilience_state_path`         | string | `artifacts/resilience` | Directory for persisted resilience state files       |

### `logging`

| Key              | Type   | Default                        | Description                             |
| ---------------- | ------ | ------------------------------ | --------------------------------------- |
| `event_log_path` | string | `logs/execution_events.ndjson` | NDJSON event log file path              |
| `rotate_daily`   | bool   | `true`                         | Append date suffix to log file each day |
| `retain_files`   | int    | `7`                            | Number of rotated files to keep         |

### `api`

| Key       | Type   | Default | Description                                                                       |
| --------- | ------ | ------- | --------------------------------------------------------------------------------- |
| `api_key` | string | `""`    | Bearer key for operator write endpoints. Use env var `EXECUTION_SERVICE_API_KEY`. |

### `streaming`

| Key                 | Type | Default | Description                                            |
| ------------------- | ---- | ------- | ------------------------------------------------------ |
| `metrics_update_hz` | int  | `1`     | Frequency of `MetricsUpdate` SignalR pushes per second |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    ExecutionEngine (IHostedService)              │
│                                                                  │
│  ┌──────────────┐   tick   ┌──────────────┐  ┌──────────────┐  │
│  │ MarketDataHub │ ──────► │ EntryEvaluator│  │ ExitEvaluator│  │
│  │  (IHostedSvc) │         │              │  │              │  │
│  └──────┬───────┘         └──────┬───────┘  └──────┬───────┘  │
│         │                        │                  │           │
│         │ IExchangeAdapter        │ OrderRouter      │ OrderRouter│
└─────────┼────────────────────────┼──────────────────┼───────────┘
          │                        │                  │
          ▼                        ▼                  ▼
  ┌───────────────────┐   ┌────────────────┐  ┌──────────────────┐
  │ DelegatingAdapter  │   │ PositionRegistry│  │PortfolioRisk     │
  │ (paper or live)   │   │ (disk persist) │  │Enforcer          │
  └──────┬────────────┘   └────────────────┘  └──────────────────┘
         │
    ┌────┴─────────────────────┐
    │                          │
    ▼                          ▼
KrakenWebSocket           KrakenRest
Adapter                   Adapter
(market data)             (orders/account)
```

**Startup sequence:**

1. `Program.cs` builds the host and registers all services via `AddExecutionServiceCore()`
2. `ResilienceWiring` (hosted service) wires `FailureTracker.OnSafeModeTriggered → SafeModeController.ActivateAsync`
3. `ReconciliationService` runs once: syncs `PositionRegistry` against the exchange
4. `StrategyService` starts the `FileSystemWatcher` on the configured watch path
5. `MarketDataHub` subscribes to market data for the currently active strategy's assets
6. `ExecutionEngine` starts receiving ticks and driving `EntryEvaluator` + `ExitEvaluator`
7. `ExecutionHubBroadcaster` begins periodic pushes to connected SignalR clients

---

## Domain Models

### `StrategyDocument`

The formal contract written by the Agent Runner and consumed by the Execution Service.

```json
{
  "mode": "paper",
  "validity_window": "2026-02-28T23:59:59Z",
  "posture": "moderate",
  "posture_rationale": "Market is range-bound with moderate volatility.",
  "portfolio_risk": {
    "max_drawdown_pct": 0.10,
    "daily_loss_limit_usd": 500.00,
    "max_total_exposure_pct": 0.80,
    "max_per_position_pct": 0.20,
    "safe_mode_triggers": ["consecutive_failures", "daily_loss"]
  },
  "positions": [
    {
      "id": "btc-long-001",
      "asset": "BTC/USD",
      "direction": "long",
      "allocation_pct": 0.15,
      "entry_type": "conditional",
      "entry_condition": "RSI(14, BTC/USD) < 35 AND price(BTC/USD) > 45000",
      "take_profit_targets": [
        { "price": 52000, "close_pct": 0.50 },
        { "price": 56000, "close_pct": 0.50 }
      ],
      "stop_loss": { "type": "trailing", "trail_pct": 0.03 },
      "time_exit_utc": "2026-02-28T18:00:00Z",
      "invalidation_condition": "price(BTC/USD) < 43000"
    }
  ],
  "strategy_rationale": "RSI oversold bounce play with defined risk."
}
```

**Posture values:**

| Value        | Meaning                                            |
| ------------ | -------------------------------------------------- |
| `aggressive` | Full position sizing, widest risk parameters       |
| `moderate`   | Default operational mode                           |
| `defensive`  | Reduced sizing                                     |
| `flat`       | No new entries, hold existing positions            |
| `exit_all`   | Close all open positions immediately, then go flat |

**Entry types:**

| Value         | Behaviour                                         |
| ------------- | ------------------------------------------------- |
| `market`      | Enter immediately on strategy load                |
| `limit`       | Enter when live price reaches `entry_limit_price` |
| `conditional` | Enter when `entry_condition` DSL evaluates `true` |

**Stop-loss types:**

| Value      | Required fields | Behaviour                                                                   |
| ---------- | --------------- | --------------------------------------------------------------------------- |
| `hard`     | `price`         | Exit when bid ≤ price (long) or ask ≥ price (short)                         |
| `trailing` | `trail_pct`     | Stop tracks price at `trail_pct` distance; never moves against the position |

### `MarketSnapshot`

```csharp
public sealed class MarketSnapshot
{
    public required string Asset { get; init; }
    public required decimal Bid { get; init; }
    public required decimal Ask { get; init; }
    public decimal Mid => (Bid + Ask) / 2;
    public required DateTimeOffset Timestamp { get; init; }
    public IReadOnlyDictionary<string, decimal> Indicators { get; init; } = new Dictionary<string, decimal>();
}
```

Indicators are keyed as `"{NAME}_{PERIOD}"` (e.g. `"RSI_14"`, `"EMA_20"`). They are populated by whatever `IMarketDataSource` is active; in live mode the Market Data service is expected to provide them.

---

## Strategy Management

### `StrategyService`

- Implements `IHostedService` and `IDisposable`
- Watches the configured file path using `FileSystemWatcher` with a debounce of `ReloadLatencyMs` to avoid acting on partial writes
- On each file change: reads and deserialises the JSON, runs full validation, generates a SHA256 `Id`, emits `OnStrategyLoaded` event
- Runs a background loop every `ValidityCheckIntervalMs` to monitor the `validity_window` — transitions to `StrategyState.Expired` when the deadline passes
- `ForceReloadAsync()` immediately re-reads the file without waiting for a file-system event

**`StrategyState` enum:**

| Value     | Meaning                                                         |
| --------- | --------------------------------------------------------------- |
| `None`    | No strategy has been loaded yet                                 |
| `Active`  | Strategy is loaded, validated, and within its `validity_window` |
| `Expired` | `validity_window` has passed                                    |
| `Invalid` | Last file load failed validation                                |

### `StrategyValidator`

Returns a list of `StrategyValidationError` records. An empty list means the strategy is valid. Errors are accumulated (not short-circuited) so the operator receives a complete picture.

Validates:
- `mode` is `"paper"` or `"live"`
- `posture` is one of the five valid values
- `validity_window` is in the future
- All `PortfolioRisk` decimals are in valid ranges
- Each `StrategyPosition` has required fields, valid `direction`, consistent `entry_type` / `entry_condition` / `entry_limit_price`
- Take-profit `close_pct` values sum to ≤ 1.0 per position
- Stop-loss fields match its declared type

### `CompiledStrategy`

At strategy load time, `CompiledStrategy.Compile(doc, parser)` pre-parses all DSL condition strings into `ICondition` trees. During the hot execution path, `EntryEvaluator` and `ExitEvaluator` call `Evaluate()` on the pre-built trees with no parsing overhead.

---

## Condition DSL

Conditions are expressed as a small domain-specific language parsed by `ConditionParser`.

### Syntax

| Expression                   | Example                                              | Description                                                      |
| ---------------------------- | ---------------------------------------------------- | ---------------------------------------------------------------- |
| `price(ASSET)`               | `price(BTC/USD)`                                     | Current mid-price of an asset                                    |
| `INDICATOR(PERIOD, ASSET)`   | `RSI(14, BTC/USD)`                                   | Named indicator from the snapshot                                |
| Comparison                   | `price(BTC/USD) > 50000`                             | `>`, `<`, `>=`, `<=`, `==`                                       |
| `crosses_above(EXPR, VALUE)` | `crosses_above(price(ETH/USD), 3000)`                | True on the tick when the expression crosses above the threshold |
| `crosses_below(EXPR, VALUE)` | `crosses_below(RSI(14, BTC/USD), 30)`                | True on the tick when the expression crosses below the threshold |
| `AND(EXPR, EXPR)`            | `AND(RSI(14, BTC/USD) < 35, price(BTC/USD) > 45000)` | Both sub-conditions true                                         |
| `OR(EXPR, EXPR)`             | `OR(price(BTC/USD) > 55000, price(ETH/USD) > 3500)`  | Either sub-condition true                                        |
| `NOT(EXPR)`                  | `NOT(price(BTC/USD) > 60000)`                        | Logical negation                                                 |

### Return semantics

`ICondition.Evaluate()` returns `bool?`:

- `true` — condition is satisfied
- `false` — condition is not satisfied
- `null` — required indicator data is not yet available (the entry is skipped with reason `"indicator_not_ready"`)

### Indicator key format

Indicator names are normalised to uppercase with the period appended after an underscore: `RSI(14, BTC/USD)` looks up the key `"RSI_14"` in the snapshot's `Indicators` dictionary.

---

## Execution Engine

### `MarketDataHub`

- Calls `IExchangeAdapter.SubscribeToMarketDataAsync` for the assets in the active strategy
- Re-subscribes automatically when `StrategyService.OnStrategyLoaded` fires
- Maintains `Dictionary<string, MarketSnapshot> _snapshots` (the most recent snapshot per asset)
- Exposes `GetAllSnapshots()` and `LastTickAt`
- Raises `OnSnapshot` event on every incoming tick, which `ExecutionEngine` subscribes to

### `EntryEvaluator` (ES-CEE-001)

Called on every tick. For each strategy position not yet entered:

1. **Posture guard** — skips all entries if `posture` is `flat` or `exit_all`
2. **Risk gate** — calls `PortfolioRiskEnforcer.EvaluateAsync`; skips entries if `EntriesSuspended`
3. **Condition evaluation** per position:
   - `market` → enter immediately (once per strategy cycle)
   - `limit` → enter when bid ≤ limit price (long) or ask ≥ limit price (short)
   - `conditional` → evaluate the compiled `ICondition`; `null` result → skip with `entry_skipped` + `indicator_not_ready`
4. **Sizing** — calls `PositionSizingCalculator.CalculateAsync`
5. **Dispatch** — calls `OrderRouter.PlaceEntryOrderAsync`
6. **Deduplication** — tracks dispatched positions in `HashSet<string>`; reset on strategy reload

### `ExitEvaluator` (ES-CEE-002)

Called on every tick for each open position:

1. **`exit_all` posture** — dispatch market-sell for everything immediately
2. **Unrealized P&L update** — calls `PositionRegistry.UpdateUnrealizedPnl`
3. **Trailing stop update** — if `stop_loss.type == "trailing"`, advance the stop level when price moves in the position's favour (never move it against)
4. **Hard stop-loss check** — bid ≤ stop price for longs, ask ≥ stop price for shorts
5. **Trailing stop trigger** — same price vs. `OpenPosition.TrailingStopPrice`
6. **Time exit** — `DateTimeOffset.UtcNow >= time_exit_utc`
7. **Invalidation condition** — evaluate DSL; close on `true`
8. **Scaled take-profit** — fire partial closes in order (target N requires target N-1 to be already hit); update `TakeProfitTargetsHit` on `OpenPosition`

All exit dispatches are guarded with a `HashSet<string>` to prevent double-close.

---

## Order & Position Management

### `OrderRouter`

- Maintains `Dictionary<string, OrderRecord> ActiveOrders`
- **Deduplication**: before dispatching, checks whether an active order already exists for the same `StrategyPositionId`
- On successful fill: calls `PositionRegistry.OpenPosition` (entry) or `ClosePosition` (exit)
- On exception: calls `FailureTracker.RecordFailure()`; on success: calls `RecordSuccess()`

### `PositionSizingCalculator`

```
quantity = (totalCapital × effectivePct) / currentPrice
```

- `effectivePct = min(allocation_pct, max_per_position_pct)`
- Rounds DOWN to the asset's lot increment (e.g., 0.0001 BTC, 0.001 ETH)
- Returns `null` if the rounded quantity is below the minimum lot size

### `PositionRegistry`

- Thread-safe with `Lock`
- Persists atomically to `positions.json` (open positions) and `trades.json` (closed trades) using write-to-temp-then-rename
- `Load()` is called once on startup
- Exposes `OnPositionChanged` event (used by `ExecutionHubBroadcaster`)
- `ApplyPartialFill` updates `AverageEntryPrice` incrementally:  
  `newAvg = (prevAvg × prevQty + fillPrice × fillQty) / (prevQty + fillQty)`

### `OpenPosition`

| Field                  | Type           | Description                                         |
| ---------------------- | -------------- | --------------------------------------------------- |
| `Id`                   | string         | Internal UUID                                       |
| `StrategyPositionId`   | string         | Links back to `StrategyPosition.Id`                 |
| `StrategyId`           | string         | SHA256 of the strategy document                     |
| `Asset`                | string         | e.g. `"BTC/USD"`                                    |
| `Direction`            | string         | `"long"` or `"short"`                               |
| `Quantity`             | decimal        | Current quantity held                               |
| `AverageEntryPrice`    | decimal        | Volume-weighted average                             |
| `TrailingStopPrice`    | decimal?       | Current trailing stop level — updated on every tick |
| `TakeProfitTargetsHit` | HashSet\<int\> | Indices of take-profit targets already triggered    |
| `UnrealizedPnl`        | decimal        | Updated on every tick                               |
| `Origin`               | string         | `"strategy"`, `"reconciled"`, or `"external"`       |

---

## Portfolio Risk Enforcement

`PortfolioRiskEnforcer` is a stateful singleton that maintains drawdown, exposure, and daily-loss counters.

### Checks

| Limit          | Field                 | Trigger condition                                     |
| -------------- | --------------------- | ----------------------------------------------------- |
| Total exposure | `MaxTotalExposurePct` | Sum of all open position cost ≥ cap                   |
| Max drawdown   | `MaxDrawdownPct`      | Current equity ≤ peak equity × (1 − max_drawdown_pct) |
| Daily loss     | `DailyLossLimitUsd`   | Cumulative realised loss today ≥ limit                |

### Hysteresis

Exposure suspension uses hysteresis to prevent rapid on/off cycling:
- Suspends when `exposurePct >= MaxTotalExposurePct`
- Resumes when `exposurePct < MaxTotalExposurePct × 0.95`

### Safe mode

When `SafeModeTriggered` is true (set by drawdown or daily-loss breach), no entries are allowed and `SafeModeController.ActivateAsync` is called.

---

## Operation Modes

### Mode state machine

```
          OperatorController POST /operator/mode/promote-to-live
                          ┌─────────────────────►
     ┌────────────┐        │                      ┌────────────┐
     │   paper    │◄───────┘                      │    live    │
     └────────────┘        ◄────────────────────── └────────────┘
          (default)    OperatorController POST /operator/mode/demote-to-paper
```

Mode is persisted to `{ResilienceStatePath}/operation_mode.json`. The service never enters live mode automatically — it always requires an authenticated operator action.

### `DelegatingExchangeAdapter`

Routes all `IExchangeAdapter` calls to the correct concrete implementation at runtime:

| Mode    | `SubscribeToMarketDataAsync` | All other methods     |
| ------- | ---------------------------- | --------------------- |
| `paper` | `PaperTradingAdapter`        | `PaperTradingAdapter` |
| `live`  | `KrakenWebSocketAdapter`     | `KrakenRestAdapter`   |

`SetLiveAdapters(wsAdapter, restAdapter)` wires in the live adapters. Until called, the service operates in paper mode.

### `PaperTradingAdapter`

Simulates exchange behaviour:
- Uses the most recent `MarketSnapshot.Mid` for fill prices
- Buys fill at `mid × (1 + slippage_pct)`, sells at `mid × (1 − slippage_pct)`
- Commission is deducted from fill proceeds: `notional × commission_rate`
- Returns `OrderAcknowledgement.Status = Filled` synchronously
- Returns `Status = Rejected` if no snapshot is available for the asset
- `GetAccountBalanceAsync` returns `InitialBalanceUsd` minus cost of open positions

---

## Exchange Adapters

### `IExchangeAdapter` interface

```csharp
Task SubscribeToMarketDataAsync(IReadOnlyList<string> assets, Func<MarketSnapshot, Task> onSnapshot, CancellationToken ct);
Task<OrderAcknowledgement> PlaceOrderAsync(PlaceOrderRequest request, CancellationToken ct);
Task<CancellationResult> CancelOrderAsync(string exchangeOrderId, CancellationToken ct);
Task<OrderStatusResult> GetOrderStatusAsync(string exchangeOrderId, CancellationToken ct);
Task<AccountBalance> GetAccountBalanceAsync(CancellationToken ct);
Task<IReadOnlyList<ExchangePosition>> GetOpenPositionsAsync(CancellationToken ct);
Task<IReadOnlyList<Trade>> GetTradeHistoryAsync(DateTimeOffset since, CancellationToken ct);
bool IsRateLimited { get; }
DateTimeOffset? RateLimitResumesAt { get; }
```

### `KrakenWebSocketAdapter`

- Connects to `wss://ws.kraken.com/v2`
- Subscribes to the `ticker` channel for the requested asset symbols
- Auto-reconnects on disconnect with `ReconnectDelaySeconds` between attempts (max `MaxReconnectAttempts`)
- Raises the `onSnapshot` callback for each incoming ticker update
- Injectable `Func<ClientWebSocket>` factory enables unit-testing without a live connection
- All non-market-data methods throw `NotSupportedException`
- `IsRateLimited` is always `false`

### `KrakenRestAdapter`

- Base URL: `https://api.kraken.com/0/private/`
- **Authentication**: HMAC-SHA512 signature over `nonce + SHA256(nonce + post_body)`, encoded as Base64
- **Nonce**: current Unix timestamp in milliseconds

| Method                   | Kraken endpoint                 |
| ------------------------ | ------------------------------- |
| `PlaceOrderAsync`        | `POST /0/private/AddOrder`      |
| `CancelOrderAsync`       | `POST /0/private/CancelOrder`   |
| `GetOrderStatusAsync`    | `POST /0/private/QueryOrders`   |
| `GetAccountBalanceAsync` | `POST /0/private/Balance`       |
| `GetOpenPositionsAsync`  | `POST /0/private/OpenPositions` |
| `GetTradeHistoryAsync`   | `POST /0/private/TradesHistory` |

**Error handling:**

| Kraken error                 | Thrown exception                                            |
| ---------------------------- | ----------------------------------------------------------- |
| `EAPI:Invalid key`           | `AuthenticationException`                                   |
| `EOrder:Rate limit exceeded` | `RateLimitException` (sets `IsRateLimited = true` for 60 s) |
| HTTP 401                     | `AuthenticationException`                                   |
| Other non-empty `error[]`    | `ExchangeAdapterException`                                  |
| txid not found in response   | `OrderNotFoundException`                                    |

`SubscribeToMarketDataAsync` throws `NotSupportedException` — use `KrakenWebSocketAdapter` for market data.

---

## Resilience & Safe Mode

### `FailureTracker`

- Counts consecutive order failures (incremented by `OrderRouter` on `PlaceOrderAsync` exception)
- Persists count to `{ResilienceStatePath}/failure_count.json` after every change
- On startup: if persisted count ≥ `ConsecutiveFailureThreshold`, `SafeModeTriggered` is immediately `true`
- `RecordSuccess()` resets the counter to 0
- Raises `OnSafeModeTriggered` event when the threshold is crossed — `ResilienceWiring` wires this to `SafeModeController.ActivateAsync`

### `SafeModeController`

Safe mode persists across restarts via `{ResilienceStatePath}/safe_mode.json`.

**On activation:**
1. Persists `{ "active": true, "triggered_at": "...", "reason": "..." }`
2. Fetches all open positions from `PositionRegistry`
3. Dispatches a market-sell order for each via `OrderRouter`
4. Emits `safe_mode_activated` event

**On deactivation:**
1. Persists `{ "active": false }`
2. Calls `FailureTracker.Reset()`
3. Emits `safe_mode_deactivated` event

While safe mode is active: `EntryEvaluator` makes no entries, and `PortfolioRiskEnforcer.SafeModeTriggered` returns `true`.

### `ReconciliationService`

Runs once at startup (after `StrategyService` and before the first tick).

1. Skips entirely if `SafeModeController.IsActive`
2. Fetches `PositionRegistry.OpenPositions` and `IExchangeAdapter.GetOpenPositionsAsync()`
3. **Orphaned** (in registry, absent from exchange): calls `PositionRegistry.ClosePosition(..., "reconciled_missing")`, emits `position_closed` with `origin: "reconciliation"`
4. **Unknown** (on exchange, absent from registry): adds to registry with `Origin = "reconciled"`, emits `position_opened` with `origin: "reconciliation"`
5. Emits `reconciliation_summary` event with counts

If the exchange adapter throws, logs the error and continues without failing the startup.

---

## Structured Event Log

Every significant event is appended to the NDJSON event log. Each line is a `ExecutionEvent` object:

```json
{"timestamp":"2026-02-27T10:00:00Z","event_type":"entry_triggered","mode":"paper","service_version":"1.0.0","data":{"position_id":"btc-long-001","asset":"BTC/USD","entry_type":"conditional","quantity":0.03}}
```

### Event types

| Constant                | String value             | When emitted                                           |
| ----------------------- | ------------------------ | ------------------------------------------------------ |
| `ServiceStarted`        | `service_started`        | `ExecutionEngine.StartAsync`                           |
| `ServiceStopped`        | `service_stopped`        | `ExecutionEngine.StopAsync`                            |
| `StrategyLoaded`        | `strategy_loaded`        | `StrategyService` — successful load                    |
| `StrategyRejected`      | `strategy_rejected`      | `StrategyService` — validation failed                  |
| `StrategyExpired`       | `strategy_expired`       | `StrategyService` — validity window passed             |
| `StrategySwapped`       | `strategy_swapped`       | `StrategyService` — new strategy replaces old          |
| `EntryTriggered`        | `entry_triggered`        | `EntryEvaluator` — order about to be placed            |
| `EntrySkipped`          | `entry_skipped`          | `EntryEvaluator` — condition not met or data not ready |
| `ExitTriggered`         | `exit_triggered`         | `ExitEvaluator` — close order about to be placed       |
| `OrderPlaced`           | `order_placed`           | `OrderRouter` — after `PlaceOrderAsync` succeeds       |
| `OrderFilled`           | `order_filled`           | `OrderRouter.ApplyFillAsync`                           |
| `OrderPartiallyFilled`  | `order_partially_filled` | `OrderRouter.ApplyFillAsync`                           |
| `OrderCancelled`        | `order_cancelled`        | `OrderRouter.CancelOrderAsync`                         |
| `OrderRejected`         | `order_rejected`         | `PaperTradingAdapter` — no market data                 |
| `PositionOpened`        | `position_opened`        | `PositionRegistry.OpenPosition`                        |
| `PositionClosed`        | `position_closed`        | `PositionRegistry.ClosePosition`                       |
| `RiskLimitBreached`     | `risk_limit_breached`    | `PortfolioRiskEnforcer`                                |
| `SafeModeActivated`     | `safe_mode_activated`    | `SafeModeController.ActivateAsync`                     |
| `SafeModeDeactivated`   | `safe_mode_deactivated`  | `SafeModeController.DeactivateAsync`                   |
| `ReconciliationSummary` | `reconciliation_summary` | `ReconciliationService`                                |
| `ModeChanged`           | `mode_changed`           | `OperationModeService`                                 |

### Log rotation

`FileEventLogger` maintains a 1000-entry in-memory ring buffer for `GetRecentAsync`. The on-disk file is rotated daily when `rotate_daily: true`: the date is appended to the filename (`execution_events_2026-02-27.ndjson`). The logger trims files older than `retain_files` days on each rotation.

---

## REST API

All endpoints are on port 5004. Write endpoints require the `X-Api-Key` header matching `api.api_key`.

### Read endpoints (no auth required)

| Method | Path                | Description                                                                     |
| ------ | ------------------- | ------------------------------------------------------------------------------- |
| `GET`  | `/status`           | Mode, safe-mode state, strategy state, open position count, last tick timestamp |
| `GET`  | `/strategy`         | Full active `StrategyDocument`; 404 if none                                     |
| `GET`  | `/positions`        | All open positions                                                              |
| `GET`  | `/positions/{id}`   | Single position; 404 if not found                                               |
| `GET`  | `/orders`           | All active orders                                                               |
| `GET`  | `/trades`           | All closed trades                                                               |
| `GET`  | `/metrics`          | Order/fill/rejection counters                                                   |
| `GET`  | `/events?limit=100` | Last N events (max 1000) from the ring buffer                                   |
| `GET`  | `/health/live`      | 200 OK always (liveness probe)                                                  |

### Operator endpoints (`X-Api-Key` required)

| Method | Path                             | Body                         | Description                              |
| ------ | -------------------------------- | ---------------------------- | ---------------------------------------- |
| `POST` | `/operator/safe-mode/activate`   | `{ "reason": "..." }`        | Activate safe mode (close all positions) |
| `POST` | `/operator/safe-mode/deactivate` | _(none)_                     | Deactivate safe mode                     |
| `POST` | `/operator/mode/promote-to-live` | `{ "operator_note": "..." }` | Promote to live trading                  |
| `POST` | `/operator/mode/demote-to-paper` | `{ "operator_note": "..." }` | Demote to paper trading                  |
| `POST` | `/operator/strategy/reload`      | _(none)_                     | Force immediate strategy file re-read    |

**Auth error response (401):**
```json
{ "error": "Invalid or missing API key." }
```

---

## SignalR Streaming Hub

**Endpoint:** `/hubs/execution`  
**Client library:** `@microsoft/signalr`

Clients subscribe to named groups to receive real-time pushes.

### Client-to-server methods

```typescript
// Subscribe
connection.invoke("SubscribeToStatus");
connection.invoke("SubscribeToMetrics");
connection.invoke("SubscribeToEvents");
connection.invoke("SubscribeToPositions");

// Unsubscribe
connection.invoke("UnsubscribeFromStatus");
// ... etc
```

### Server-to-client events

| Event name       | Group       | Frequency                     | Payload                                                                       |
| ---------------- | ----------- | ----------------------------- | ----------------------------------------------------------------------------- |
| `StatusUpdate`   | `status`    | Every 2 seconds               | `{ mode, safe_mode, strategy_state, strategy_id, open_positions, timestamp }` |
| `MetricsUpdate`  | `metrics`   | `1/metrics_update_hz` seconds | Metrics snapshot from `MetricsCollector`                                      |
| `EventLog`       | `events`    | On every new event            | `ExecutionEvent` object                                                       |
| `PositionUpdate` | `positions` | On every position change      | `OpenPosition` object                                                         |

---

## CLI Mode

The service detects CLI mode when the first argument is a known verb and `--service` is not present.

```bash
# Check service status
dotnet run -- status

# Activate safe mode
dotnet run -- safe-mode activate "pre-maintenance window"

# Deactivate safe mode
dotnet run -- safe-mode deactivate

# Force strategy reload
dotnet run -- strategy reload

# Promote to live trading
dotnet run -- promote-to-live "risk sign-off complete"

# Demote to paper
dotnet run -- demote-to-paper "end of day"
```

CLI commands communicate with the running service by reading and modifying the persisted state files directly, so no HTTP call is made (the service does not need to be running for safe-mode or mode changes; the effect takes place on next startup or is picked up in the next state-check cycle).

---

## Dependency Injection Map

All services are registered by `AddExecutionServiceCore(IServiceCollection, IConfiguration)` in `ServiceCollectionExtensions.cs`.

| Registration                               | Lifetime                        | Notes                                      |
| ------------------------------------------ | ------------------------------- | ------------------------------------------ |
| `IEventLogger → FileEventLogger`           | Singleton                       | In-memory ring buffer + NDJSON file        |
| `OperationModeService`                     | Singleton                       | Also registered as `IOperationModeService` |
| `IMarketDataSource → NullMarketDataSource` | Singleton                       | Replace to inject real feed                |
| `PaperTradingAdapter`                      | Singleton                       |                                            |
| `DelegatingExchangeAdapter`                | Singleton                       | Also registered as `IExchangeAdapter`      |
| `StrategyValidator`                        | Singleton                       |                                            |
| `ConditionParser`                          | Singleton                       |                                            |
| `StrategyService`                          | Singleton + IHostedService      | Also registered as `IStrategyService`      |
| `PortfolioRiskEnforcer`                    | Singleton                       |                                            |
| `PositionRegistry`                         | Singleton (factory)             | Loads persisted state on construction      |
| `PositionSizingCalculator`                 | Singleton                       |                                            |
| `OrderRouter`                              | Singleton                       |                                            |
| `FailureTracker`                           | Singleton                       |                                            |
| `SafeModeController`                       | Singleton + ISafeModeController |                                            |
| `ResilienceWiring`                         | Singleton + IHostedService      | Wires failure→safemode event               |
| `ReconciliationService`                    | Singleton + IHostedService      | Runs once on startup                       |
| `MarketDataHub`                            | Singleton + IHostedService      |                                            |
| `EntryEvaluator`                           | Singleton                       |                                            |
| `ExitEvaluator`                            | Singleton                       |                                            |
| `ExecutionEngine`                          | Singleton + IHostedService      |                                            |
| `ApiKeyAuthFilter`                         | Scoped                          | Used via `[ServiceFilter]`                 |
| `MetricsCollector` + `IMetricsCollector`   | Singleton                       |                                            |
| `ExecutionHubBroadcaster`                  | Singleton + IHostedService      |                                            |

---

## Testing

### Framework

- **xUnit** — test runner
- **FluentAssertions** — assertion library
- **NSubstitute** — mocking and test doubles
- **coverlet.collector** — coverage collection

### Test project structure

```
Crypton.Api.ExecutionService.Tests/
├── Api/
│   ├── StatusControllerTests.cs    (5 tests)
│   └── OperatorControllerTests.cs  (8 tests)
├── Exchange/
│   ├── KrakenRestAdapterTests.cs   (12 tests)
│   └── KrakenWebSocketAdapterTests.cs (5 tests)
├── Execution/
│   ├── EntryEvaluatorTests.cs      (8 tests)
│   └── ExitEvaluatorTests.cs       (11 tests)
├── Logging/
│   ├── InMemoryEventLoggerTests.cs (8 tests)
│   └── FileEventLoggerTests.cs     (9 tests)
├── Models/
│   └── StrategyValidatorTests.cs   (38 tests)
├── OperationMode/
│   ├── OperationModeServiceTests.cs (8 tests)
│   └── PaperTradingAdapterTests.cs  (12 tests)
├── Orders/
│   ├── OrderRouterTests.cs          (8 tests)
│   └── PositionSizingCalculatorTests.cs (5 tests)
├── Positions/
│   ├── PositionRegistryTests.cs     (10 tests)
│   └── PortfolioRiskEnforcerTests.cs (7 tests)
├── Resilience/
│   ├── FailureTrackerTests.cs       (10 tests)
│   ├── SafeModeControllerTests.cs   (8 tests)
│   └── ReconciliationServiceTests.cs (6 tests)
├── Strategy/
│   ├── StrategyServiceTests.cs      (7 tests)
│   └── Conditions/
│       └── ConditionParserTests.cs  (22 tests)
└── TestServiceHost.cs               (builder stub for in-process tests)
```

**Total: 215 tests**

### Running tests

```bash
# All tests
dotnet test src/Crypton.Api.ExecutionService.Tests

# With coverage
dotnet test src/Crypton.Api.ExecutionService.Tests \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage

# Single test
dotnet test src/Crypton.Api.ExecutionService.Tests \
  --filter "HardStopLoss_Long_TriggersClose_WhenBidAtOrBelowStopPrice"
```

### Test patterns

**File-system dependent services** (e.g., `PositionRegistry`, `StrategyService`, `FailureTracker`) are tested with a unique temp directory created per test class and cleaned up in `IDisposable.Dispose()`.

**Exchange interactions** are tested against NSubstitute substitutes of `IExchangeAdapter`.

**HTTP interactions** (`KrakenRestAdapter`) are tested by injecting a `MockHttpMessageHandler` that captures requests and returns pre-configured responses.

**WebSocket parsing** (`KrakenWebSocketAdapter`) is tested via the `internal ProcessMessageAsync(string rawJson, callback)` method, which is exposed without triggering a real WebSocket connection.

---

## Containerisation

### Dockerfile

Multi-stage build following the same pattern as `Crypton.Api.AgentRunner`:

```dockerfile
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Crypton.Api.ExecutionService \
    -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_HTTP_PORTS=5004
EXPOSE 5004
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD curl -f http://localhost:5004/health/live || exit 1
ENTRYPOINT ["dotnet", "Crypton.Api.ExecutionService.dll"]
```

### `docker-compose.yml` entry

```yaml
execution-service:
  build:
    context: .
    dockerfile: src/Crypton.Api.ExecutionService/Dockerfile
  ports:
    - "5004:5004"
  environment:
    - KRAKEN_API_KEY=${KRAKEN_API_KEY}
    - KRAKEN_SECRET_KEY=${KRAKEN_SECRET_KEY}
    - EXECUTION_SERVICE_API_KEY=${EXECUTION_SERVICE_API_KEY}
  volumes:
    - ./artifacts/execution:/app/artifacts
    - ./artifacts/logs:/app/logs
  depends_on:
    market-data-service:
      condition: service_healthy
  healthcheck:
    test: ["CMD", "curl", "-f", "http://localhost:5004/health/live"]
    interval: 30s
    timeout: 5s
    retries: 3
```

### Testing outside a container

Because all dependencies are injected and file paths are configurable, the service runs and tests completely without Docker:

```bash
# Run all tests (no Docker required)
dotnet test src/Crypton.Api.ExecutionService.Tests

# Run the service locally (paper mode, no exchange credentials needed)
dotnet run --project src/Crypton.Api.ExecutionService
```

No test requires a live network connection. The Kraken adapters are tested with mocked HTTP/WebSocket handlers.

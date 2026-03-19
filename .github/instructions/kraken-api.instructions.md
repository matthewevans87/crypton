---
applyTo: |
  src/Crypton.Api.ExecutionService/Exchange/**,
  src/Crypton.Api.ExecutionService.Tests/Exchange/**,
  src/Crypton.Api.MarketData/Adapters/**,
  src/Crypton.Api.MarketData.Tests/KrakenExchangeAdapterTests.cs
description: "Use when working on Kraken API integration — REST, WebSocket (public and authenticated), exchange adapters, order/fill handling, or related tests."
---

# Kraken API Integration

## Architecture Overview

Three distinct Kraken adapters exist. Do not conflate them.

| Adapter | Service | Transport | Auth | Purpose |
|---|---|---|---|---|
| `KrakenRestAdapter` | ExecutionService | REST `https://api.kraken.com` | HMAC-SHA512 | Orders, account, WS token |
| `KrakenWebSocketAdapter` | ExecutionService | WS `wss://ws.kraken.com/v2` | None | Market data (`ticker`) |
| `KrakenWsExecutionAdapter` | ExecutionService | WS `wss://ws-auth.kraken.com/v2` | WS token | Live fill events (`executions`) |
| `KrakenExchangeAdapter` | MarketDataService | WS `wss://ws.kraken.com/v2` + REST | Optional | Prices, order book, trades |

`DelegatingExchangeAdapter` routes `IExchangeAdapter` calls: `SubscribeToMarketDataAsync` → `KrakenWebSocketAdapter`; all other calls → `KrakenRestAdapter`. In paper mode both are bypassed in favour of `PaperTradingAdapter`.

---

## REST API (ExecutionService)

**Base URL:** configured via `KrakenAdapterConfig.RestBaseUrl` (default `https://api.kraken.com`).

All private endpoints use `POST`. No private endpoint ever uses `GET`.

### Endpoints Used

| Endpoint | Method |
|---|---|
| `/0/private/AddOrder` | `PlaceOrderAsync` |
| `/0/private/CancelOrder` | `CancelOrderAsync` |
| `/0/private/QueryOrders` | `GetOrderStatusAsync` |
| `/0/private/Balance` | `GetAccountBalanceAsync` |
| `/0/private/OpenPositions` | `GetOpenPositionsAsync` (margin only — returns empty for spot) |
| `/0/private/TradesHistory` | `GetTradeHistoryAsync` |
| `/0/private/GetWebSocketsToken` | `GetWebSocketsTokenAsync` |

### Authentication

Every private request requires two HTTP headers: `API-Key` and `API-Sign`.

```
signature_input = UTF8(uri_path) ++ SHA256_raw( UTF8(nonce + url_encoded_post_body) )
API-Sign        = Base64( HMAC-SHA512( Base64Decode(api_secret), signature_input ) )
```

- `nonce` = `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString()` — always included in `postData`
- `api_secret` is stored as a Base64 string; decode to raw bytes before use as the HMAC key

### Rate Limiting & Error Handling

Kraken errors are returned as a non-empty `error` array in the JSON body, even on HTTP 200.

`HandleKrakenError(string errMsg)` maps Kraken error codes to typed exceptions:

| Kraken error string | Exception thrown |
|---|---|
| `EOrder:Rate limit exceeded` | `RateLimitException` → sets `IsRateLimited = true` for 60 s |
| `EAPI:Rate limit exceeded` | `RateLimitException` → sets `IsRateLimited = true` for 60 s |
| `EAPI:Invalid key` | `AuthenticationException` |
| HTTP 401 | `AuthenticationException` (detected before JSON parsing) |
| Anything else | `ExchangeAdapterException` |
| `result[txid]` missing | `OrderNotFoundException` (thrown in `GetOrderStatusAsync`) |

Exception hierarchy: `ExchangeAdapterException` → `AuthenticationException`, `RateLimitException`, `OrderNotFoundException`.

`IsRateLimited` auto-clears when the clock passes `_rateLimitResumesAt` (checked under `_rateLimitLock`).

### Key Field Mappings

- Kraken cash balance key is `ZUSD` or `USD` — both map to `AccountBalance.AvailableUsd`
- `AddOrder` response gives `result.txids[0]` — stored as `OrderAcknowledgement.ExchangeOrderId`
- `QueryOrders` fills: `vol_exec` (string) → `FilledQuantity`; `price` (string) → `AverageFillPrice`
- `TradesHistory`: `time` is a Unix timestamp in **seconds** (float)
- `OpenPositions`: returns margin positions only — Crypton uses spot, so this is rarely non-empty

---

## WebSocket — Public (`KrakenWebSocketAdapter`)

**URL:** `wss://ws.kraken.com/v2` (from `KrakenAdapterConfig.WsBaseUrl`)

**Channel:** `ticker` only. All other channels (heartbeat, status, book) are ignored.

**Subscribe:**
```json
{ "method": "subscribe", "params": { "channel": "ticker", "symbol": ["BTC/USD"] } }
```

**Incoming data (fields extracted):** `symbol`, `bid`, `ask`, `timestamp` → `MarketSnapshot { Asset, Bid, Ask, Timestamp }`.

**Reconnect:** fixed delay (`ReconnectDelaySeconds`), up to `MaxReconnectAttempts`. No exponential backoff.

---

## WebSocket — Authenticated Executions (`KrakenWsExecutionAdapter`)

**URL:** `wss://ws-auth.kraken.com/v2` (hardcoded constant)

**Channel:** `executions`

**Subscribe:**
```json
{ "method": "subscribe", "params": { "channel": "executions", "token": "<wsToken>", "snap_orders": true, "snap_trades": false } }
```

`snap_orders: true` delivers a snapshot of current open orders on connect. `snap_trades: false` — do not request historical fills.

### Token Lifecycle

Token = `POST /0/private/GetWebSocketsToken`. Lifetime: 15 minutes (valid indefinitely while sub is active).
- Fetched fresh on every (re)connect
- Proactive reconnect after **12 minutes** (`TokenRefreshInterval`) to avoid expiry mid-session
- Reconnect is triggered by closing the WS with `"token_refresh"` reason; the outer loop re-fetches

### Fill Processing

Only `exec_type: "trade"` and `exec_type: "filled"` are processed. All other values are skipped.

| Field | Usage |
|---|---|
| `order_id` | Maps to `OrderRouter._exchangeToInternalId` |
| `last_qty` | Fill quantity |
| `avg_price` | Used for cost-basis (preferred over `last_price`) |
| `timestamp` | ISO 8601, parsed with `RoundtripKind` |
| `order_status == "filled" \|\| exec_type == "filled"` | `isFullFill = true` |

Fills are dispatched fire-and-forget via `Task.Run → _router.ApplyFillByExchangeOrderIdAsync(...)` with `mode: "live"`.

**Critical:** `KrakenWsExecutionAdapter` only activates when `IOperationModeService.CurrentMode == "live"`. It disconnects and idles when mode is `"paper"`. It reconnects with exponential backoff (2 s → doubles → max 60 s).

### Fill → Position Flow

```
Kraken WS executions
  → KrakenWsExecutionAdapter.HandleMessage()
    → OrderRouter.ApplyFillByExchangeOrderIdAsync(exchangeOrderId, qty, price, isFullFill)
      → OrderRouter.ApplyFillAsync()
        → PositionRegistry.OpenPosition() / ApplyPartialFill()
          → OnPositionChanged / OnPositionClosed events
            → ExecutionHubBroadcaster → SignalR → MonitoringDashboard → Browser
```

**Spot vs. margin:** Crypton trades spot. `position_status` in Kraken execution events (`opened`/`closing`/`closed`) is **margin-only** — never use it. Crypton's position state lives entirely in `PositionRegistry`.

### Order ID Correlation (`_exchangeToInternalId`)

`OrderRouter` maintains a reverse-lookup dictionary: `exchangeOrderId (Kraken txid) → internalId`. This is populated in `PlaceEntryOrderAsync` after `PlaceOrderAsync` returns the acknowledged txid.

When a WS fill arrives for an `order_id` not in this map, `ApplyFillByExchangeOrderIdAsync` logs a **warning and returns silently** — it does NOT throw. This happens legitimately for orders placed in a previous process lifetime (the map is in-memory, not persisted). If this becomes a problem, reconcile via `GetTradeHistoryAsync` on startup.

### Idempotency (`ApplyFillAsync`)

`ApplyFillAsync` checks `if (record.Status == OrderStatus.Filled) return` at the top — subsequent calls for an already-filled order are no-ops. This protects against duplicate processing when `snap_orders: true` delivers a snapshot of already-filled orders on reconnect.

### Paper Mode vs Live Fill Reconciliation

In **paper mode**, `PaperTradingAdapter.PlaceOrderAsync` fills the order synchronously (returns a `Filled` status immediately). `OrderRouter.TryApplyImmediateFillAsync` is called right after `PlaceOrderAsync` — it polls `GetOrderStatusAsync` once and applies the fill if the order is already filled. This is the sole fill path for paper mode since `KrakenWsExecutionAdapter` is dormant.

In **live mode**, `TryApplyImmediateFillAsync` still fires immediately (catches fast-filling market orders before the WS event arrives), but `KrakenWsExecutionAdapter` is the primary fill path. The idempotency check ensures `TryApplyImmediateFillAsync` and the WS fill don't double-count.

`TryApplyImmediateFillAsync` failures are **swallowed silently** — the WS channel or a future reconciliation pass are expected to cover any miss.

### `DelegatingExchangeAdapter` — Paper/Live Switching

`DelegatingExchangeAdapter` holds references to both `PaperTradingAdapter` and the live Kraken pair (`KrakenWebSocketAdapter` + `KrakenRestAdapter`). The live adapters are injected at startup by `LiveAdapterWiring` (an `IHostedService`) via `SetLiveAdapters(wsAdapter, restAdapter)`. 

Routing:
- Always: `SubscribeToMarketDataAsync` → `KrakenWebSocketAdapter` (live) or `PaperTradingAdapter` (paper)
- Paper mode: all order/account calls → `PaperTradingAdapter`
- Live mode: all order/account calls → `KrakenRestAdapter`

Mode is read from `IOperationModeService.CurrentMode` on every call — no restart required to switch.

### SignalR Propagation Chain (ExecutionService → Browser)

Understanding the full event flow requires reading across three services:

```
PositionRegistry.OnPositionChanged / OnPositionClosed
  → ExecutionHubBroadcaster (ExecutionService)
    → ExecutionHub.PositionsGroup SignalR broadcast ("PositionUpdated" / "PositionClosed")
      → ExecutionServiceClient (MonitoringDashboard) — connected as a SignalR CLIENT to ExecutionService
        → fires C# event: OnPositionChanged / OnPositionClosed
          → Program.cs handler in MonitoringDashboard
            → IDashboardHubContext.Clients.All.PositionUpdated / PositionClosed
              → Browser signalr.ts handler
                → App.tsx state update
```

MonitoringDashboard is a **BFF (Backend for Frontend)**: it acts as a SignalR client to upstream services (ExecutionService, AgentRunner) and re-broadcasts to the browser over the Dashboard hub. No browser connection ever touches ExecutionService or AgentRunner directly.

Strategy updates follow a different detection path: `ExecutionServiceClient` receives a `StatusUpdate` (broadcast every tick), and `Program.cs` compares the incoming `strategy_id` field against `_lastStrategyId`. When it changes, the MonitoringDashboard fetches the full strategy via REST and broadcasts it over `StrategyUpdated`.

---

## MarketData Service (`KrakenExchangeAdapter`)

Separate from ExecutionService adapters. Subscribes to **three channels** on `wss://ws.kraken.com/v2`:

- `ticker` — bid/ask prices, fires `OnPriceUpdate`
- `book` (depth 25) — order book updates
- `trade` (snapshot: false) — public trade tape

REST public endpoints: `/0/public/Ticker`, `/0/public/Depth`, `/0/public/OHLC`

**Symbol mapping** (Kraken REST uses legacy pair names):
```
BTC/USD → XXBTZUSD
ETH/USD → XETHZUSD
SOL/USD → SOLUSD
```

**Rate limiting:** client-side token bucket — `MaxRequestsPerSecond: 5`, `MaxRequestsPerMinute: 15`. No server-side error string detection; purely preventive.

**Circuit breaker:** wraps all REST calls (`CircuitBreaker` / `CircuitBreakerOptions`). Opens after repeated failures; throws `CircuitBreakerOpenException`.

**Balance polling:** `POST /0/private/Balance` every 30 seconds, but only if `ApiKey`/`ApiSecret` are configured.

---

## Configuration

**ExecutionService** (`KrakenAdapterConfig` in `ExecutionServiceConfig`):
- `RestBaseUrl` — Kraken REST base URL
- `WsBaseUrl` — public WS URL
- `ApiKey` / `ApiSecret` — from env (`EXECUTIONSERVICE__KRAKEN__APIKEY`, `EXECUTIONSERVICE__KRAKEN__APISECRET`)
- `MaxReconnectAttempts` / `ReconnectDelaySeconds` — for `KrakenWebSocketAdapter`

**MarketDataService** (`KrakenConfig` in `MarketDataConfig`):
- `WsBaseUrl`, `ApiKey`, `ApiSecret`, `SymbolMapping` (dict)

---

## Testing Conventions

`HandleMessage` and `ProcessMessageAsync` are `internal` methods — test them directly without a live WS.

Use `MockHttpMessageHandler` + `KrakenResponseBuilder` for REST tests. Always cover:
1. Success path
2. Kraken error string → correct typed exception
3. Rate limit → `IsRateLimited` state set
4. HTTP 401 → `AuthenticationException`

For execution adapter tests: construct a real `OrderRouter` + mock `IExchangeAdapter`; call `HandleMessage` with captured JSON to verify position state in `PositionRegistry`.

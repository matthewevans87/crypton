# Kraken API Reference for Execution Service

This document covers every Kraken API surface needed by `Crypton.Api.ExecutionService`. It is
written to be used as a direct implementation reference, not as a verbatim copy of the official
docs. All field names, types, and constraints are drawn from the Kraken v0 REST API and v2
WebSocket API as of early 2026.

---

## 1 — Architecture: REST vs WebSocket

| Concern                                      | Transport        | URL                           |
| -------------------------------------------- | ---------------- | ----------------------------- |
| Place / amend / cancel orders                | REST (private)   | `https://api.kraken.com/0/…`  |
| Query order status (polling fallback)        | REST (private)   | `https://api.kraken.com/0/…`  |
| Query account balances (startup / reconcile) | REST (private)   | `https://api.kraken.com/0/…`  |
| Query open margin positions (reconcile)      | REST (private)   | `https://api.kraken.com/0/…`  |
| Query trade history (reconcile)              | REST (private)   | `https://api.kraken.com/0/…`  |
| Get tradable pairs / min order sizes         | REST (public)    | `https://api.kraken.com/0/…`  |
| Obtain WS auth token                         | REST (private)   | `https://api.kraken.com/0/…`  |
| Live price ticks (bid/ask/last)              | WebSocket (pub)  | `wss://ws.kraken.com/v2`      |
| Order book (L2, 10/25/100 levels)            | WebSocket (pub)  | `wss://ws.kraken.com/v2`      |
| Public trade tape                            | WebSocket (pub)  | `wss://ws.kraken.com/v2`      |
| Live order fill / status events              | WebSocket (auth) | `wss://ws-auth.kraken.com/v2` |
| Live balance updates                         | WebSocket (auth) | `wss://ws-auth.kraken.com/v2` |

**Rule of thumb:**  
- Use `wss://ws.kraken.com/v2` (no auth) for all market data subscriptions.  
- Use `wss://ws-auth.kraken.com/v2` with a WS token for execution and account events.  
- Use REST for every mutating operation and for queries that do not need streaming.

---

## 2 — REST Authentication

All private REST endpoints require two custom HTTP headers. No public endpoint requires auth.

### 2.1 Headers

| Header     | Value                                             |
| ---------- | ------------------------------------------------- |
| `API-Key`  | The raw API key string from Kraken.               |
| `API-Sign` | Base64-encoded HMAC-SHA512 signature (see below). |

### 2.2 Signature Algorithm

```
nonce       = current Unix timestamp in milliseconds (string, monotonically increasing)
postData    = URL-encoded request body (all parameters including nonce, key=value&key=value)
path        = the endpoint path, e.g. "/0/private/AddOrder"

sha256      = SHA256( UTF8(nonce + postData) )         # raw bytes
message     = UTF8(path) + sha256                      # concatenate raw bytes
secret      = Base64Decode( api_secret )               # raw bytes
API-Sign    = Base64Encode( HMAC-SHA512(secret, message) )
```

**Current implementation:** `KrakenRestAdapter.ComputeSignature()` — correct.

### 2.3 Content-Type

Private endpoints: `Content-Type: application/x-www-form-urlencoded` (form body, not JSON).  
The `nonce` field must always be included in the form body.

### 2.4 Error Handling

Every response, including HTTP 200, may carry errors in the top-level `"error"` array:

```json
{ "error": ["EOrder:Rate limit exceeded"], "result": null }
```

Notable error codes:

| Code                         | Meaning                  | Action                          |
| ---------------------------- | ------------------------ | ------------------------------- |
| `EAPI:Invalid key`           | Bad or revoked API key   | Throw `AuthenticationException` |
| `EAPI:Invalid nonce`         | Nonce reused or too old  | Retry with new nonce            |
| `EOrder:Rate limit exceeded` | Trading rate limit hit   | Backoff 60 s                    |
| `EAPI:Rate limit exceeded`   | API-level rate limit hit | Backoff 60 s                    |
| `EOrder:Insufficient funds`  | Not enough balance       | Log + surface to strategy layer |
| `EOrder:Unknown order`       | txid not found           | Treat as `OrderNotFound`        |
| `EOrder:Invalid order`       | Bad order parameters     | Log as permanent failure        |

---

## 3 — REST Endpoints

### 3.1 Public Market Data

#### `GET /0/public/AssetPairs`

**Purpose:** Enumerate valid pair symbols, minimum order sizes, tick sizes, leverage options.  
**Used by:** Execution Service startup / pair validation; AgentRunner `technical_indicators` tool.

| Query param | Type   | Required | Description                                                            |
| ----------- | ------ | -------- | ---------------------------------------------------------------------- |
| `pair`      | string | No       | Comma-separated pairs, e.g. `BTC/USD,ETH/USD`. Returns all if omitted. |
| `info`      | enum   | No       | `info` (default), `leverage`, `fees`, `margin`                         |

**Response fields (per pair):**

| Field           | Type   | Description                                                        |
| --------------- | ------ | ------------------------------------------------------------------ |
| `altname`       | string | Alternate name, e.g. `XBTUSD`                                      |
| `wsname`        | string | WebSocket pair name, e.g. `XBT/USD`. Use this in WS subscriptions. |
| `base`          | string | Base asset ID (e.g. `XXBT`)                                        |
| `quote`         | string | Quote asset ID (e.g. `ZUSD`)                                       |
| `pair_decimals` | int    | Max decimal places for price                                       |
| `lot_decimals`  | int    | Max decimal places for volume (base)                               |
| `ordermin`      | string | Minimum order quantity (base currency)                             |
| `costmin`       | string | Minimum order cost (quote currency)                                |
| `tick_size`     | string | Minimum price increment                                            |
| `status`        | string | `online`, `cancel_only`, `post_only`, `limit_only`, `reduce_only`  |
| `leverage_buy`  | int[]  | Available leverage multipliers for buys                            |
| `leverage_sell` | int[]  | Available leverage multipliers for sells                           |

> **Note:** Kraken's legacy pair identifiers (e.g. `XXBTZUSD`) differ from the display names
> (`XBT/USD`) and WebSocket names (`XBT/USD`). Always use `wsname` for WS subscriptions and
> the altname or pair display name for REST order placement — confirm via this endpoint at
> startup. `BTC/USD` resolves to `XBT/USD` (wsname) and `XBTUSD` (altname).

---

### 3.2 Account Data (Private)

#### `POST /0/private/Balance`

**Purpose:** Retrieve all spot asset balances. Used at startup for reconciliation and to
seed `AccountBalance` before the first strategy cycle.

**Request body:**

| Field   | Type  | Required | Description      |
| ------- | ----- | -------- | ---------------- |
| `nonce` | int64 | ✅        | Monotonic nonce. |

**Response:**

```json
{ "error": [], "result": { "ZUSD": "1234.56", "XXBT": "0.5", "XETH": "3.2" } }
```

Key = Kraken asset code (not display name). Notable mappings:

| Kraken code | Asset |
| ----------- | ----- |
| `ZUSD`      | USD   |
| `XXBT`      | BTC   |
| `XETH`      | ETH   |
| `SOL`       | SOL   |

> **Gap (2c.5):** `GetAccountBalanceAsync` currently maps only `ZUSD`/`USD` to `AvailableUsd`.
> Other assets only appear in `AssetBalances`. This is correct.

---

#### `POST /0/private/TradeBalance`

**Purpose:** Retrieve margin/collateral summary. Useful for live-mode margin tracking (portfolio
risk, `ExecutionEngine` telemetry). Not required for spot paper trading.

**Request body:** `nonce` (required), `asset` (default `ZUSD`).

**Key response fields:**

| Field | Description                                         |
| ----- | --------------------------------------------------- |
| `eb`  | Equivalent balance of all currencies in quote asset |
| `tb`  | Trade balance (equity currencies only)              |
| `m`   | Margin consumed by open positions                   |
| `n`   | Unrealised P&L                                      |
| `e`   | Equity = `tb + n`                                   |
| `mf`  | Free margin = `e - initial margin`                  |
| `ml`  | Margin level = `(e / m) * 100`                      |

---

#### `POST /0/private/OpenPositions`

**Purpose:** Get open margin positions. Used by `ReconciliationService` on startup to re-sync
`PositionRegistry` with live exchange state after a restart.

**Request body:** `nonce`. Optional: `docalcs=true` for P&L.

**Response:** Map of `txid → position`:

| Field        | Type   | Description                               |
| ------------ | ------ | ----------------------------------------- |
| `ordertxid`  | string | Order ID that opened this position        |
| `pair`       | string | Asset pair (Kraken code, e.g. `XXBTZUSD`) |
| `type`       | string | `buy` or `sell`                           |
| `ordertype`  | string | Order type used to open                   |
| `cost`       | string | Opening cost in quote currency            |
| `fee`        | string | Opening fee in quote currency             |
| `vol`        | string | Size opened (base currency)               |
| `vol_closed` | string | Quantity already closed                   |
| `margin`     | string | Initial margin consumed                   |

> **Gap (2c.6):** The current `GetOpenPositionsAsync` computes `AverageEntryPrice = cost / vol`.
> For short positions, this is correct but `PositionRegistry.OpenPosition` only handles buys.

---

#### `POST /0/private/OpenOrders`

**Purpose:** Poll for open order status. Used as fallback when WS `executions` channel is not
available (paper mode, reconnect gap).

**Request body:** `nonce`. Optional: `trades=true` to include associated trade IDs, `cl_ord_id`
to filter.

**Key response fields (per order under `result.open`):**

| Field             | Description                                        |
| ----------------- | -------------------------------------------------- |
| `status`          | `pending`, `open`, `closed`, `canceled`, `expired` |
| `descr.pair`      | Asset pair                                         |
| `descr.type`      | `buy` or `sell`                                    |
| `descr.ordertype` | Order type                                         |
| `vol`             | Total order volume                                 |
| `vol_exec`        | Executed volume so far                             |
| `price`           | Average fill price                                 |
| `cl_ord_id`       | Client order ID if set                             |

---

#### `POST /0/private/TradesHistory`

**Purpose:** Retrieve historical fills. Used by `ReconciliationService` and for populating the
trade history endpoint (`GET /trades` in the API).

**Request body:**

| Field   | Type  | Required | Description                                |
| ------- | ----- | -------- | ------------------------------------------ |
| `nonce` | int64 | ✅        |                                            |
| `start` | int64 | No       | Unix timestamp (seconds) to filter from    |
| `end`   | int64 | No       | Unix timestamp (seconds) to filter to      |
| `ofs`   | int   | No       | Offset for pagination (50 trades per page) |

**Response fields per trade (under `result.trades`):**

| Field       | Description                                  |
| ----------- | -------------------------------------------- |
| `pair`      | Kraken pair code (e.g. `XXBTZUSD`)           |
| `type`      | `buy` or `sell`                              |
| `ordertype` | Order type                                   |
| `vol`       | Volume filled (base currency, string)        |
| `price`     | Fill price (quote currency, string)          |
| `fee`       | Fee paid (quote currency, string)            |
| `cost`      | Total cost of fill (quote currency, string)  |
| `time`      | Unix timestamp (float, seconds + fractional) |
| `ordertxid` | The order that generated this fill           |

> **Gap:** `GetTradeHistoryAsync` parses `time` as milliseconds (`tsUnix * 1000`) but Kraken
> returns it in **seconds**. This causes all `Trade.Timestamp` values to be ~46 years in the future.
> Fix: `DateTimeOffset.FromUnixTimeSeconds((long)tsUnix)`.

---

### 3.3 Order Management (Private)

#### `POST /0/private/AddOrder`

**Purpose:** Place a new order. Core of the live execution path.

**Required headers:** `API-Key`, `API-Sign`  
**Content-Type:** `application/x-www-form-urlencoded`

**Key request fields:**

| Field              | Type   | Required | Description                                                                                                                                                                                 |
| ------------------ | ------ | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `nonce`            | int64  | ✅        | Monotonic nonce                                                                                                                                                                             |
| `ordertype`        | string | ✅        | `market`, `limit`, `stop-loss`, `stop-loss-limit`, `take-profit`, `take-profit-limit`, `trailing-stop`, `trailing-stop-limit`, `iceberg`, `settle-position`                                 |
| `type`             | string | ✅        | `buy` or `sell`                                                                                                                                                                             |
| `volume`           | string | ✅        | Quantity in base currency. Use `"0"` to close a margin position.                                                                                                                            |
| `pair`             | string | ✅        | Pair identifier or altname (e.g. `XBTUSD` or `BTC/USD`)                                                                                                                                     |
| `price`            | string | Cond.    | Limit price (for `limit`, `iceberg`); trigger price for `stop-loss`, `take-profit`, `trailing-stop` variants. Supports `+/-/#` prefix for relative pricing, `%` suffix for percent offsets. |
| `price2`           | string | Cond.    | Limit price for `stop-loss-limit`, `take-profit-limit`, `trailing-stop-limit`.                                                                                                              |
| `trigger`          | string | No       | `last` (default) or `index` — reference price for triggered orders                                                                                                                          |
| `leverage`         | string | No       | E.g. `"5"` for 5× leverage on margin orders                                                                                                                                                 |
| `reduce_only`      | bool   | No       | If `true`, only reduces an existing margin position                                                                                                                                         |
| `oflags`           | string | No       | Comma-delimited: `post` (post-only), `fcib` (fee in base), `fciq` (fee in quote), `viqc` (volume in quote, buy market only)                                                                 |
| `timeinforce`      | string | No       | `GTC` (default), `IOC`, `GTD`                                                                                                                                                               |
| `starttm`          | string | No       | Scheduled start: `0` = now, `<n>` = Unix ts, `+<n>` = seconds from now                                                                                                                      |
| `expiretm`         | string | No       | Expiry (GTD only): `0` = none, `<n>` = Unix ts, `+<n>` = seconds from now (≥5 s)                                                                                                            |
| `cl_ord_id`        | string | No       | Client order ID: UUID or free text up to 18 ASCII chars. **Recommended: use a UUID derived from the internal order ID** to enable idempotent status lookups.                                |
| `userref`          | int32  | No       | Non-unique numeric tag; mutually exclusive with `cl_ord_id`                                                                                                                                 |
| `validate`         | bool   | No       | If `true`, validate only — do not submit to matching engine                                                                                                                                 |
| `deadline`         | string | No       | RFC3339 timestamp; engine rejects order after this time (useful for latency protection)                                                                                                     |
| `close[ordertype]` | string | No       | Conditional close order type                                                                                                                                                                |
| `close[price]`     | string | No       | Conditional close price                                                                                                                                                                     |
| `close[price2]`    | string | No       | Conditional close price2                                                                                                                                                                    |

**Response:**

```json
{
  "error": [],
  "result": {
    "descr": { "order": "buy 1.25 XBTUSD @ limit 27500.0" },
    "txids": ["OHYO67-6LP66-HMQ437"]
  }
}
```

| Field         | Description                                                                 |
| ------------- | --------------------------------------------------------------------------- |
| `txids`       | Array of Kraken order IDs. Normally one element. Save as `ExchangeOrderId`. |
| `descr.order` | Human-readable order description                                            |

> **Gap (ExchangeModels):** `OrderType` enum is missing `TakeProfit`, `TakeProfitLimit`, `TrailingStop`,
> `TrailingStopLimit`. Any unrecognized type silently maps to `"market"` in `PlaceOrderAsync`.
> Strategy DSL may produce stop-loss-limit and take-profit orders; the enum and mapping must be
> extended to cover all types needed by `CompiledStrategy`.

---

#### `POST /0/private/CancelOrder`

**Purpose:** Cancel an open order by Kraken txid or client order ID.

**Request body:**

| Field       | Type   | Required | Description                                       |
| ----------- | ------ | -------- | ------------------------------------------------- |
| `nonce`     | int64  | ✅        |                                                   |
| `txid`      | string | Cond.    | Kraken txid. Mutually exclusive with `cl_ord_id`. |
| `cl_ord_id` | string | Cond.    | Client order ID.                                  |

**Response:**

```json
{ "error": [], "result": { "count": 1, "pending": false } }
```

---

#### `POST /0/private/CancelAllOrders`

**Purpose:** Cancel every open order on the account at once. Used by `SafeModeController` during
emergency market-sell activation.

**Request body:** `nonce` only.

**Response:** `{ "result": { "count": <n> } }`

---

#### `POST /0/private/QueryOrders`

**Purpose:** Query status of one or more specific orders by txid. Polling fallback when no WS
`executions` stream is active.

**Request body:**

| Field    | Type   | Required | Description                    |
| -------- | ------ | -------- | ------------------------------ |
| `nonce`  | int64  | ✅        |                                |
| `txid`   | string | ✅        | Comma-delimited txids (max 50) |
| `trades` | bool   | No       | Include associated trade IDs   |

**Response:** Map of `txid → order` with same fields as `OpenOrders` plus:

| Field      | Description                                        |
| ---------- | -------------------------------------------------- |
| `status`   | `open`, `closed`, `canceled`, `pending`, `expired` |
| `vol_exec` | Filled volume (string)                             |
| `price`    | Average fill price (string, `"0"` if unfilled)     |

---

### 3.4 WebSocket Token

#### `POST /0/private/GetWebSocketsToken`

**Purpose:** Obtain a short-lived token for authenticated WebSocket channels (`executions`,
`balances`, `add_order`, etc.). Required before connecting to `wss://ws-auth.kraken.com/v2`.

**Request body:** `nonce` only.

**Response:**

```json
{ "error": [], "result": { "token": "abc123...", "expires": 900 } }
```

| Field     | Description                                                                                              |
| --------- | -------------------------------------------------------------------------------------------------------- |
| `token`   | Token string. Valid for 15 minutes from creation but does not expire if a WS subscription is maintained. |
| `expires` | Seconds until expiry (typically 900)                                                                     |

**Implementation note:** Call this endpoint via `KrakenRestAdapter` before opening the auth WS
connection. Re-fetch the token before it expires (or on WS disconnect) using a background timer.

---

## 4 — WebSocket API v2

### 4.1 Connection

| Channel type       | URL                           | Auth required |
| ------------------ | ----------------------------- | ------------- |
| Public market data | `wss://ws.kraken.com/v2`      | No            |
| Private execution  | `wss://ws-auth.kraken.com/v2` | Yes (token)   |

Both connections should be maintained with auto-reconnect logic. On reconnect, re-subscribe to all
channels. For the auth connection, obtain a fresh token before reconnecting if the previous one
has expired.

### 4.2 Message Envelope

All messages are JSON text frames.

**Subscribe request:**
```json
{ "method": "subscribe", "params": { "channel": "<name>", ... }, "req_id": 1 }
```

**Subscribe ack:**
```json
{ "method": "subscribe", "result": { ... }, "success": true, "req_id": 1 }
```

**Channel update:**
```json
{ "channel": "<name>", "type": "snapshot|update", "data": [ ... ] }
```

Non-channel messages (heartbeat, status):
```json
{ "channel": "heartbeat" }
{ "channel": "status", "data": [ { "system": "online", "version": "2.0.4" } ] }
```

---

### 4.3 Public Channels (`wss://ws.kraken.com/v2`)

#### `ticker` — Level 1 Market Data

**Purpose:** Real-time best-bid/ask and last-trade data per symbol. The primary market data
feed for `PaperTradingAdapter` fill simulation and `ExecutionEngine` tick loop.

**Subscribe:**
```json
{
  "method": "subscribe",
  "params": {
    "channel": "ticker",
    "symbol": ["BTC/USD", "ETH/USD"],
    "event_trigger": "trades",
    "snapshot": true
  }
}
```

| Param           | Type     | Default  | Description                                          |
| --------------- | -------- | -------- | ---------------------------------------------------- |
| `symbol`        | string[] | —        | List of WS pair names (use `wsname` from AssetPairs) |
| `event_trigger` | string   | `trades` | `bbo` (on book change) or `trades` (on every fill)   |
| `snapshot`      | bool     | `true`   | Send current state on subscribe                      |

**Update message fields:**
```json
{
  "channel": "ticker",
  "type": "update",
  "data": [{
    "symbol": "BTC/USD",
    "bid": 45000.0,
    "bid_qty": 0.5,
    "ask": 45001.0,
    "ask_qty": 0.3,
    "last": 45000.5,
    "volume": 1234.56,
    "high": 46000.0,
    "low": 44000.0,
    "change": -500.0,
    "change_pct": -1.1,
    "timestamp": "2026-03-01T12:00:00.123456Z"
  }]
}
```

| Field       | Type   | Description                   |
| ----------- | ------ | ----------------------------- |
| `symbol`    | string | WS pair name (e.g. `BTC/USD`) |
| `bid`       | float  | Best bid price                |
| `ask`       | float  | Best ask price                |
| `last`      | float  | Last traded price             |
| `timestamp` | string | RFC3339 with microseconds     |
| `volume`    | float  | 24-hour traded volume         |
| `high`      | float  | 24-hour high                  |
| `low`       | float  | 24-hour low                   |

> **Current implementation:** `KrakenWebSocketAdapter.ParseTickerItem` maps `bid`, `ask`, and
> `timestamp` to `MarketSnapshot`. `last` is not captured but would be useful for stop/trigger
> price evaluation.

---

#### `book` — Level 2 Order Book

**Purpose:** Full order book for advanced entry/exit condition evaluation (e.g. `book_depth` DSL
functions, slippage estimation for large orders). Not required for MVP but documented here for
future use.

**Subscribe:**
```json
{
  "method": "subscribe",
  "params": {
    "channel": "book",
    "symbol": ["BTC/USD"],
    "depth": 10,
    "snapshot": true
  }
}
```

| Param    | Type     | Default | Description                                         |
| -------- | -------- | ------- | --------------------------------------------------- |
| `symbol` | string[] | —       |                                                     |
| `depth`  | int      | `10`    | Levels to receive: `10`, `25`, `100`, `500`, `1000` |

**Data fields (per item in `data`):**

| Field       | Type             | Description                                                   |
| ----------- | ---------------- | ------------------------------------------------------------- |
| `symbol`    | string           |                                                               |
| `bids`      | `[{price, qty}]` | Bidside; qty=0 means remove level                             |
| `asks`      | `[{price, qty}]` | Askside; qty=0 means remove level                             |
| `checksum`  | int              | CRC32 of top 10 bids+asks; validate to detect missed messages |
| `timestamp` | string           | RFC3339                                                       |

**Note:** snapshot `type=snapshot` provides the initial book; subsequent `type=update` messages
are incremental. Maintain a local book and apply updates in sequence. Verify `checksum` after
each update.

---

#### `trade` — Public Trade Tape

**Purpose:** Every executed trade on Kraken matching engine, publicly visible. Useful for
last-trade price tracking and volume analysis. Not a substitute for the `executions` channel.

**Subscribe:**
```json
{ "method": "subscribe", "params": { "channel": "trade", "symbol": ["BTC/USD"], "snapshot": false } }
```

**Data fields:**

| Field       | Type   | Description             |
| ----------- | ------ | ----------------------- |
| `symbol`    | string |                         |
| `price`     | float  | Trade price             |
| `qty`       | float  | Trade quantity          |
| `side`      | string | `buy` or `sell`         |
| `ord_type`  | string | `limit` or `market`     |
| `timestamp` | string | RFC3339                 |
| `trade_id`  | int    | Unique trade identifier |

---

### 4.4 Authenticated Channels (`wss://ws-auth.kraken.com/v2`)

All auth channels require a `token` field in the subscribe request. The token is obtained from
`POST /0/private/GetWebSocketsToken`.

---

#### `executions` — Order Status and Fill Events

**Purpose:** Real-time stream of all order lifecycle events and fill notifications for the
authenticated account. This is the primary mechanism for live-mode fill tracking; it replaces
polling `QueryOrders`.  

**Subscribe:**
```json
{
  "method": "subscribe",
  "params": {
    "channel": "executions",
    "token": "<ws_token>",
    "snap_orders": true,
    "snap_trades": false,
    "order_status": true
  }
}
```

| Param          | Type   | Default | Description                                         |
| -------------- | ------ | ------- | --------------------------------------------------- |
| `snap_orders`  | bool   | `true`  | Include current open orders in initial snapshot     |
| `snap_trades`  | bool   | `false` | Include last 50 fills in initial snapshot           |
| `order_status` | bool   | `true`  | Stream all status transitions (not just open/close) |
| `token`        | string | —       | WS auth token                                       |

**Key `exec_type` values:**

| `exec_type`   | Meaning                                                     |
| ------------- | ----------------------------------------------------------- |
| `pending_new` | Order received by engine but not yet live                   |
| `new`         | Order is live in the book                                   |
| `trade`       | Partial fill event                                          |
| `filled`      | Order fully filled                                          |
| `canceled`    | Order cancelled                                             |
| `expired`     | GTD/IOC order expired                                       |
| `amended`     | User-initiated parameter change                             |
| `restated`    | Engine-initiated change (e.g. partial execution of iceberg) |

**Key data fields (most relevant to Execution Service):**

| Field          | Type   | Condition      | Description                                                               |
| -------------- | ------ | -------------- | ------------------------------------------------------------------------- |
| `order_id`     | string | always         | Kraken order ID (same as `txid` from REST)                                |
| `cl_ord_id`    | string | if set         | Client order ID                                                           |
| `exec_type`    | string | always         | See table above                                                           |
| `order_status` | string | always         | `pending_new`, `new`, `partially_filled`, `filled`, `canceled`, `expired` |
| `symbol`       | string | always         | WS pair name                                                              |
| `side`         | string | always         | `buy` or `sell`                                                           |
| `order_type`   | string | always         | Order type                                                                |
| `order_qty`    | float  | always         | Original order quantity                                                   |
| `cum_qty`      | float  | always         | Cumulative filled quantity                                                |
| `cum_cost`     | float  | always         | Cumulative filled value                                                   |
| `avg_price`    | float  | always         | Average fill price so far                                                 |
| `last_qty`     | float  | `trade` events | Quantity in this individual fill                                          |
| `last_price`   | float  | `trade` events | Price of this individual fill                                             |
| `exec_id`      | string | `trade` events | Individual execution identifier                                           |
| `fees`         | array  | `trade` events | `[{asset, qty}]` — fee paid on this fill                                  |
| `timestamp`    | string | always         | RFC3339 event time                                                        |
| `reason`       | string | if applicable  | Cancellation or restatement reason                                        |

**Implementation notes:**

1. Match incoming events to internal orders by `cl_ord_id` (preferred) or `order_id`.
2. On `exec_type=trade`: deliver a `OrderFillEvent` to the `OrderRouter`/`PositionRegistry`.
3. On `exec_type=filled`: treat as the final fill; update `PositionRegistry` and `MetricsCollector`.
4. On `exec_type=canceled` or `expired`: update the order record; apply `FailureTracker` if
   unexpected.
5. The `executions` channel snapshot on connect includes all currently open orders — use this to
   reconcile the in-memory `OrderRouter` order state after a reconnect.

---

#### `balances` — Account Balance Updates

**Purpose:** Real-time stream of all balance changes (deposits, withdrawals, trade fills, fees).
Enables `ExecutionEngine` to maintain an accurate in-memory balance without polling REST.

**Subscribe:**
```json
{
  "method": "subscribe",
  "params": {
    "channel": "balances",
    "token": "<ws_token>",
    "snapshot": true
  }
}
```

**Snapshot data (per asset):**

| Field     | Type   | Description                                    |
| --------- | ------ | ---------------------------------------------- |
| `asset`   | string | Asset symbol code (e.g. `ZUSD`, `XXBT`)        |
| `balance` | float  | Total held across all wallets                  |
| `wallets` | array  | `[{type, id, balance}]` — per-wallet breakdown |

**Update data (per ledger transaction):**

| Field       | Type   | Description                                                                |
| ----------- | ------ | -------------------------------------------------------------------------- |
| `asset`     | string | Asset affected                                                             |
| `amount`    | float  | Change (positive = credit, negative = debit)                               |
| `balance`   | float  | New total balance after this transaction                                   |
| `fee`       | float  | Fee on this transaction                                                    |
| `ledger_id` | string | Kraken ledger reference ID                                                 |
| `ref_id`    | string | Related entity ID (e.g. `trade_id` for trades)                             |
| `type`      | string | `deposit`, `withdrawal`, `trade`, `margin`, `adjustment`, `rollover`, etc. |
| `timestamp` | string | RFC3339                                                                    |

---

### 4.5 Authenticated Order Entry via WebSocket

The WebSocket API also supports placing/cancelling orders on `wss://ws-auth.kraken.com/v2`.
**Crypton does not use this path** — all order entry uses the REST API. This avoids the complexity
of a second stateful connection for order operations and simplifies error handling. However, for
low-latency requirements in future, WS order entry could be added.

**For reference:**

- `add_order` — same parameters as REST `AddOrder` plus `token`
- `cancel_order` — `{ method: "cancel_order", params: { order_id: ["<txid>"], token } }`
- `amend_order` — modify price/qty of an open order

---

## 5 — Feature-to-API Mapping

This section maps each Execution Service feature to the specific API endpoints it requires.

### 5.1 Paper Trading (current primary path)

| Feature                                 | API required                                                        | Status                   |
| --------------------------------------- | ------------------------------------------------------------------- | ------------------------ |
| Fill simulation at mid ± slippage       | `ticker` WS channel via `IMarketDataSource` → `PaperTradingAdapter` | ❌ `NullMarketDataSource` |
| Snapshot of all balances at startup     | N/A (simulated balances from config)                                | ✅                        |
| Record paper fill in `PositionRegistry` | Internal only                                                       | ⚠️ Short sells not wired  |

### 5.2 Live Trading

| Feature                                   | API required                                         | Status                                       |
| ----------------------------------------- | ---------------------------------------------------- | -------------------------------------------- |
| Obtain WS auth token                      | `POST /0/private/GetWebSocketsToken`                 | ❌ Not called                                 |
| Subscribe to order fill events            | `executions` channel (`wss://ws-auth.kraken.com/v2`) | ❌ Not subscribed                             |
| Subscribe to balance updates              | `balances` channel (`wss://ws-auth.kraken.com/v2`)   | ❌ Not subscribed                             |
| Subscribe to market data ticks            | `ticker` channel (`wss://ws.kraken.com/v2`)          | ✅ `KrakenWebSocketAdapter`                   |
| Place an order                            | `POST /0/private/AddOrder`                           | ✅ `KrakenRestAdapter`                        |
| Cancel an order                           | `POST /0/private/CancelOrder`                        | ✅ `KrakenRestAdapter`                        |
| Cancel all orders (safe mode)             | `POST /0/private/CancelAllOrders`                    | ❌ REST adapter uses `CancelOrder` one-by-one |
| Query order status (poll)                 | `POST /0/private/QueryOrders`                        | ✅ `KrakenRestAdapter.GetOrderStatusAsync`    |
| Account balance (startup)                 | `POST /0/private/Balance`                            | ✅ `KrakenRestAdapter.GetAccountBalanceAsync` |
| Open margin positions (startup/reconcile) | `POST /0/private/OpenPositions`                      | ✅ `KrakenRestAdapter.GetOpenPositionsAsync`  |
| Trade history (reconcile)                 | `POST /0/private/TradesHistory`                      | ✅ (bug: timestamp parsing in seconds vs ms)  |
| Validate pair metadata                    | `GET /0/public/AssetPairs`                           | ❌ Not called                                 |

### 5.3 Startup / DI Wiring

| Feature                                                                        | Status                                                                                                         |
| ------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------- |
| `KrakenRestAdapter` instantiated in DI                                         | ❌ Not registered; created manually in `DelegatingExchangeAdapter.SetLiveAdapters` which itself is never called |
| `KrakenWebSocketAdapter` instantiated in DI                                    | ❌ Same as above                                                                                                |
| `DelegatingExchangeAdapter.SetLiveAdapters` called at startup                  | ❌ Nobody calls this                                                                                            |
| `IMarketDataSource` wired to real impl.                                        | ❌ `NullMarketDataSource` registered                                                                            |
| `EXECUTIONSERVICE__MARKETDATASERVICEURL` in `execution-service` docker-compose | ❌ Not set                                                                                                      |

---

## 6 — Known Bugs in Current Implementation

| Location                                    | Issue                                                                                                                                    |
| ------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------- |
| `KrakenRestAdapter.GetTradeHistoryAsync`    | `time` field is seconds (not ms); `FromUnixTimeMilliseconds((long)(tsUnix * 1000))` overflows. Fix: `FromUnixTimeSeconds((long)tsUnix)`. |
| `ExchangeModels.OrderType`                  | Missing `TakeProfit`, `TakeProfitLimit`, `TrailingStop`, `TrailingStopLimit`. Unmapped types silently fall through to `"market"`.        |
| `PositionRegistry.OpenPosition`             | Only called for `Buy` fills in `OrderRouter.ApplyFillAsync`. Short-direction fills leave the registry empty for that position.           |
| `ExecutionEngine.CurrentMode`               | Hardcoded to `"paper"`. `OperationModeService.CurrentMode` is never queried.                                                             |
| `MetricsCollector`                          | Counters defined but never incremented by `OrderRouter`, `EntryEvaluator`, or `ExitEvaluator`.                                           |
| `DelegatingExchangeAdapter.SetLiveAdapters` | Method exists but is never called. Live mode is unreachable at runtime.                                                                  |

---

## 7 — Rate Limits

Kraken uses a counter-based rate limit system for private REST endpoints. The counter increments
on each call and decays over time. Exact values depend on the account tier (Starter, Intermediate,
Pro).

**Order-related:**  
- Starter: max 60 API calls/rolling 120 s window for trading endpoints.  
- Use `cl_ord_id` instead of `txid` lookups where possible to reduce query calls.  
- Prefer the `executions` WS channel for fill tracking instead of polling `QueryOrders`.

**Error codes to watch for:**
- `EOrder:Rate limit exceeded` — trading rate limit. Back off 60 s.
- `EAPI:Rate limit exceeded` — API-level. Back off 60 s.

**Implemented:** `KrakenRestAdapter.HandleKrakenError` sets `_isRateLimited = true` with
`_rateLimitResumesAt = UtcNow + 60s` on either error. ✅

---

## 8 — API Key Permissions Required

Minimum permissions to support full Execution Service functionality:

| Permission                                       | Used for                                                      |
| ------------------------------------------------ | ------------------------------------------------------------- |
| `Funds permissions - Query`                      | `Balance`, `TradeBalance`                                     |
| `Orders and trades - Query open orders & trades` | `OpenOrders`, `QueryOrders`, `TradesHistory`, `OpenPositions` |
| `Orders and trades - Create & modify orders`     | `AddOrder`                                                    |
| `Orders and trades - Cancel & close orders`      | `CancelOrder`, `CancelAllOrders`                              |
| `WebSocket interface - On`                       | `GetWebSocketsToken`                                          |


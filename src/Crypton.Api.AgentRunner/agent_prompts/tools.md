# Crypton — Agent Tool Reference

This document is the authoritative specification for all agent-callable tools. Read it once when starting a new session; refer back when you need to confirm parameter names, return shapes, or calling conventions.

---

## Calling Convention

Tools are invoked using XML tags with a JSON argument object. Use the following syntax on its own line:

```
<tool_call>tool_name {"param": "value", "param2": value}</tool_call>
```

Rules:
- The tag must be `<tool_call>` (lowercase, no namespace).
- The tool name immediately follows the opening tag, separated by a space from the JSON object.
- The JSON object must be valid JSON (`"key": "value"` — always double-quoted keys).
- Omit optional parameters to use their defaults.
- Do not invent parameter names — only those listed here are valid.
- If a tool call fails, the error message will describe the cause. Do not retry indefinitely; log the failure and proceed with the information you have, noting the gap in your output.

---

## `web_search`

Search the web via the Brave Search API. Returns a ranked list of results suitable for identifying sources to fetch.

### Parameters

| Parameter | Type    | Required | Default   | Description                                                                                          |
| --------- | ------- | -------- | --------- | ---------------------------------------------------------------------------------------------------- |
| `query`   | string  | yes      | —         | The search query. Be specific. Include asset names, dates, and key terms rather than broad keywords. |
| `count`   | integer | no       | 10        | Number of results to return. Range: 1–20.                                                            |
| `recency` | string  | no       | `"any"`   | Filter results by age. Accepts: `"day"`, `"week"`, `"month"`, `"year"`, `"any"`.                     |
| `market`  | string  | no       | `"en-US"` | Locale string for regional result filtering.                                                         |

### Returns

A list of up to `count` results, each containing:
- `title` — page title
- `url` — full URL
- `snippet` — 1–2 sentence excerpt
- `age` — publication age estimate (where available)

### Example

```
<tool_call>web_search {"query": "Kraken exchange withdrawal issues February 2026", "count": 5, "recency": "week"}</tool_call>
```

### Notes

- Results contain snippets only — use `web_fetch` to read content in full.
- Prefer specific queries over broad ones. `"Fed funds rate decision March 2026"` returns better results than `"Fed interest rates"`.
- Use `recency` aggressively. For news and sentiment signals, stale results are worse than no results.

---

## `web_fetch`

Fetch and extract the readable content of a web page. Strips navigation, ads, and boilerplate, returning clean prose.

### Parameters

| Parameter    | Type    | Required | Default | Description                                                                                                              |
| ------------ | ------- | -------- | ------- | ------------------------------------------------------------------------------------------------------------------------ |
| `url`        | string  | yes      | —       | The full URL to fetch. Must begin with `https://`.                                                                       |
| `max_length` | integer | no       | 8000    | Maximum character length of the extracted content. Increase for long-form reports; decrease if only a summary is needed. |

### Returns

A single string containing the cleaned, readable content of the page. If extraction fails or content is behind a paywall, returns a best-effort partial extract with a `[TRUNCATED]` or `[PAYWALL]` marker.

### Example

```
<tool_call>web_fetch {"url": "https://www.federalreserve.gov/monetarypolicy/fomcminutes20260129.htm"}</tool_call>
```

### Notes

- Always validate the URL from a `web_search` result before fetching. Snippets can mislead.
- If the page is a PDF, the tool will attempt text extraction but formatting may be degraded.
- For data-heavy pages (e.g., Quiver Quant, Capitol Trades), fetch the specific URL for the asset or topic in question rather than the homepage.

---

## `bird`

Query X (Twitter) for posts, user timelines, and trending topics via the `bird` CLI. Used for real-time sentiment, influencer signals, and first-hand market commentary.

### Parameters

| Parameter | Type    | Required | Default    | Description                                                                                                                                                          |
| --------- | ------- | -------- | ---------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `query`   | string  | yes      | —          | A search query or `@username` for a specific account's timeline. Supports standard X search operators (`from:`, `since:`, `until:`, `#hashtag`, `-filter:retweets`). |
| `count`   | integer | no       | 20         | Number of posts to return. Range: 1–50.                                                                                                                              |
| `recency` | string  | no       | `"day"`    | Time window. Accepts: `"hour"`, `"day"`, `"week"`. Social signals degrade quickly; default is tighter than `web_search`.                                             |
| `mode`    | string  | no       | `"search"` | `"search"` for keyword/operator queries; `"timeline"` for a specific user's recent posts. When using `"timeline"`, `query` should be `@username`.                    |

### Returns

A list of posts, each containing:
- `author` — display name and handle
- `text` — full post text
- `timestamp` — ISO 8601 datetime
- `engagement` — like, repost, and reply counts (where available)

### Example

```
<tool_call>bird {"query": "@unusualwhales", "mode": "timeline", "count": 10, "recency": "day"}</tool_call>
<tool_call>bird {"query": "bitcoin ETF inflows -filter:retweets", "count": 20, "recency": "day"}</tool_call>
```

### Notes

- Engagement counts are a rough signal-to-noise filter: high-engagement posts from credible accounts carry more weight than low-engagement posts from unknown accounts.
- Cross-verify social claims with `web_search` before including them in findings. X is a primary signal source, not a ground truth.
- For trend-spotting, use keyword search. For known high-signal accounts (e.g., `@unusualwhales`, `@WatcherGuru`), use `timeline` mode directly.

---

## `current_position`

Retrieve the current portfolio state from the Execution Service via a REST call. This is the ground truth for all portfolio-aware reasoning.

### Parameters

| Parameter         | Type    | Required | Default | Description                                                                                                                                                                                      |
| ----------------- | ------- | -------- | ------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `include_history` | boolean | no       | `false` | When `true`, includes the full trade history for the current strategy cycle in addition to the current snapshot. Set to `true` for evaluation; `false` is sufficient for analysis and synthesis. |

### Returns

A structured object containing:
- `mode` — `"paper"` or `"live"`
- `timestamp` — time of snapshot
- `total_value` — total portfolio value in base currency (USD)
- `available_capital` — uninvested capital available for new positions
- `total_exposure_pct` — percentage of total value currently deployed
- `unrealized_pnl` — total unrealized P&L across all open positions
- `positions` — array of open positions, each with:
  - `id`, `asset`, `direction` (`long` / `short`)
  - `entry_price`, `current_price`, `quantity`
  - `allocation_pct`, `unrealized_pnl`, `unrealized_pnl_pct`
  - `opened_at`, `strategy_cycle`
- `realized_pnl_cycle` — total realized P&L since the current strategy cycle began (only when `include_history=true`)
- `trade_history` — array of completed trades for the current cycle (only when `include_history=true`), each with:
  - `id`, `asset`, `direction`, `entry_price`, `exit_price`, `quantity`
  - `opened_at`, `closed_at`, `exit_reason`
  - `pnl`, `pnl_pct`

### Example

```
<tool_call>current_position {}</tool_call>
<tool_call>current_position {"include_history": true}</tool_call>
```

### Notes

- Call this early in your procedure. Portfolio state is essential context — it changes the meaning of any signal.
- In paper mode, all values are simulated. The return structure is identical to live mode.
- If the Execution Service is unreachable, the tool returns a cached snapshot with a `stale_at` timestamp. Note this in your output.

---

## `technical_indicators`

Fetch or compute technical indicator data for a specified asset and timeframe. Returns structured summaries suitable for direct inclusion in agent context.

### Parameters

| Parameter    | Type    | Required | Default | Description                                                                                                                                                                               |
| ------------ | ------- | -------- | ------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `asset`      | string  | yes      | —       | Base asset symbol. Use **`"BTC"`**, **`"ETH"`**, `"SOL"`, `"XRP"`, etc. Do NOT include the quote currency (no `"XBTUSD"` or `"BTC/USD"`).                                                 |
| `timeframe`  | string  | yes      | —       | Candle interval. Accepts: `"1h"`, `"4h"`, `"1d"`, `"1w"`.                                                                                                                                 |
| `indicators` | string  | no       | `"all"` | Comma-separated list of indicators to compute. Accepts: `"rsi"`, `"macd"`, `"bbands"`, `"ema_20"`, `"ema_50"`, `"ema_200"`, `"volume_profile"`, `"atr"`, `"obv"`, `"stoch_rsi"`, `"all"`. |
| `lookback`   | integer | no       | 100     | Number of candles to include in calculations and trend summaries.                                                                                                                         |

### Returns

A structured object containing:
- `asset`, `timeframe`, `as_of` — metadata
- `price` — current price, 24h high/low, 24h volume
- For each requested indicator:
  - Current value(s)
  - A brief trend summary (e.g., `"RSI declining from 68 to 54 over 5 candles — momentum weakening"`)
  - Key signal level if relevant (e.g., MACD crossover datetime, Bollinger Band width vs. 30-day mean)

### Example

```
<tool_call>technical_indicators {"asset": "BTC", "timeframe": "1d"}</tool_call>
<tool_call>technical_indicators {"asset": "ETH", "timeframe": "4h"}</tool_call>
```

### Notes

- Always specify both `asset` and `timeframe`. Omitting either will return an error.
- Use `"1d"` for directional thesis work; use `"4h"` or `"1h"` for entry timing and stop placement.
- The `atr` indicator is particularly useful for stop-loss calibration — a stop tighter than 1× ATR is likely to be hit by normal volatility.
- Call the tool directly rather than relying on indicator values quoted in `research.md`. Research Agent data may be from an earlier timestamp.

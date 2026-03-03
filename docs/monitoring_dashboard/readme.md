# Crypton Monitoring Dashboard

The Monitoring Dashboard is a Vite + React single-page application that provides a real-time window into every layer of the Crypton system — portfolio state, open positions, active strategy, AI agent activity, cycle performance, and service health.

Data flows through two channels: a persistent SignalR WebSocket connection refreshes agent reasoning, tool-call events, and market prices in real time. A REST polling layer fills in data that does not need sub-second latency (portfolio, performance, strategy, system health). The MonitoringDashboard .NET service at `http://localhost:5001` acts as a reverse proxy; all `/api/*` routes are forwarded to the appropriate back-end service.

---

## Quick Start

```bash
# from workspace root
cd src/Crypton.Web.Dashboard
npm install
npm run dev          # opens at http://localhost:5173
```

The dashboard will start polling and connecting immediately. The Status Bar at the top shows connection state at a glance. No configuration is needed for local development as long as the three back-end services are running (see [docker-compose.yml](../../docker-compose.yml)).

---

## Layout

```
┌─────────────────────────────────────────────────┐
│  Status Bar  (connection + service health)       │
├─────────────────────────────────────────────────┤
│  Tab Bar  (Main | Analysis | Diagnostics | …)    │
├─────────────────────────────────────────────────┤
│                                                  │
│  Panel Grid  (collection of panels for this tab) │
│                                                  │
└─────────────────────────────────────────────────┘
```

### Status Bar

The header strip provides an always-visible system summary.

**Left side (left → right):**

| Element          | Meaning                                                                                      |
| ---------------- | -------------------------------------------------------------------------------------------- |
| `● Dashboard`    | Dashboard SignalR connection dot — green = connected, amber = connecting, red = disconnected |
| `● MD`           | MarketData service chip — dot colour reflects online / degraded / offline                    |
| `● ES`           | ExecutionService chip                                                                        |
| `● AR`           | AgentRunner chip. Hovering shows the full status detail text for each service.               |
| `Mode: Paper`    | Current strategy execution mode (Paper or Live)                                              |
| `Agent: <state>` | Current agent state name                                                                     |

**Right side:**

| Element   | Meaning                                         |
| --------- | ----------------------------------------------- |
| `⌘K`      | Opens the Command Palette                       |
| Timestamp | Wall-clock time of the most recent data refresh |

---

## Tabs

Tabs let you arrange panels into separate workspaces. Up to **8 tabs** can be open at once.

### Default tabs

| Tab             | Default panels                                                                                                       |
| --------------- | -------------------------------------------------------------------------------------------------------------------- |
| **Main**        | Portfolio Summary, Strategy Overview, Agent State, Cycle Performance, Price Ticker (BTC/USD), Price Ticker (ETH/USD) |
| **Analysis**    | Loop State, Reasoning Trace, Tool Calls, Technical Indicators (BTC), Open Positions, Holdings                        |
| **Diagnostics** | System Diagnostics                                                                                                   |

Tab state (which panels exist, in which tabs) is **persisted to localStorage**, so your custom layout survives a page reload.

### Tab controls

| Action                   | How                                        |
| ------------------------ | ------------------------------------------ |
| Switch tab               | Click the tab title, or `⌘1`–`⌘8`          |
| New tab                  | Click `+` in the tab bar, or `⌘T`          |
| Close tab                | Click `✕` on the tab, or `⌘W`              |
| Reorder tabs             | Drag and drop a tab header                 |
| Duplicate / close others | Right-click a tab to open the context menu |

---

## Command Palette

Press `⌘K` (or `Ctrl+K`) to open the command palette — the central hub for adding panels and triggering actions.

Type to filter. Use `↑`/`↓` to move and `Enter` to execute. `Escape` closes it.

### Available commands

**Agent Controls** — appears at the top of the palette (always visible without scrolling):

| Command                    | Effect                                                                            |
| -------------------------- | --------------------------------------------------------------------------------- |
| Agent: Force New Cycle     | Asks AgentRunner to start a new cycle immediately (`POST /api/agent/force-cycle`) |
| Agent: Pause Loop          | Suspends the agent loop (`POST /api/agent/pause`)                                 |
| Agent: Resume Loop         | Resumes a paused loop (`POST /api/agent/resume`)                                  |
| Agent: Abort Current Cycle | Cancels the in-progress cycle (`POST /api/agent/abort`)                           |

**Add Panel** — adds a new instance of the panel to the active tab.

**Navigation** — one entry per open tab; switches to that tab.

**Actions:**

| Command          | Effect                   |
| ---------------- | ------------------------ |
| Create New Tab   | Opens a blank tab (`⌘T`) |
| Refresh All Data | Hard-reloads the page    |

Recently used commands appear at the top of the list when the palette is open with no query.

---

## Keyboard Shortcuts

| Shortcut        | Action                                |
| --------------- | ------------------------------------- |
| `⌘K` / `Ctrl+K` | Toggle command palette                |
| `⌘T` / `Ctrl+T` | New tab                               |
| `⌘W` / `Ctrl+W` | Close active tab                      |
| `⌘1`–`⌘8`       | Switch to tab 1–8                     |
| `⌘S` / `Ctrl+S` | Force-refresh all data (REST re-poll) |
| `Escape`        | Close command palette                 |

---

## Panel Reference

Each panel is an independent widget that reads from the shared Zustand store. Panels can be added multiple times or mixed freely across tabs. The panel title (header bar) is set by the `PanelGrid` wrapper; the body content is described below.

---

### Portfolio Summary

**Purpose:** High-level financial snapshot of the portfolio.

**Shows:**
- Total portfolio value in USD (large font)
- 24-hour dollar change with `▲`/`▼` arrow and percentage
- Unrealized P&L
- Available capital (cash not in positions)

**Data source:** `GET /api/portfolio/summary` — polled on load and on `⌘S`.

---

### Holdings

**Purpose:** Breakdown of what the portfolio currently holds.

**Shows:** A row per asset with symbol + quantity on the left, dollar value + allocation percentage on the right. Rows are separated by subtle borders.

**Data source:** `GET /api/portfolio/holdings`.

---

### Open Positions

**Purpose:** Live view of all active trading positions.

**Shows:** One card per position containing:
- Asset symbol and direction badge (`LONG` / `SHORT`)
- Unrealized P&L as a percentage
- Entry price and current market price
- Stop-loss level

Cards with a position near the stop-loss or take-profit level are highlighted with an amber border as an early warning.

**Data source:** `GET /api/portfolio/positions`.

---

### Strategy Overview

**Purpose:** Summary of the active strategy's posture and risk parameters.

**Shows:**
- Execution mode (`Live` with red dot / `Paper` with blue dot) and strategy validity time remaining
- Posture badge: `Aggressive` (amber), `Moderate` (blue), `Defensive` (green), `Flat` (grey), `Exit All` (red)
- Maximum drawdown limit (%)
- Daily loss limit (%)
- Maximum exposure (%)

**Data source:** `GET /api/strategy/current`.

---

### Agent State

*(also registered as **Agent Activity** — same component)*

**Purpose:** Snapshot of what the AI agent is doing right now.

**Shows:**
- Pulsing green dot when the agent is running + current state name in large monospace font
- Active agent identifier (sub-label)
- Progress bar with percentage complete
- Token counter for the current cycle
- Name of the tool currently being called (if any)

**Data source:** Agent state pushed via SignalR from AgentRunner; falls back to `GET /api/agent/state`.

---

### Reasoning Trace

**Purpose:** Live stream of the agent's internal reasoning steps, as they are generated.

**Shows:** A scrolling log of reasoning entries, each with a monospace timestamp and the text content. The newest entry is rendered in primary text colour; older entries fade to secondary. Shows "Waiting for agent…" when empty.

**Tip:** This panel fills very quickly during an active cycle. Scroll to the bottom to follow along in real time; the panel does not auto-scroll to avoid interrupting manual review.

**Data source:** SignalR token stream from AgentRunner hub (`reasoning` group).

---

### Tool Calls

**Purpose:** Log of recent tool invocations by the agent.

**Shows:** Up to 10 of the most recent tool calls as clickable cards:
- Tool name (blue monospace)
- Duration in milliseconds (right-aligned)
- Input preview (first 50 characters)
- Completion status: `✓` or `✗ <error message>`

Error calls are highlighted with a red background. Clicking a card sets `selectedToolCallId` in the store, which the **Tool Call Detail** panel reads.

**Data source:** SignalR tool-call events from AgentRunner hub (`toolcalls` group).

---

### Tool Call Detail

**Purpose:** Full drill-down for a tool call selected in the Tool Calls panel.

**Shows:** When a tool call is selected:
- Tool name in the header (with a `✕` clear button)
- Status (Completed / Error / Running)
- Duration (ms)
- Timestamp
- Full input JSON in a scrollable `CodeBlock`
- Full output JSON in a scrollable `CodeBlock` (when available)

Shows "Select a tool call to view details" when nothing is selected.

**Workflow:** Place **Tool Calls** and **Tool Call Detail** side-by-side. Click any row in Tool Calls to populate Tool Call Detail instantly.

---

### Loop State

**Purpose:** Control panel for the agent's execution loop.

**Shows:**
- Step progress timeline: `Plan → Research → Analyze → Synthesize → Execute → Evaluate` with dots coloured green (complete) / active / grey (pending)
- Current step label (large font)
- Cycle number and overall progress percentage
- **Cycle interval selector**: dropdown with options 1h / 2h / 4h / 6h / 12h / 24h — changing this calls `POST /api/agent/config/cycle-interval` immediately
- Next scheduled cycle time

**Data source:** `GET /api/agent/loop` + `GET /api/agent/config/cycle-interval`; writes via `POST /api/agent/config/cycle-interval`.

---

### Loop Timeline

**Purpose:** Visual alternative to Loop State — a more graphical representation of the current loop phase.

**Shows:** A horizontal bar showing cycle number and percentage, then six labelled circle nodes (Planning → Research → Analysis → Strategy → Execution → Evaluation) where completed steps are green-filled with `✓`, the current step has an info-colour border, and pending steps are grey. A text badge at the bottom shows the raw current-state string.

**Data source:** Same as Loop State.

---

### Cycle Performance

**Purpose:** P&L scorecard for the current trading cycle.

**Shows:**
- Total P&L (realized + unrealized) in large green/red font
- Realized P&L and unrealized P&L breakdown
- Win rate (%)
- Winning trades / total trades
- Maximum drawdown (%)
- Average winning trade value

**Data source:** `GET /api/performance/cycle`.

---

### Daily Loss Limit

**Purpose:** Real-time safety gauge for the daily loss limit.

**Shows:** A status badge (`✓ ACTIVE` / `⚠ WARNING` / `⚠ BREACHED`) in a coloured background, the dollar amounts (`$used / $limit`), and a horizontal fill bar.

- Green when loss is below 80% of limit
- Amber warning when loss is ≥ 80% of limit
- Red when limit is breached

The limit value is currently hard-coded in the component as `$1,000`. When breached, the ExecutionService should halt new entries automatically; this panel surfaces that state visually.

**Data source:** `cycle.dailyLossLimitBreached` from `GET /api/performance/cycle`.

---

### Evaluation Rating

**Purpose:** Letter-grade summary of recent cycle performance from the agent's self-evaluation.

**Shows:**
- A prominent 48×48px grade tile: `A` (Excellent, green), `B` (Good, teal), `C` (Average, amber), `D` (Below Average, orange), `F` (Poor, red)
- Trend indicator: `▲ Improving`, `▼ Declining`, or `─ Stable`
- Net P&L, return %, win rate, and trade count in a 2×2 grid
- Verdict text (the agent's written assessment of the cycle) in a small text box below

**Data source:** `GET /api/performance/evaluation`.

---

### Cycle History

**Purpose:** Quick overview of historical cycles.

**Shows:** A compact scrollable table (up to 20 rows) with three columns: Date, P&L, Win Rate. Rows are colour-coded green/red by P&L sign. Hovering highlights a row.

**Note:** Clicking a row does not currently open a detail view from within this panel — use **Cycle Detail** for interactive drill-down.

**Data source:** `GET /api/performance/cycles`.

---

### Last Cycle Summary

**Purpose:** Compact card summarising the most recently completed cycle.

**Shows:**
- "Last Cycle" label + relative age (e.g. "2 hours ago")
- P&L and win rate as two large figures
- Trade count and duration
- Evaluation rating (A–F with `↑`/`↓` trend from the current evaluation)

**Note:** This panel is not currently reachable from the command palette (it is registered in the component map but omitted from `PANEL_COMMANDS`). Add it manually via the store or add an entry in `CommandPalette.tsx`.

**Data source:** `GET /api/performance/cycle` + `GET /api/performance/evaluation`.

---

### Cycle Detail

**Purpose:** Interactive drill-down into individual historical cycles.

**Shows:**
- A selectable list of the last 10 cycles, each showing date, P&L, trade count, and win rate
- Clicking a cycle fetches and expands a detail pane below the list showing P&L, win rate, winning/total trades, max drawdown, and strategy name

**Note:** Like Last Cycle Summary, this panel is not in `PANEL_COMMANDS`. Add it via the command palette source or `addPanel` directly.

**Data source:** List from `GET /api/performance/cycles`; detail from `GET /api/strategy/{id}` (proxied from ExecutionService).

---

### System Diagnostics

**Purpose:** Detailed health report for all three back-end services.

**Shows:** An overall status line ("All Systems Operational" / "Degraded" / "One or more services offline") and one **service card** per service (MarketData, ExecutionService, AgentRunner). Each card includes:
- Coloured status dot + name + status badge (`Online` / `Degraded` / `Offline`)
- `WS` badge showing whether SignalR is connected to that service
- Last-checked timestamp
- Human-readable description of the service's role
- Detail text (e.g. current version, uptime)
- Alert list (amber warning boxes for any active issues)
- Key metric pairs in a two-column grid (cache size, queue depth, etc.)

A manual `↻ Refresh` button triggers an immediate re-poll without waiting for the 15-second automatic cycle.

**Data source:** `GET /api/system/status` — polled every 15 seconds by the MonitoringDashboard service, pushed into the store.

---

### System Status

**Purpose:** Compact at-a-glance status summary for all back-end services.

**Shows:**
- An overall banner: `All Systems Operational` (green), `Degraded` (amber), or `Service Outage` (red)
- One row per service showing a coloured status dot, service name, status label (`Online` / `Degraded` / `Offline`), and last-checked timestamp

Use this panel when you want a minimal health indicator that doesn't take much space. For full detail including metrics and alerts, use **System Diagnostics**.

**Data source:** `systemHealth` store state, sourced from `GET /api/system/status`.

---

### Connection Health

**Purpose:** Per-service matrix of HTTP reachability and SignalR WebSocket connectivity.

**Shows:** A three-column table (Service / HTTP / WS) with a row for the dashboard itself and one row per back-end service:
- **HTTP** — green `Up` if the service responded to its last health check, red `Down` if offline
- **WS** — green `Connected` if SignalR is currently subscribed, red `Disconnected` if not, `N/A` if SignalR is not applicable for that service

Useful for quickly diagnosing whether a data-feed problem is an HTTP issue (service down) or purely a WebSocket drop.

**Data source:** `systemHealth` store state + `connectionStatus` (dashboard's own SignalR state).

---

### Error Log

**Purpose:** Aggregated view of all active errors and warnings across the system.

**Shows:** A scrollable list of entries, each tagged with the source service:
- Red `✗` entries for service outages and failed agent tool calls
- Amber `⚠` entries for degraded-service alerts and warnings from service metrics

Shows a green `✓ No errors or warnings` message when everything is clean.

**Data source:** Derived from `systemHealth` service alerts + failed tool calls in `agent.toolCalls`.

---

### Price Ticker

**Purpose:** Live price display for a single asset.

**Shows:**
- Asset symbol and current price (large monospace, with a brief green/red flash animation on each tick)
- 24-hour change with `▲`/`▼` arrow, percentage, and dollar amount
- Bid and ask prices
- 24-hour high/low range

**Configuration:** The panel accepts an `asset` config key (default `BTC/USD`). To monitor multiple assets, add multiple Price Ticker panels with different asset values — e.g. `ETH/USD`, `SOL/USD`. Set this in the command palette or by editing `appsettings.json`.

**Data source:** `GET /api/market/prices` via SignalR market-data events; falls back to REST polling.

---

### Price Chart

**Purpose:** Candlestick chart with overlay of relevant position entry/stop-loss/take-profit levels.

**Shows:**
- OHLCV candlestick bars (green for up-days, red for down-days) via Recharts
- Horizontal reference lines from the current strategy's position rules: entry levels (blue), stop-losses (red), and take-profits (green)
- Responsive container — fills whatever width the panel occupies

**Configuration:** Accepts `asset` (default `BTC/USD`) and `timeframe` (default `1h`). Data is refreshed every 30 seconds.

**Data source:** `GET /api/market/ohlcv?asset=BTC/USD&timeframe=1h&limit=100` — fetched directly from the panel on mount and every 30s.

---

### Technical Indicators

**Purpose:** Key momentum and trend indicators for a single asset.

**Shows:**
- **RSI (14)** — numeric value with a fill bar. Red when ≥ 70 (overbought), green when ≤ 30 (oversold), neutral otherwise.
- **MACD** — value, signal line, and histogram with sign-coloured histogram value (`+` green, `-` red)
- **Bollinger Bands** — upper (red), middle (neutral), and lower (green) band prices

**Configuration:** Accepts `asset` (default `BTC/USD`).

**Note:** If live indicator data is unavailable from the MarketData service, the panel falls back to hard-coded sample values so the layout is always populated.

**Data source:** `GET /api/market/indicators`.

---

## Workflow Guides

### Monitoring a Live Run

1. Open the **Main** tab — it provides the core financial and agent status at a glance.
2. Watch **Agent State** for the current loop phase and progress.
3. Switch to **Analysis** for the real-time stream:
   - **Reasoning Trace** shows the agent's chain-of-thought as tokens arrive.
   - **Tool Calls** shows every tool the agent invokes with pass/fail status.
   - Click a tool call row to open the full input/output in **Tool Call Detail** (add that panel if not present).
4. **Cycle Performance** and **Daily Loss Limit** are the key financial risk indicators — keep them visible.
5. If an alert appears in the Status Bar chips, open **Diagnostics** → **System Diagnostics** for full detail.

---

### Reviewing Cycle Performance After Completion

1. Open **Evaluation Rating** to see the agent's self-assessed grade and verdict text.
2. Open **Last Cycle Summary** for a compact P&L + win rate card.
3. Open **Cycle History** to spot trends across the last 20 cycles.
4. For a deeper dive, open **Cycle Detail**: click any cycle row to expand P&L, drawdown, and strategy info.

---

### Configuring the Loop Schedule

1. Open **Loop State** (in the Analysis tab, or add via `⌘K → Loop State`).
2. Use the **Cycle interval** dropdown to select 1h, 2h, 4h, 6h, 12h, or 24h.
3. The new interval takes effect immediately — the panel calls `POST /api/agent/config/cycle-interval` on change.
4. The "Next cycle at" timestamp updates to reflect the new schedule.

---

### Controlling the Agent Mid-Run

Use the command palette (`⌘K`) for all agent control commands — no need to navigate away from your current view.

| Goal                                  | Command                      |
| ------------------------------------- | ---------------------------- |
| Trigger a cycle now (skip the wait)   | `Agent: Force New Cycle`     |
| Pause the loop after the current step | `Agent: Pause Loop`          |
| Resume after pausing                  | `Agent: Resume Loop`         |
| Stop the current cycle immediately    | `Agent: Abort Current Cycle` |

**Loop State** and **Loop Timeline** will reflect the new state within seconds via SignalR.

---

### Building a Custom Workspace

1. Press `⌘T` to open a new blank tab. Name it by double-clicking the tab label (not yet implemented — see Appendix).
2. Press `⌘K`, type the panel name, and hit `Enter` to add it to the tab.
3. Repeat for all panels you want in this view.
4. Drag panels to reorder within the grid (drag by the panel header).
5. All changes persist to localStorage — your layout will survive a page reload.

**Example specialist dashboards:**

- **Risk monitor**: Daily Loss Limit + Open Positions + Strategy Overview + Cycle Performance
- **Agent deep-dive**: Reasoning Trace + Tool Calls + Tool Call Detail + Loop Timeline + Agent State
- **Market overview**: Price Chart (BTC) + Price Chart (ETH) + Technical Indicators (BTC) + Technical Indicators (ETH) + Price Ticker (BTC) + Price Ticker (ETH)

---

## Monitoring System Health

### Status Bar (quick read)

The three service chips give an immediate at-a-glance health check:

- **MD** → MarketData: exchange feed, price cache, technical indicators
- **ES** → ExecutionService: order management, position tracking
- **AR** → AgentRunner: AI loop, cycle orchestration

Green = online, amber = degraded (one or more subsystems reporting issues), red = offline (no response).

### System Diagnostics panel (detailed read)

The panel in the **Diagnostics** tab expands each chip into a full card showing:
- The specific issue in the detail text line
- Any alerts from the service (shown as amber warning boxes)
- Internal metrics (cache sizes, queue depths, connection counts)
- Whether the dashboard's SignalR subscription to each service is active (`WS` badge)

**Common diagnostic actions:**

| Symptom                                  | What to check                                                                                                                                                         |
| ---------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Reasoning Trace/Tool Calls stop updating | AR chip colour + `WS` badge in System Diagnostics. If `WS` is grey, SignalR to AgentRunner has dropped. The dashboard retries automatically; wait 10–15 s or refresh. |
| Price data stale                         | MD chip. Check Detail text for exchange adapter status and the alerts section for rate-limit warnings.                                                                |
| Positions/orders not updating            | ES chip. Check for circuit-breaker alerts in the metrics grid.                                                                                                        |
| All chips amber or red                   | Check docker-compose services are running: `docker compose ps`.                                                                                                       |

---

## Appendix: Unimplemented Panels

Several panel types are defined in the `PanelType` union and listed in the command palette, but have no corresponding React component. Selecting them via `⌘K → Add [Panel Name]` will add a panel slot that renders empty.

### Panels in command palette but no component

| Panel                    | Description as shown in palette  | Notes                                                                                      |
| ------------------------ | -------------------------------- | ------------------------------------------------------------------------------------------ |
| **Trade History**        | Recent trades with P&L           | `GET /api/portfolio/trades` endpoint exists; component not yet written                     |
| **Strategy Parameters**  | Risk limits and constraints      | Would surface `strategy.current.riskParameters` detail                                     |
| **Strategy Rationale**   | Human-readable strategy text     | The strategy response includes rationale text — needs a prose component                    |
| **Position Rules**       | Entry conditions and allocations | `strategy.current.positionRules[]` array available; needs table component                  |
| **Macro Signals**        | Trend, volatility, sentiment     | `GET /api/market/macro` endpoint exists; component not yet written                         |
| **Lifetime Performance** | Total P&L and statistics         | `GET /api/performance/lifetime` endpoint exists; component not yet written                 |
| **Recommendations**      | Key points from evaluation       | Evaluation response likely contains a `recommendations[]` field; component not yet written |

### Panels registered in components but not in the command palette

| Panel                  | Access workaround                                                                                                    |
| ---------------------- | -------------------------------------------------------------------------------------------------------------------- |
| **Last Cycle Summary** | Add via `addPanel('last-cycle-summary')` in browser console, or add a `PANEL_COMMANDS` entry in `CommandPalette.tsx` |
| **Cycle Detail**       | Same as above                                                                                                        |

### Other gaps

- **Tab renaming**: Tabs cannot be renamed via the UI after creation. The title is set by `addTab(title)` at creation time; there is no inline edit.
- **Panel resize / drag-and-drop positioning**: The `PanelConfig` schema supports `x`, `y`, `width`, and `height` fields, but the current `PanelGrid` renders panels in a CSS grid flow — free drag-and-drop repositioning is not yet wired up.
- **Daily loss limit is hard-coded**: `DailyLossLimitPanel` hard-codes `$1,000` as the limit. It should read this value from the strategy's risk parameters.
- **Refresh data on reconnect**: When the SignalR connection to AgentRunner drops and reconnects, stale reasoning and tool-call lists are not cleared. A `clearAgentBuffers()` call on reconnect would prevent showing data from a previous cycle session.
- **No panel-level error boundaries**: If any panel throws an unhandled exception it will propagate to the top-level React error boundary and blank the whole tab. Per-panel `ErrorBoundary` wrappers would contain failures to the affected panel only.

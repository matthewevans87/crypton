# Monitoring Dashboard Feature Backlog

## Overview

This backlog defines all features required to build the Monitoring Dashboard - a real-time, high-density, information-rich web visualization layer inspired by F35 cockpit interfaces and professional trading terminals. The dashboard provides complete transparency into what the agents are doing, thinking, reading, searching for, and their lines of reasoning.

**Design Philosophy:** "All signal; no noise." Every pixel must justify its existence. No decoration, no redundancy. The UI is the application.

**Key Paradigms:**
- Zero scrolling - everything fits in the viewport
- VS Code-style tabbed interface with draggable panels
- Command palette-driven workflow (Cmd+K)
- Maximum information density with intelligent collapse
- Real-time dynamism - live data streaming with motion conveying information
- F35 cockpit aesthetic: clean crisp lines, dark theme, high contrast

---

## Priority Legend

| Priority | Description |
|----------|-------------|
| P0 | Critical - Must work for basic functionality |
| P1 | High | Operational monitoring and observability |
| P2 | Medium | Enhanced features and user experience |
| P3 | Low | Polish, testing, deployment |

---

## 1. UI/UX Framework (Core)

The foundational architecture that makes the cockpit paradigm possible.

### 1.1 Viewport Architecture

| ID | Feature | Priority |
|----|---------|----------|
| UI-001 | Fixed viewport (100vh x 100vw) with zero scrolling | P0 |
| UI-002 | Implement tab bar (VS Code style) at top - create, close, switch tabs | P0 |
| UI-003 | Tab state persistence - restore tabs on page reload | P1 |
| UI-004 | Tab drag-and-drop reordering | P2 |
| UI-005 | New tab button (+) creates empty workspace | P0 |
| UI-006 | Maximum 8 tabs per workspace (configurable) | P2 |
| UI-007 | Tab context menu (close, close others, close all, duplicate) | P2 |

### 1.2 Panel System

| ID | Feature | Priority |
|----|---------|----------|
| UI-008 | Panel container with header, content, resize handles | P0 |
| UI-009 | Drag panel by header to reposition | P0 |
| UI-010 | Resize panels by dragging edges/corners | P0 |
| UI-011 | Snap-to-grid layout (8px grid) during drag | P0 |
| UI-012 | Panel maximize to full viewport (double-click header) | P0 |
| UI-013 | Restore maximized panel (ESC key or button) | P0 |
| UI-014 | Panel close button (X) in header | P0 |
| UI-015 | Panel header shows title and key summary metric | P0 |
| UI-016 | Minimum panel size constraints (min-width: 200px, min-height: 100px) | P1 |
| UI-017 | Panel layout persistence per tab | P1 |

### 1.3 Intelligent Panel Collapse

| ID | Feature | Priority |
|----|---------|----------|
| UI-018 | Auto-collapse panels that don't fit in viewport to 28px summary bar | P0 |
| UI-019 | Collapsed panel shows: icon, title, key metric (e.g., "$127k", "BTC ▲") | P0 |
| UI-020 | Click collapsed bar to restore panel | P0 |
| UI-021 | Maximize one panel forces smart collapse of another if space needed | P0 |
| UI-022 | Collapse preference per panel type (user can lock collapse behavior) | P2 |
| UI-023 | Manual collapse toggle (pin icon in panel header) | P1 |

### 1.4 Command Palette

| ID | Feature | Priority |
|----|---------|----------|
| UI-024 | Cmd+K / Ctrl+K opens command palette overlay | P0 |
| UI-025 | Fuzzy search filter as user types | P0 |
| UI-026 | Command categories: Add Panel, Navigate, Settings, Actions | P0 |
| UI-027 | "Add Panel" commands add panel to current tab at optimal position | P0 |
| UI-028 | Recent commands history (last 10) | P1 |
| UI-029 | Command keyboard shortcut hints in palette | P1 |
| UI-030 | ESC closes command palette | P0 |

---

## 2. Visual Design System

The aesthetic foundation - F35 cockpit meets Bloomberg Terminal.

### 2.1 Color Palette

| ID | Feature | Priority |
|----|---------|----------|
| VD-001 | Deep dark background (#0a0a0f) for viewport | P0 |
| VD-002 | Panel background (#0f0f1a) with subtle border (#1a1a2e) | P0 |
| VD-003 | Primary text: off-white (#e0e0e0) | P0 |
| VD-004 | Secondary text: muted gray (#808090) | P0 |
| VD-005 | Profit/positive: cyan-green (#00ffc8) | P0 |
| VD-006 | Loss/negative: coral-red (#ff4466) | P0 |
| VD-007 | Warning: amber (#ffaa00) | P0 |
| VD-008 | Info/neutral: blue (#4488ff) | P0 |
| VD-009 | Active/running state: bright green (#00ff88) | P0 |
| VD-010 | Idle/waiting state: gray (#666680) | P0 |
| VD-011 | Panel glow accent color (#00ffc8 at 20% opacity) for attention | P0 |
| VD-012 | Syntax highlighting colors for code/JSON display | P1 |

### 2.2 Typography

| ID | Feature | Priority |
|----|---------|----------|
| VD-013 | Monospace font for all data (JetBrains Mono or Fira Code) | P0 |
| VD-014 | Sans-serif for labels (Inter or system-ui) | P0 |
| VD-015 | Base font size: 12px for data, 11px for labels | P0 |
| VD-016 | Compact line height (1.2) for density | P0 |
| VD-017 | Font weight: 400 for data, 500 for labels, 600 for headers | P0 |
| VD-018 | Tab titles: 12px, truncate with ellipsis | P0 |
| VD-019 | Numeric figures: tabular-nums font feature for alignment | P0 |

### 2.3 Layout & Spacing

| ID | Feature | Priority |
|----|---------|----------|
| VD-020 | 4px baseline grid - all spacing in multiples of 4px | P0 |
| VD-021 | Panel padding: 8px | P0 |
| VD-022 | Panel header height: 28px | P0 |
| VD-023 | Panel gap: 4px (gutter) | P0 |
| VD-024 | Border radius: 0px (crisp corners) or max 2px | P0 |
| VD-025 | Border width: 1px for panels, 2px for active/focused | P0 |

### 2.4 Motion & Animation

| ID | Feature | Priority |
|----|---------|----------|
| VD-026 | Data tick animation: smooth number transitions (150ms) | P0 |
| VD-027 | Price change flash: brief color pulse on update (green/red) | P0 |
| VD-028 | Panel appear/disappear: fade + slide (200ms ease-out) | P0 |
| VD-029 | Panel resize: smooth dimension transition | P0 |
| VD-030 | State transitions: smooth color/shape morphs | P0 |
| VD-031 | Panel glow pulse: subtle border animation for attention (1s loop) | P0 |
| VD-032 | Chart real-time draw: smooth line extension | P0 |
| VD-033 | Command palette: scale + fade (150ms) | P0 |
| VD-034 | Disable all animations if prefers-reduced-motion | P2 |

---

## 3. Panel Library

Reusable, composable panels that display specific information domains.

### 3.1 Portfolio Panels

| ID | Feature | Priority |
|----|---------|----------|
| PL-001 | Portfolio Summary Panel: total value, 24h change %, unrealized P&L | P0 |
| PL-002 | Holdings Panel: asset, quantity, value, allocation % | P0 |
| PL-003 | Open Positions Panel: entry price, current price, P&L, stop/target | P0 |
| PL-004 | Position row highlight: glow when near stop-loss or take-profit | P0 |
| PL-005 | Trade History Panel: timestamp, asset, direction, size, entry, exit, P&L | P0 |
| PL-006 | Trade history inline filter (asset, date, outcome) | P1 |
| PL-007 | Position time display: "2h 34m" or "3d 5h" format | P1 |

### 3.2 Strategy Panels

| ID | Feature | Priority |
|----|---------|----------|
| PL-008 | Strategy Overview Panel: mode (paper/live), posture, validity countdown | P0 |
| PL-009 | Strategy Parameters Panel: max drawdown, daily loss limit, max exposure | P0 |
| PL-010 | Strategy Rationale Panel: human-readable strategy text | P0 |
| PL-011 | Position Rules Panel: asset, entry condition, allocation, stop, target | P0 |
| PL-012 | Strategy Rules on Chart: overlay entry/exit lines on price chart | P0 |
| PL-013 | Invalidation Conditions Panel: per-position invalidation rules | P1 |
| PL-014 | Strategy History Panel: list of past strategies with timestamps | P1 |
| PL-015 | Strategy Detail Panel: full strategy.json in collapsible JSON viewer | P1 |

### 3.3 Market Panels

| ID | Feature | Priority |
|----|---------|----------|
| PL-016 | Price Ticker Panel: live price, 24h change, bid/ask spread | P0 |
| PL-017 | Price Chart Panel: candlestick/line chart with zoom/pan | P0 |
| PL-018 | Multi-timeframe selector (1m, 5m, 15m, 1h, 4h, 1d) | P0 |
| PL-019 | Technical Indicators Panel: RSI, MACD, BB values with signals | P0 |
| PL-020 | Indicator overlay on chart (toggleable) | P0 |
| PL-021 | Order Book Panel: depth visualization (bid/ask levels) | P1 |
| PL-022 | Macro Signals Panel: trend, volatility, sentiment indicators | P1 |
| PL-023 | Market Overview Panel: BTC dominance, total market cap, fear/greed | P2 |

### 3.4 Agent Transparency Panels (Critical)

| ID | Feature | Priority |
|----|---------|----------|
| PL-024 | Agent State Panel: current state (Plan/Research/Analyze/etc), timestamp | P0 |
| PL-025 | Agent Activity Indicator: pulsing dot when agent is running | P0 |
| PL-026 | Reasoning Trace Panel: live stream of agent thinking (token by token) | P0 |
| PL-027 | Tool Calls Panel: list of tool invocations with status | P0 |
| PL-028 | Tool Call Detail Panel: click tool call to see input + output | P0 |
| PL-029 | Tool Call Input Panel: shows exact prompt/context sent to tool | P0 |
| PL-030 | Tool Call Output Panel: shows raw tool response | P0 |
| PL-031 | Agent Memory Panel: current context window contents | P1 |
| PL-032 | Mailbox Panel: messages in agent inbox with sender/recipient | P1 |
| PL-033 | Agent Context Panel: system prompt + instructions visible to agent | P2 |
| PL-034 | Search Query Panel: exact search strings agent has executed | P1 |
| PL-035 | Web Fetch Panel: URLs agent has fetched, with snippet preview | P1 |
| PL-036 | Reasoning Lineage Panel: connect conclusions to supporting evidence | P2 |

### 3.5 Performance Panels

| ID | Feature | Priority |
|----|---------|----------|
| PL-037 | Cycle Performance Panel: P&L, win rate, avg trade, drawdown | P0 |
| PL-038 | Daily Loss Limit Panel: status bar (active/breached), threshold | P0 |
| PL-039 | Lifetime Performance Panel: total P&L, trades, win rate, streaks | P1 |
| PL-040 | Equity Curve Panel: line chart of portfolio value over time | P1 |
| PL-041 | Cycle History Panel: list of cycles with metrics | P0 |
| PL-042 | Cycle Detail Panel: link to evaluation.md and strategy.json | P0 |
| PL-043 | Win/Loss Distribution Panel: histogram of trade outcomes | P2 |

### 3.6 Learning Loop Panels

| ID | Feature | Priority |
|----|---------|----------|
| PL-044 | Loop State Panel: current step, time in state, progress % | P0 |
| PL-045 | Loop Timeline Panel: visual timeline of cycle progress | P0 |
| PL-046 | Last Cycle Summary Panel: key metrics from evaluation | P0 |
| PL-047 | Evaluation Rating Panel: A-F rating with trend arrow | P0 |
| PL-048 | Recommendations Panel: key points from evaluation.md | P1 |
| PL-049 | Artifact Preview Panel: view plan.md, research.md, analysis.md | P1 |
| PL-050 | Next Cycle Panel: countdown to next expected cycle | P1 |

### 3.7 System Panels

| ID | Feature | Priority |
|----|---------|----------|
| PL-051 | System Status Panel: API connections, latency, errors | P1 |
| PL-052 | Connection Health Panel: green/yellow/red status per endpoint | P1 |
| PL-053 | Error Log Panel: recent errors with timestamp and details | P1 |
| PL-054 | Event Stream Panel: real-time log of all system events | P2 |

---

## 4. Real-Time Data Streaming

High-frequency data delivery with proper handling.

### 4.1 WebSocket Integration

| ID | Feature | Priority |
|----|---------|----------|
| RT-001 | WebSocket connection to Execution Service for positions | P0 |
| RT-002 | WebSocket connection to Agent Runner for state updates | P0 |
| RT-003 | WebSocket connection for market data (prices) | P0 |
| RT-004 | Automatic reconnection with exponential backoff | P0 |
| RT-005 | Connection status indicator in header | P0 |
| RT-006 | Message queue during disconnection, replay on reconnect | P1 |

### 4.2 Data Tick Handling

| ID | Feature | Priority |
|----|---------|----------|
| RT-007 | Price tick: update display within 100ms of receipt | P0 |
| RT-008 | Number flip animation for price changes | P0 |
| RT-009 | Color flash on price increase (brief green) | P0 |
| RT-010 | Color flash on price decrease (brief red) | P0 |
| RT-011 | Rate limiting: max 10 updates/second per panel | P1 |
| RT-012 | Batch updates: combine multiple ticks into single render | P1 |

### 4.3 Polling Fallback

| ID | Feature | Priority |
|----|---------|----------|
| RT-013 | REST polling fallback when WebSocket unavailable | P0 |
| RT-014 | Configurable poll intervals per data type | P1 |
| RT-015 | Smart polling: increase frequency during active agent | P2 |

### 4.4 Data Normalization

| ID | Feature | Priority |
|----|---------|----------|
| RT-016 | Normalize price data format across sources | P0 |
| RT-017 | Handle missing/null data gracefully | P0 |
| RT-018 | Timestamp normalization to local time | P0 |

---

## 5. Interaction System

Keyboard-driven workflow for power users.

### 5.1 Keyboard Shortcuts

| ID | Feature | Priority |
|----|---------|----------|
| IS-001 | Global: Cmd+K - Open command palette | P0 |
| IS-002 | Global: Cmd+1-8 - Switch to tab 1-8 | P0 |
| IS-003 | Global: Cmd+W - Close current tab | P0 |
| IS-004 | Global: Cmd+T - New tab | P0 |
| IS-005 | Global: ESC - Close command palette / restore maximized panel | P0 |
| IS-006 | Panel: Double-click header - Maximize panel | P0 |
| IS-007 | Panel: Cmd+Arrow keys - Navigate between panels | P1 |
| IS-008 | Panel: Cmd+Enter - Expand/collapse panel | P1 |
| IS-009 | Global: Cmd+S - Force refresh all data | P1 |
| IS-010 | Global: Cmd+, - Open settings | P2 |

### 5.2 Mouse Interactions

| ID | Feature | Priority |
|----|---------|----------|
| IS-011 | Panel drag: click and hold header to reposition | P0 |
| IS-012 | Panel resize: drag edges/corners | P0 |
| IS-013 | Scroll wheel on chart: zoom in/out | P0 |
| IS-014 | Scroll wheel + shift on chart: pan | P0 |
| IS-015 | Hover on price: show detailed bid/ask | P1 |
| IS-016 | Click tool call in list: open detail panel | P0 |
| IS-017 | Right-click panel header: context menu | P2 |

### 5.3 Drag and Drop

| ID | Feature | Priority |
|----|---------|----------|
| IS-018 | Drag panel from one tab to another | P1 |
| IS-019 | Drag panel to reorder within tab | P0 |
| IS-020 | Drop zone highlighting during drag | P0 |
| IS-021 | Snap-to-grid during drag operations | P0 |

---

## 6. Alert & Attention System

Drawing attention without interruption.

### 6.1 Panel Glow Effects

| ID | Feature | Priority |
|----|---------|----------|
| AS-001 | Panel border glow when related activity occurs | P0 |
| AS-002 | Agent Reasoning panel glows when agent is thinking | P0 |
| AS-003 | Tool Calls panel glows when tool is invoked | P0 |
| AS-004 | Portfolio panel glows when position hits stop/target | P0 |
| AS-005 | Configurable glow duration (default: 3 seconds) | P2 |
| AS-006 | Glow color intensity indicates urgency | P1 |

### 6.2 Status Indicators

| ID | Feature | Priority |
|----|---------|----------|
| AS-007 | Header status bar: connection status (green/yellow/red) | P0 |
| AS-008 | Header: current mode indicator (paper/live) | P0 |
| AS-009 | Header: countdown to strategy expiry | P0 |
| AS-010 | Header: time until next cycle | P0 |
| AS-011 | Per-panel: small dot indicator for live data | P1 |

### 6.3 Alert Conditions

| ID | Feature | Priority |
|----|---------|----------|
| AS-012 | Price alert: trigger when price crosses threshold | P1 |
| AS-013 | P&L alert: trigger at 80% of daily loss limit | P1 |
| AS-014 | Drawdown alert: trigger at max drawdown threshold | P1 |
| AS-015 | Strategy expiry alert: 1 hour warning | P1 |
| AS-016 | New strategy alert: flash when strategy.json updates | P1 |
| AS-017 | Agent state change alert: flash on state transition | P2 |
| AS-018 | Error alert: panel glow on new error | P1 |

### 6.4 Notification Patterns

| ID | Feature | Priority |
|----|---------|----------|
| AS-019 | No popups - alerts are panel-level only | P0 |
| AS-020 | Browser notification for critical alerts (optional, opt-in) | P2 |
| AS-021 | Alert sound toggle (optional, opt-in) | P3 |
| AS-022 | Alert history panel (recent alerts) | P2 |

---

## 7. Data & API Integration

Connecting to the Crypton backend services.

### 7.1 API Client

| ID | Feature | Priority |
|----|---------|----------|
| API-001 | Fetch current positions from Execution Service | P0 |
| API-002 | Fetch active strategy from Execution Service | P0 |
| API-003 | Fetch trade history from Execution Service | P0 |
| API-004 | Fetch agent state from Agent Runner | P0 |
| API-005 | Fetch market data (prices, indicators) | P0 |
| API-006 | Fetch evaluation.md content | P0 |
| API-007 | Fetch artifact content (plan.md, research.md, analysis.md) | P1 |
| API-008 | Retry logic with exponential backoff (3 attempts) | P0 |
| API-009 | Request timeout handling (10s default) | P0 |
| API-010 | Error response display in UI | P0 |

### 7.2 Caching Strategy

| ID | Feature | Priority |
|----|---------|----------|
| API-011 | Client-side cache for strategy and positions | P1 |
| API-012 | Cache invalidation on WebSocket update | P1 |
| API-013 | Stale-while-revalidate for non-critical data | P2 |
| API-014 | Cache size limit and eviction policy | P2 |

### 7.3 Environment Configuration

| ID | Feature | Priority |
|----|---------|----------|
| API-015 | Environment variable for API base URLs | P0 |
| API-016 | WebSocket endpoint configuration | P0 |
| API-017 | Feature flags for panel visibility | P2 |

---

## 8. Security & Deployment

Production readiness concerns.

### 8.1 Authentication

| ID | Feature | Priority |
|----|---------|----------|
| SEC-001 | Optional authentication gate (configurable) | P3 |
| SEC-002 | Read-only access by default | P3 |
| SEC-003 | Session timeout handling | P3 |
| SEC-004 | Secure credential storage | P3 |

### 8.2 Deployment

| ID | Feature | Priority |
|----|---------|----------|
| DEP-001 | Docker container for React SPA | P3 |
| DEP-002 | Nginx configuration for production serving | P3 |
| DEP-003 | Environment-based configuration (dev/staging/prod) | P3 |
| DEP-004 | Build optimization (code splitting, lazy loading) | P3 |
| DEP-005 | Health check endpoint | P3 |

---

## 9. Future Enhancements

Long-term vision beyond MVP.

| ID | Feature | Priority |
|----|---------|----------|
| FUT-001 | Mobile-optimized view (separate layout, not responsive) | P3 |
| FUT-002 | Multiple workspace profiles (save/load layouts) | P3 |
| FUT-003 | Custom panel creation (user-defined panels) | P3 |
| FUT-004 | Scriptable macros (record and replay command sequences) | P3 |
| FUT-005 | Team collaboration (share workspace snapshots) | P3 |
| FUT-006 | Dark/light theme toggle (default dark) | P3 |
| FUT-007 | Keyboard-only navigation mode | P3 |

---

## Design Reference: Layout Mockup

```
┌─────────────────────────────────────────────────────────────────────┐
│ [Main ×] [Analysis] [Debug ×] [+]                    [Cmd+K] [⚙] │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐ │
│  │ PORTFOLIO       │  │ STRATEGY        │  │ AGENT STATE     │ │
│  │ $127,432.00    │  │ ● PAPER 4:32    │  │ ● ANALYZING     │ │
│  │ ▲ +2.4%       │  │ MODERATE        │  │ ┌─────────────┐ │ │
│  │                 │  │ Risk: 2.3%      │  │ │ ● RUNNING   │ │ │
│  │ BTC  $45,200   │  │ Entries: 3      │  │ └─────────────┘ │ │
│  │ ETH  $2,890    │  │                 │  │ Tool: thinking │ │
│  │ SOL  $98.40    │  │                 │  │ Tokens: 2,341  │ │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘ │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────────┐│
│  │ MARKET: BTC/USD                              [≡] [□] [×]     ││
│  │ $45,200.00  ▲ +1.2%                    RSI: 62  MACD: ▲     ││
│  │ ┌─────────────────────────────────────────────────────────┐   ││
│  │ │        ╭─────────────────────────────────────────────╮    ││
│  │ │       ╱                 PRICE CHART                    │   ││
│  │ │──────╯                                                  │   ││
│  │ └─────────────────────────────────────────────────────────┘   ││
│  └─────────────────────────────────────────────────────────────────┘│
│                                                                     │
│  ┌──────────────────────────────┐  ┌────────────────────────────┐│
│  │ TOOL CALLS                   │  │ REASONING TRACE            ││
│  │ → search("BTC ETF...")       │  │ Evaluating RSI divergence..││
│  │ ← 12 results                 │  │ Price rejecting 45.5k..    ││
│  │ → fetch(coindesk.com/...)    │  │ Momentum shifting bullish ││
│  │ ← article content...        │  │ Volume spike on 4h...      ││
│  └──────────────────────────────┘  └────────────────────────────┘│
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

## Key Conventions

- **Panel header**: Always 28px, shows title + key metric + window controls
- **Collapsed panel**: 28px bar showing icon + title + one key number
- **Glow effect**: 2px border, accent color at 30% opacity, pulse 1s
- **Price tick**: Number transitions over 150ms, flash color 200ms
- **No scrollbars**: All content fits; overflow = collapse to summary
- **Command palette**: Centered overlay, 500px width, max 10 visible results

# Monitoring Dashboard

Real-time monitoring dashboard for Crypton - an autonomous cryptocurrency portfolio management system.

## Design Philosophy

**"All signal; no noise"** - F35 Cockpit meets Trading Terminal.

- Zero scrolling - everything fits in the viewport
- VS Code-style tabbed interface with draggable panels
- Command palette-driven workflow (Cmd+K)
- Maximum information density with intelligent collapse
- Real-time dynamism - live data streaming
- Dark theme with crisp lines

## Quick Start

### Prerequisites

- Node.js 18+
- .NET 10 SDK
- npm or yarn

### Development

1. **Start Backend API:**
   ```bash
   cd src/monitoring_dashboard/backend/MonitoringDashboard.Api
   dotnet run
   ```
   Backend runs on http://localhost:5000

2. **Start Frontend:**
   ```bash
   cd src/monitoring_dashboard/frontend
   npm install
   npm run dev
   ```
   Frontend runs on http://localhost:3000

### VS Code Debugging

Open the `src/monitoring_dashboard` folder in VS Code and use:

- **F5** or select "Backend API" from debug dropdown to run backend
- **"Frontend (Chrome)"** or **"Frontend (Firefox)"** to debug frontend
- **"Full Stack"** compound to debug both simultaneously

### Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Cmd+K | Open command palette |
| Cmd+1-8 | Switch to tab |
| Cmd+T | New tab |
| Cmd+W | Close tab |
| ESC | Close command palette / restore panel |
| Double-click panel header | Maximize panel |

## Project Structure

```
src/monitoring_dashboard/
├── backend/
│   └── MonitoringDashboard.Api/
│       ├── Controllers/      # REST API endpoints
│       ├── Hubs/             # SignalR hubs
│       ├── Models/           # Data models
│       └── Program.cs        # Entry point
│
├── frontend/
│   └── src/
│       ├── components/
│       │   ├── layout/      # TabBar, StatusBar, CommandPalette
│       │   └── panels/      # Dashboard panels
│       ├── services/         # API and SignalR clients
│       ├── store/            # Zustand state management
│       ├── styles/           # Global CSS (F35 theme)
│       └── types/            # TypeScript types
│
└── .vscode/
    ├── launch.json           # Debug configurations
    └── tasks.json           # Build tasks
```

## Features

### Portfolio & Positions
- Portfolio summary with total value and 24h change
- Holdings list with allocation percentages
- Open positions with entry/exit prices, P&L, stops
- Trade history

### Strategy
- Active strategy overview (mode, posture, validity)
- Position rules visualization
- Strategy history

### Market
- Live price tickers with bid/ask
- Technical indicators (RSI, MACD, Bollinger Bands)
- Macro signals

### Agent Transparency
- Agent state and progress
- Reasoning trace (live thinking stream)
- Tool calls with inputs/outputs
- Loop timeline

### Performance
- Current cycle P&L and metrics
- Win rate, max drawdown
- Cycle history
- Evaluation ratings

## API Endpoints

- `GET /api/portfolio/summary` - Portfolio summary
- `GET /api/portfolio/holdings` - Holdings list
- `GET /api/portfolio/positions` - Open positions
- `GET /api/portfolio/trades` - Trade history
- `GET /api/strategy/current` - Active strategy
- `GET /api/market/prices` - Live prices
- `GET /api/market/indicators` - Technical indicators
- `GET /api/agent/state` - Agent state
- `GET /api/agent/loop` - Loop status
- `GET /api/agent/toolcalls` - Tool call history
- `GET /api/performance/cycle` - Current cycle metrics
- `GET /api/performance/evaluation` - Latest evaluation

## SignalR Events

Connect to `/hubs/dashboard` for real-time updates:

- `PortfolioUpdated` - Portfolio data changed
- `PriceUpdated` - Price ticker update
- `AgentStateChanged` - Agent state transition
- `ToolCallStarted` / `ToolCallCompleted` - Tool execution
- `ReasoningUpdated` - New reasoning step
- `StrategyUpdated` - Strategy changed
- `CycleCompleted` - Cycle finished

## Tech Stack

- **Backend:** C# .NET 10, ASP.NET Core, SignalR
- **Frontend:** React 18, TypeScript, Vite
- **State:** Zustand
- **Real-time:** SignalR
- **Charts:** Recharts
- **Styling:** CSS Variables (custom F35 theme)

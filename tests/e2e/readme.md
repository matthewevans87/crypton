# Crypton E2E Test Harness

Playwright-based end-to-end tests covering the full stack: four .NET backend services, a React dashboard, and the real-time SignalR connections between them.

---

## Quick start

```bash
# 1. Install dependencies (one-time)
cd tests/e2e
npm install
npx playwright install chromium

# 2. Start the backend services and React dev server (separate terminal)
./scripts/start-test-services.sh

# 3. Run tests
npm test
```

The test runner will reuse the already-running Vite dev server and services. A fresh `npm test` from cold takes ~2 min; with services already up it typically completes in under 90 seconds.

---

## Service topology

| Service                | Port | Project                               |
| ---------------------- | ---- | ------------------------------------- |
| Monitoring Dashboard   | 5001 | `src/Crypton.Api.MonitoringDashboard` |
| Market Data            | 5002 | `src/Crypton.Api.MarketData`          |
| Agent Runner           | 5003 | `src/Crypton.Api.AgentRunner`         |
| Execution Service      | 5004 | `src/Crypton.Api.ExecutionService`    |
| React dashboard (Vite) | 3000 | `src/Crypton.Web.Dashboard`           |

The Vite dev server proxies all `/api` requests through to the Monitoring Dashboard (port 5001), which in turn aggregates data from the other three services.

---

## Two operating modes

### Watch mode (default, recommended for development)

Start services yourself — the test harness just health-polls them and runs immediately.

```bash
# Terminal 1: start backend services
./scripts/start-test-services.sh        # uses dotnet watch run, restarts on code changes

# Terminal 2: run tests (can be repeated without restarting services)
cd tests/e2e && npm test
```

`TEST_MANAGED_SERVICES` is `false` by default. Global setup polls all four health endpoints with a 60 s timeout and fails with a helpful message if any service isn't up.

### CI / managed mode

Set `TEST_MANAGED_SERVICES=true` in `.env.test` (or as an environment variable) and the global setup will spawn `dotnet run` for each service, poll until healthy, then tear everything down after the suite completes.

```bash
TEST_MANAGED_SERVICES=true npm test
```

In CI managed mode, `MARKETDATA__EXCHANGE__USEMOCK=true` is automatically injected into the Market Data process so tests get deterministic prices.

---

## Mock exchange adapter

`Crypton.Api.MarketData` ships a `MockExchangeAdapter` that returns fixed deterministic prices:

| Pair    | Price   |
| ------- | ------- |
| BTC/USD | $50,000 |
| ETH/USD | $3,000  |
| SOL/USD | $120    |

Activate it by setting the env var before starting the service:

```bash
MARKETDATA__EXCHANGE__USEMOCK=true dotnet run --project src/Crypton.Api.MarketData/...
```

Or via the start script, which accepts a `--mock` flag (see `scripts/start-test-services.sh`).

Without mock mode the service connects to live Kraken WebSocket data. The `market-data.spec.ts` suite detects which mode is active at runtime and skips the exact-price assertions when running against live data.

---

## Authentication

AgentRunner and ExecutionService enforce API key authentication via the `X-Api-Key` header.

The test key (`testkey`) is the same value configured in `~/.config/crypton/.env` for local dev:

```
# ~/.config/crypton/.env
AGENT_RUNNER_API_KEY=testkey
```

`.env.test` stores `TEST_API_KEY=testkey`. All `ApiClient` REST calls and Playwright's `extraHTTPHeaders` config inject this automatically. If you change your local API key, update `.env.test` to match.

**Important:** if `AgentRunnerConfig.Api.ApiKey` is empty, the `[ApiKey]` filter allows all requests. Tests still pass, but this means the API key isn't being validated.

---

## Agent state machine

The Agent Runner has a `LoopState` enum. Only certain transitions are valid:

```
Idle → Plan → Research → Analyze → Synthesize → WaitingForNextCycle
                                                       ↓
                                                    Evaluate → Plan (next cycle)

Any running state → Paused → (resume) → Plan / Evaluate / Idle
Any running state → Failed
```

**Pause is only available from running states** (`WaitingForNextCycle`, `Plan`, `Research`, `Analyze`, `Synthesize`). When the agent is in `Failed` or `Idle`, calling `/api/override/pause` succeeds at the HTTP level but does not change `currentState`. Tests that need to exercise pause/resume use `test.skip` when the initial state is not pausable:

```typescript
const pausableStates = ['WaitingForNextCycle', 'Plan', 'Research', 'Analyze', 'Synthesize'];
test.skip(!pausableStates.includes(initial.currentState), `Agent is in "${initial.currentState}"`);
```

Check the current state by polling `/api/status` on port 5003 (requires `X-Api-Key`):

```bash
curl -H "X-Api-Key: testkey" http://localhost:5003/api/status
# → { "currentState": "WaitingForNextCycle", "isPaused": false, ... }
```

---

## API endpoints used by tests

### Agent Runner (`:5003`)

| Method | Path                        | Auth        | Notes                                                     |
| ------ | --------------------------- | ----------- | --------------------------------------------------------- |
| GET    | `/api/status`               | `X-Api-Key` | Returns `{ currentState, isPaused, currentCycleId, ... }` |
| POST   | `/api/override/pause`       | `X-Api-Key` | Body: `{ "reason": "..." }` (optional)                    |
| POST   | `/api/override/resume`      | `X-Api-Key` |                                                           |
| POST   | `/api/override/force-cycle` | `X-Api-Key` | Forces an immediate cycle regardless of schedule          |
| POST   | `/api/override/abort`       | `X-Api-Key` | Aborts the current cycle                                  |
| GET    | `/health/live`              | none        |                                                           |

**`isPaused` vs `currentState`:** The `isPaused` field on the status response is read from the current cycle context and reflects whether `CycleContext.IsPaused` was set — it does not always match `currentState === "Paused"`. Use `currentState` for state machine assertions; `isPaused` is supplementary metadata.

### Market Data (`:5002`)

| Method | Path                                          | Notes                        |
| ------ | --------------------------------------------- | ---------------------------- |
| GET    | `/api/prices/{symbol}`                        | e.g. `/api/prices/BTC%2FUSD` |
| GET    | `/api/prices?symbols=BTC/USD&symbols=ETH/USD` | Batch                        |
| GET    | `/health/live`                                |                              |

### Monitoring Dashboard (`:5001`)

| Method | Path                     | Notes                                                                  |
| ------ | ------------------------ | ---------------------------------------------------------------------- |
| GET    | `/api/system/status`     | Returns `{ services: [{ name, status, detail, checkedAt, metrics }] }` |
| GET    | `/api/portfolio/summary` | Returns `{ totalValue, unrealizedPnL, availableCapital }`              |
| GET    | `/health/live`           |                                                                        |

**System status values:** `status` on each service entry is `"online"`, `"degraded"`, or `"offline"` — not just a boolean. A service is `degraded` when it is reachable but reporting internal issues (e.g. agent in Failed state, exchange WebSocket disconnected, stale price data).

### Execution Service (`:5004`)

| Method | Path           | Notes                            |
| ------ | -------------- | -------------------------------- |
| GET    | `/health/live` | Returns HTTP 200 with empty body |

---

## UI structure and `data-testid` map

The dashboard is a single-page React app. The layout is:

```
App
├── StatusBar            data-testid="status-bar"
│   ├── SignalR dot      data-testid="status-connected" | "status-connecting" | "status-disconnected"
│   └── ServiceChip[]    data-testid="service-chip-{name.toLowerCase()}"   data-status="online|degraded|offline"
├── TabBar
│   └── Tab button[]     data-testid="tab-{id}"   data-active="true|false"
│       Tabs: main · analysis · diagnostics (plus any user-created tabs)
└── PanelGrid
    └── Panel[]          (type-specific testids, see below)
```

### Default tab layout

| Tab id        | Panels                                                                                                       |
| ------------- | ------------------------------------------------------------------------------------------------------------ |
| `main`        | portfolio-summary, strategy-overview, **agent-state**, cycle-performance, price-ticker-btc, price-ticker-eth |
| `analysis`    | loop-state, reasoning-trace, tool-calls, technical-indicators, open-positions, holdings                      |
| `diagnostics` | **system-status**, system-diagnostics, connection-health, error-log                                          |

### Panel `data-testid` inventory

| Panel type        | `data-testid`                         | Notes                                                                                              |
| ----------------- | ------------------------------------- | -------------------------------------------------------------------------------------------------- |
| AgentStatePanel   | `panel-agent-state`                   | Present on main tab. Renders with testid in *both* loading and loaded states.                      |
|                   | `agent-current-state`                 | Text content is the `LoopState` string (e.g. `WaitingForNextCycle`). Only present in loaded state. |
| SystemStatusPanel | `panel-system-status`                 | Present on diagnostics tab. Renders with testid in *both* loading and loaded states.               |
|                   | `system-status-overall`               | `data-status="online\|degraded\|offline"`. Only present once `systemHealth` data has loaded.       |
|                   | `service-status-{name.toLowerCase()}` | Per-service row. `data-status="online\|degraded\|offline"`.                                        |
| PriceTickerPanel  | `panel-price-ticker`                  | `data-asset="BTC/USD"` (or whichever pair the panel is configured for).                            |
|                   | `price-value`                         | Formatted price string (e.g. `$68,249.00`).                                                        |
| StatusBar         | `status-bar`                          | Always present.                                                                                    |

### Navigating to non-default tabs

Panels on tabs other than `main` require a tab click before waiting for the panel. Use the `data-testid` on the tab div:

```typescript
await page.locator('[data-testid="tab-diagnostics"]').click();
await page.locator('[data-testid="panel-system-status"]').waitFor({ state: 'visible', timeout: 10_000 });
```

### SignalR connection timing

The `dashboardPage` fixture navigates to `/` and waits for `[data-testid="status-connected"]` before yielding the page to the test (15 s timeout). This guarantees SignalR is live before any panel assertions. Panel `data-testid` wrappers render immediately (even in loading state), but actual data-driven content may arrive a few seconds later — use `waitFor` with a generous timeout (10–20 s) rather than immediate `expect`.

---

## Fixture and helper reference

### `fixtures/base.ts` — custom fixtures

```typescript
import { test, expect } from '../fixtures/base';

test('example', async ({ api, dashboardPage }) => { ... });
```

- **`api`** — `ApiClient` pre-configured with service URLs and the test API key. Use for direct REST assertions and test setup.
- **`dashboardPage`** — a `Page` that has already navigated to `/` and verified SignalR connected. Use for UI assertions.

### `helpers/api.ts` — `ApiClient`

Key methods:

```typescript
api.getAgentStatus()          // GET /api/status on AgentRunner
api.pauseAgent()              // POST /api/override/pause
api.resumeAgent()             // POST /api/override/resume
api.forceCycle()              // POST /api/override/force-cycle
api.getPrice('BTC/USD')       // GET /api/prices/BTC%2FUSD on MarketData
api.getPortfolioSummary()     // GET /api/portfolio/summary on MonitoringDashboard
api.getSystemStatus()         // GET /api/system/status on MonitoringDashboard
api.allServicesHealthy()      // polls /health/live on all four services
api.isServiceHealthy(baseUrl) // polls /health/live on one service
```

The `ApiClient` constructor is also exported for use in `beforeAll` (where the `api` fixture isn't available):

```typescript
import { ApiClient } from '../helpers/api';

test.beforeAll(async () => {
  const client = new ApiClient({
    agentRunnerUrl: process.env.AGENT_RUNNER_URL ?? 'http://localhost:5003',
    // ... other URLs
    apiKey: process.env.TEST_API_KEY ?? 'testkey',
  });
  const status = await client.getAgentStatus();
});
```

---

## Writing new tests

### Checklist

1. Import from `../fixtures/base`, not directly from `@playwright/test`.
2. Use `api` for any setup that would be fragile through the UI (seeding state, reading a value to branch on).
3. Use `dashboardPage` (not `page`) for UI tests — it guarantees SignalR is connected before the test body runs.
4. Account for async data loading: panels render a loading shell immediately, data arrives after. Use `waitFor` / `toBeVisible({ timeout: 20_000 })` for assertions on data-driven content.
5. When a test only makes sense in a specific state (e.g. agent must be cycling to test pause), use `test.skip` with a clear message rather than letting the test flake.
6. When asserting on system/service status, accept `online|degraded|offline` — don't hard-code `online`. Live services can be degraded without being blocked.

### Example: UI test

```typescript
import { test, expect } from '../fixtures/base';

test('agent panel shows a state', async ({ dashboardPage: page }) => {
  // panel-agent-state is on the main tab (default)
  await page.locator('[data-testid="panel-agent-state"]').waitFor({ state: 'visible' });

  // agent-current-state only appears once data has loaded
  const label = page.locator('[data-testid="agent-current-state"]');
  await expect(label).toBeVisible({ timeout: 20_000 });
  expect((await label.textContent())?.trim().length).toBeGreaterThan(0);
});
```

### Example: API + UI test

```typescript
test('resumed agent is no longer paused', async ({ api, dashboardPage: page }) => {
  const initial = await api.getAgentStatus();
  test.skip(initial.currentState !== 'Paused', 'Agent must already be paused');

  await api.resumeAgent();

  // verify via API
  await expect.poll(() => api.getAgentStatus().then(s => s.currentState), { timeout: 10_000 })
    .not.toBe('Paused');

  // verify in UI
  const label = page.locator('[data-testid="agent-current-state"]');
  await expect(label).not.toHaveText('Paused', { timeout: 15_000 });
});
```

---

## Configuration reference

### `.env.test`

```dotenv
TEST_API_KEY=testkey                          # must match AGENTRUNNER__API__APIKEY / EXECUTIONSERVICE__API__APIKEY
MARKET_DATA_URL=http://localhost:5002
EXECUTION_SERVICE_URL=http://localhost:5004
AGENT_RUNNER_URL=http://localhost:5003
MONITORING_DASHBOARD_URL=http://localhost:5001
DASHBOARD_UI_URL=http://localhost:3000
TEST_MANAGED_SERVICES=false                   # set true to have Playwright manage service processes
```

### Key `playwright.config.ts` settings

- `workers: 1`, `fullyParallel: false` — tests run serially; all share the same live service state.
- `reuseExistingServer: true` — Playwright won't restart Vite if something is already listening on port 3000.
- `retries: 1` in CI — allows one automatic retry for genuinely flaky network timing.
- Screenshots on failure, HTML report in `playwright-report/`.

---

## Running a subset of tests

```bash
# single file
npx playwright test tests/agent-control.spec.ts

# single test by name
npx playwright test -g "agent is reachable"

# headed browser (useful for debugging)
npm run test:headed

# interactive UI mode
npm run test:ui
```

View the last HTML report:

```bash
npm run test:report
```

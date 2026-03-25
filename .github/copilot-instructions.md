# Crypton — Copilot Instructions

## Overview of Services

Crypton is an automated cryptocurrency portfolio management system consisting of:

- **Agent Runner** (`src/Crypton.Api.AgentRunner`) — Orchestrates the Learning Loop state machine (Plan → Research → Analyze → Synthesize → Evaluate → repeat). Manages agent lifecycle, context injection, artifact management, and mailbox routing. Does **no** AI reasoning itself — it is a deterministic program.
- **Execution Service** (`src/Crypton.Api.ExecutionService`) — Deterministic trading engine. Picks up `strategy.json`, executes it against Kraken via REST/WS, manages positions, enforces risk rules, and streams telemetry. Supports paper and live trading modes.
- **Market Data Service** (`src/Crypton.Api.MarketData`) — Kraken market data adapter. Streams live prices, order book, and computes technical indicators. Exposes REST + SignalR.
- **Monitoring Dashboard Service** (`src/Crypton.Api.MonitoringDashboard`) — BFF for the Svelte web frontend. Aggregates telemetry from all services.
- **Dashboard UI** (`src/Crypton.Web.Dashboard`) — A web interface for a human operator. Surfaces system state and Kraken exchange data. Enables the operator to send commands to the Crypton system.
- **Bird Server** (`src/Crypton.Api.Bird`) — Thin Node.js HTTP wrapper around the [`@steipete/bird`](https://github.com/steipete/bird) CLI. Provides the Agent Runner's `bird` tool with access to X/Twitter data. Listens on `localhost:11435`.
  - `POST /execute { args: string }` — runs `bird <args>` and returns `{ stdout, stderr, exitCode }`.
  - `GET /health` — returns `200 OK` if the server is running **and** `BIRD_AUTH_TOKEN`/`BIRD_CT0` env vars are set; returns `503` if credentials are missing, which causes the Agent Runner startup validator to abort the cycle.
  - Managed as a systemd service via `src/Crypton.Api.Bird/Makefile`. Key targets: `make extract-tokens` (pulls OAuth tokens from the local browser), `make install` (installs the systemd unit), `make start/stop/restart/status`.
  - Auth tokens (`BIRD_AUTH_TOKEN`, `BIRD_CT0`) must be present in `~/.config/crypton/.env`. Run `make extract-tokens` to refresh them when X/Twitter sessions expire.

Shared configuration helpers live in `Crypton.Configuration`.

### System Contract

`strategy.json` is the formal contract between the Learning Loop and the Execution Service. It is produced by the Synthesis Agent and consumed deterministically by the Execution Service. Entry/exit conditions use a lightweight **DSL** evaluated on every tick (`price(BTC/USD) < 65000`, `AND(rsi(14, BTC/USD) < 35, ...)`, `crosses_below`, etc.). All JSON field names are **snake_case** matching `[JsonPropertyName]` bindings exactly.

### Inter-Service Communication

- All services expose a **SignalR hub** and a **REST API**.
- Hub paths follow the pattern `/hubs/<service>` (e.g. `/hubs/execution`, `/hubs/market-data`).
- Agent Runner writes loop artifacts (plan.md, research.md, strategy.json, etc.) to the `artifacts/` directory on disk.
- Mailboxes are files in `mailboxes/`; max 5 messages per inbox, 1–2 sentences each.

### Agent Telemetry Pipeline

Agent Runner telemetry reaches the browser through a **four-stage pipeline**. Understand this fully before touching anything in `AgentInvoker`, `AgentRunnerHubBroadcaster`, `MonitoringDashboard/Program.cs`, or `signalr.ts`.

```
AgentInvoker (AgentRunner)
  → fires string events: "[tool] → toolName({json})", "[tool] ← toolName OK (1.2s): ...", tokens
AgentRunnerService
  → wraps as AgentEventArgs { EventMessage, StepName }
  → raises AgentEventReceived
AgentRunnerHubBroadcaster
  → parses the string events by prefix
  → emits to SignalR groups: ToolCallStarted, ToolCallCompleted, TokenReceived, StepStarted, StepCompleted
AgentRunnerClient (MonitoringDashboard)
  → subscribes to hub methods and raises typed C# events: OnToolCallStarted, OnToolCallCompleted, OnTokenReceived, OnStepStarted, OnStepCompleted
MonitoringDashboard/Program.cs
  → all five OnXxx handlers MUST be wired; missing any = silent data loss
  → maps payloads to DashboardToolCall / DashboardReasoningStep / DashboardAgentState
  → emits to DashboardHub → Browser
Browser signalr.ts / App.tsx
  → receives ToolCallStarted, ToolCallCompleted, ReasoningUpdated, AgentStateChanged
```

**Critical invariant — ToolCallStarted / ToolCallCompleted data split:**
- `ToolCallStarted` carries `input` (the JSON-serialised parameters) and `output = null / isCompleted = false`.
- `ToolCallCompleted` carries `output` (result or error text) and `input = ""` — **it does not re-send the request parameters**.
- The browser `onToolCallUpdated` handler **must merge on `id`**, preserving `input` from the Started event when the Completed event arrives. Replacing the record wholesale loses the request.

## Design Philosophy

We want a system that is both _elegant_ (straightforward to understand, well-designed, efficient) and _correct_ (works as expected, robust and fault-tolerant).

Apply the following philosophy when planning and building:

1. **Challenge every requirement before touching code** — Question the requirement's validity before acting on it, especially if it came from a seemingly authoritative source.
2. **Delete before you build** — Actively look for code, services, endpoints, configs, or processes that can be removed entirely. If fewer than 10% of deleted things need to be added back, not enough was removed.
3. **Simplify and optimize only what survives deletion** — Refactor, clean up, or performance-tune only the parts confirmed necessary. Never optimize something that should be deleted.
4. **Speed up cycle time only after pruning** — Parallelize builds, reduce service startup time, and streamline CI only after the scope of what needs to exist is settled.
5. **Automate last** — Code generation, scaffolding, deployment pipelines, and agent-driven workflows should be built after the process is stable and minimal — not as a substitute for doing the prior four steps first.

## Vision

This tool is made for and used by developers. Exposing technical details is a feature, not a bug. The Monitoring Dashboard is a control plane, not just a display.

## Tech Stack

- **Language / Runtime:** C# on .NET 10. All projects target `net10.0`.
- **Web framework:** ASP.NET Core 10 (`Microsoft.NET.Sdk.Web`).
- **Real-time streaming:** SignalR (`Microsoft.AspNetCore.SignalR`).
- **Metrics:** Prometheus (`prometheus-net.AspNetCore`).
- **API docs:** Scalar (`Scalar.AspNetCore`).
- **Logging:** Serilog with structured, file-based event logging.
- **Configuration:** `IConfiguration` with `appsettings.json` + env vars. Secrets via `~/.config/crypton/.env` or `--env-file`. The `__` convention maps env vars to config hierarchy (`AGENTRUNNER__TOOLS__BRAVESEARCH__APIKEY`).
- **Tests:** xUnit, FluentAssertions, NSubstitute.
- **Frontend:** Svelte / Vite (in `Crypton.Web.Dashboard`).

## Coding Standards

### C# Conventions
- **Nullable reference types** and **implicit usings** are enabled in all projects.
- Services are registered via DI (`IServiceCollection`) in `Program.cs`; use extension methods for logical groupings.
- Configuration classes follow the pattern: one `Config` class per service (e.g. `AgentRunnerConfig`), bound via `IConfiguration.Get<T>()`. Throw `InvalidOperationException` on null bind.
- CLI mode and Service mode are separated at `Program.cs` entry point; CLI shares the same service layer — never duplicate business logic.
- Use `IHostedService` for background workers; do **not** use fire-and-forget `Task.Run`.
- `async`/`await` throughout; no `.Result` or `.Wait()`.
- Prefer `record` types for DTOs and immutable data. Use `class` for stateful services.
- All `HttpClient` instances are registered via `IHttpClientFactory`.
- All REST API responses use **camelCase**: configure `JsonNamingPolicy.CamelCase` on `AddControllers().AddJsonOptions(...)` in every service's `Program.cs`. Never use snake_case in controller anonymous object member names or `[JsonPropertyName]` attributes on HTTP-facing models. Snake_case is reserved for disk-persistence serializers (e.g. `PositionRegistry.JsonOpts`).

### Naming
- Namespaces mirror folder structure under each project root.
- Controllers: `<Domain>Controller.cs` in `Api/` or `Controllers/`.
- Hub classes: `<Domain>Hub.cs` in `Hubs/`.
- Hosted services: `<Name>Service.cs`.
- Configuration classes: `<ServiceName>Config.cs` in `Configuration/`.

### Testing
- **100% line and branch coverage is required for all non-infrastructure code.**
- Test project mirrors the source project's folder structure.
- Use `NSubstitute` for mocking interfaces. Use `FluentAssertions` for assertions (`result.Should().Be(...)`).
- Integration tests that need a live host inherit from a shared `TestServiceHost` where one exists.
- Test class name: `<SubjectClass>Tests`. Test method name: `<Method>_<Scenario>_<ExpectedOutcome>`.

### Security
- API keys are never hardcoded or committed. Always read from environment variables or `.env`.
- Validate all API key inputs at service startup; fail fast with a clear error.
- All user-facing inputs (REST request bodies) are validated with data annotations or FluentValidation before use.
- Strategy execution: paper mode is the default; switching to live requires an explicit human operator action.

## Key Reference Docs

- `docs/critical_path.md` — MVP status (✅/⚠️/❌). Check this before implementing anything.
- `docs/crypton.md` — Full system spec: agent definitions, strategy DSL, schema, mailbox protocol.
- `docs/<service>/feature_backlog.md` — Prioritised feature lists (P0–P3) per service.
- `src/<Service>/IMPLEMENTATION.md` — Per-service implementation notes where present.


## Development Workflow

1. **Check `docs/critical_path.md`** to identify what is incomplete (⚠️ or ❌) in the relevant goal.
2. **Check the relevant `docs/<service>/feature_backlog.md`** for priority and requirements.
3. **Read the existing code** in the target service before writing anything. Understand the surrounding patterns.
4. **Write the implementation** following the coding standards above.
5. **Write or update tests** to maintain 100% coverage. Run the test task for the affected project.
6. **Build** using the `build: <Service>` task to confirm no regressions before finishing.

## Common Patterns

### Adding a new REST endpoint
- Add route to the appropriate `*Controller.cs` in `Api/` or `Controllers/`.
- Register any new services in `Program.cs` via an extension method.
- Expose the endpoint in Scalar by ensuring it is reachable via the OpenAPI schema.
- If the endpoint proxies to another service, add the route in the Monitoring Dashboard's `AgentController` or equivalent.

### Adding a new SignalR event stream
- Define the method name as a constant in the Hub class.
- Emit from the relevant background service using `IHubContext<THub>`.
- Document the event in the hub's XML summary comment.

### Adding a new configuration value
- Add the property to the relevant `*Config` class.
- Document the env var override path in a comment (e.g. `// AGENTRUNNER__CYCLE__SCHEDULEINTERVALMINUTES`).
- Add a default value in `appsettings.json`.

### Modifying the strategy DSL
- The DSL evaluator lives in `Crypton.Api.ExecutionService/Strategy/`.
- The grammar and all supported functions are documented in `docs/crypton.md`.
- Any new DSL function must be added to `strategy.schema.json` validation and covered by unit tests.

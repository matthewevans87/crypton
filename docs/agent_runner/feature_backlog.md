# Agent Runner Feature Backlog

## Overview

This backlog defines all features required to build the Agent Runner - the orchestration service that drives the Learning Loop for Crypton. The Agent Runner is a deterministic program (not an agent) that manages agent lifecycle, context injection, artifact management, and failure recovery.

---

## Priority Legend

| Priority | Description |
|----------|-------------|
| P0 | Critical - Must work for basic functionality |
| P1 | High - Operational stability and error handling |
| P2 | Medium | Enhanced features and observability |
| P3 | Low | Polish, testing, deployment |

---

## 1. Core Loop Orchestration

### 1.1 State Machine Engine

| ID | Feature | Priority |
|----|---------|----------|
| FR-001 | Implement loop state machine with states: `Idle`, `Plan`, `Research`, `Analyze`, `Synthesize`, `Execute`, `Evaluate`, `WaitingForNextCycle` | P0 |
| FR-002 | Persist current state to durable storage (SQLite/file) for crash recovery | P0 |
| FR-003 | Resume loop from persisted state on service restart | P0 |
| FR-004 | Implement state transition validation (enforce sequence: Plan→Research→Analyze→Synthesize→Execute→Evaluate→Plan) | P0 |

### 1.2 Cycle Interval Management

| ID | Feature | Priority |
|----|---------|----------|
| FR-005 | Configure minimum cycle duration (default: 1 hour) | P1 |
| FR-006 | Configure maximum cycle duration (default: 24 hours) | P1 |
| FR-007 | Implement forced cycle timeout that triggers Evaluation if exceeded | P1 |
| FR-008 | Configure cycle scheduling (e.g., "run every 6 hours" or "run at 00:00 UTC daily") | P3 |

### 1.3 Loop Controller Service

| ID | Feature | Priority |
|----|---------|----------|
| FR-009 | Host Agent Runner as a long-running service (Windows/Linux daemon) | P3 |
| FR-010 | Implement graceful shutdown (complete current step before exiting) | P3 |
| FR-011 | Implement hot-reload of configuration without restart | P3 |

---

## 2. Agent Lifecycle Management

### 2.1 Agent Invocation

| ID | Feature | Priority |
|----|---------|----------|
| FR-012 | Define agent invocation interface (input artifacts, output artifact path, timeout) | P0 |
| FR-013 | Spawn LLM agent process for each step (via external process or HTTP to LLM provider) | P0 |
| FR-014 | Support agent configuration per agent type (model, temperature, system prompt path) | P0 |
| FR-015 | Implement agent result capture (stdout, stderr, output file validation) | P0 |

### 2.2 Timeout Enforcement

| ID | Feature | Priority |
|----|---------|----------|
| FR-016 | Configure per-agent timeout (default: Plan=30min, Research=45min, Analyze=30min, Synthesize=15min) | P1 |
| FR-017 | Implement timeout handler: mark step as failed, apply recovery policy | P1 |
| FR-018 | Allow per-step timeout override in cycle configuration | P3 |

### 2.3 Output Artifact Validation

| ID | Feature | Priority |
|----|---------|----------|
| FR-019 | Validate `plan.md` exists and contains required sections (1-7) | P0 |
| FR-020 | Validate `research.md` exists and contains required sections | P0 |
| FR-021 | Validate `analysis.md` exists and contains required sections | P0 |
| FR-022 | Validate `strategy.json` exists and conforms to schema | P0 |
| FR-023 | Validate `evaluation.md` exists and contains required sections | P0 |
| FR-024 | Implement artifact schema validation with detailed error messages | P0 |

---

## 3. Mailbox Management System

### 3.1 Inter-Agent Message Delivery

| ID | Feature | Priority |
|----|---------|----------|
| FR-025 | Create mailbox storage mechanism (file-based: `mailbox.<agent>`) | P0 |
| FR-026 | Implement message deposit (append to agent's mailbox) | P0 |
| FR-027 | Implement message retrieval (read last N messages) | P0 |
| FR-028 | Support forward signals (agent → next agent) | P2 |
| FR-029 | Support backward feedback (agent → previous agent) | P2 |
| FR-030 | Support broadcast signals (Evaluation Agent → all agents) | P2 |

### 3.2 Mailbox Retention Policy

| ID | Feature | Priority |
|----|---------|----------|
| FR-031 | Configure maximum messages per mailbox (default: 5) | P2 |
| FR-032 | Implement automatic pruning of old messages | P2 |
| FR-033 | Implement message timestamp tracking | P2 |

### 3.3 Mailbox Access API

| ID | Feature | Priority |
|----|---------|----------|
| FR-034 | Expose REST API to read/write mailboxes programmatically | P2 |
| FR-035 | Implement mailbox inspection for debugging | P2 |

---

## 4. Artifact Management

### 4.1 Artifact Storage

| ID | Feature | Priority |
|----|---------|----------|
| FR-036 | Define directory structure: `cycle.<timestamp>/`, `plan/memory.md`, `research/memory.md`, `analysis/memory.md` | P0 |
| FR-037 | Implement cycle artifact versioning (timestamp-based directories) | P0 |
| FR-038 | Archive completed cycle artifacts to `history/cycles/` directory | P1 |

### 4.2 Artifact Access for Agents

| ID | Feature | Priority |
|----|---------|----------|
| FR-039 | Provide agent with paths to input artifacts before execution | P0 |
| FR-040 | Implement shared memory store accessible by all agents (for cross-cycle context) | P1 |

### 4.3 Memory Persistence

| ID | Feature | Priority |
|----|---------|----------|
| FR-041 | Implement `plan/memory.md` persistence layer (append, read, search) | P2 |
| FR-042 | Implement `research/memory.md` persistence layer | P2 |
| FR-043 | Implement `analysis/memory.md` persistence layer | P2 |
| FR-044 | Implement Evaluation Agent memory (past 3-7 evaluations for context) | P1 |

---

## 5. Failure and Recovery

### 5.1 Retry Policy Engine

| ID | Feature | Priority |
|----|---------|----------|
| FR-045 | Configure max retries per step (default: 3) | P1 |
| FR-046 | Implement exponential backoff between retries (configurable: default 5min, 15min, 45min) | P1 |
| FR-047 | Distinguish retryable vs. non-retryable failures (e.g., bad output = non-retryable, timeout = retryable) | P1 |

### 5.2 Escalation Handling

| ID | Feature | Priority |
|----|---------|----------|
| FR-048 | Log failure with full context (step, input artifacts, error, retry count) | P1 |
| FR-049 | Implement operator notification via REST API webhook (configurable endpoint) | P1 |
| FR-050 | Configurable escalation policy: `halt` (stop loop) or `skip` (use last known good artifact) | P1 |
| FR-051 | Implement fallback artifact detection (find most recent valid artifact for each step) | P1 |

### 5.3 Strategy Validity Monitoring

| ID | Feature | Priority |
|----|---------|----------|
| FR-052 | Track `strategy.json` validity window from Synthesis step | P1 |
| FR-053 | Flag expired strategy (no new strategy within validity window) | P1 |
| FR-054 | Signal Execution Service to enter safe mode when strategy expires | P1 |

### 5.4 Dead Man's Switch

| ID | Feature | Priority |
|----|---------|----------|
| FR-055 | Implement heartbeat mechanism (internal timer) | P3 |
| FR-056 | Trigger safe mode if Agent Runner stops sending heartbeat for N minutes | P3 |

---

## 6. Observability

### 6.1 Structured Event Logging

| ID | Feature | Priority |
|----|---------|----------|
| FR-057 | Define event schema: `timestamp`, `event_type`, `agent`, `step`, `cycle_id`, `details`, `level` | P2 |
| FR-058 | Log all state transitions (Entering Plan, Completed Research, etc.) | P2 |
| FR-059 | Log all agent invocations (start, complete, fail, timeout) | P2 |
| FR-060 | Log all retry attempts with backoff schedule | P2 |
| FR-061 | Log all mailbox message deliveries | P2 |

### 6.2 Status API

| ID | Feature | Priority |
|----|---------|----------|
| FR-062 | Expose GET `/api/status` returning: current_state, current_cycle_id, last_completed_step, next_scheduled_time | P0 |
| FR-063 | Expose GET `/api/cycles` returning list of recent cycles with outcomes | P2 |
| FR-064 | Expose GET `/api/cycles/{id}` returning full cycle details (artifacts, timing, events) | P2 |
| FR-065 | Expose GET `/api/errors` returning recent failures with context | P2 |

### 6.3 Metrics

| ID | Feature | Priority |
|----|---------|----------|
| FR-066 | Track cycle duration (total and per-step) | P2 |
| FR-067 | Track step success/failure rates | P2 |
| FR-068 | Track retry frequency per agent | P2 |
| FR-069 | Export metrics in Prometheus format | P2 |

### 6.4 Health Checks

| ID | Feature | Priority |
|----|---------|----------|
| FR-070 | Implement liveness endpoint (`/health/live`) | P1 |
| FR-071 | Implement readiness endpoint (`/health/ready` - checks DB connectivity, disk space) | P1 |

---

## 7. Configuration Management

### 7.1 Configuration System

| ID | Feature | Priority |
|----|---------|----------|
| FR-072 | Define YAML/JSON configuration schema | P3 |
| FR-073 | Support environment variable overrides | P3 |
| FR-074 | Validate configuration on startup | P3 |
| FR-075 | Hot-reload configuration via API or file watcher | P3 |

### 7.2 Agent Configuration

| ID | Feature | Priority |
|----|---------|----------|
| FR-076 | Configure LLM provider (OpenAI, Anthropic, local, etc.) per agent | P3 |
| FR-077 | Configure model, temperature, max_tokens per agent | P3 |
| FR-078 | Configure system prompt file path per agent | P3 |

---

## 8. Execution Service Integration

### 8.1 Strategy Handoff

| ID | Feature | Priority |
|----|---------|----------|
| FR-079 | Detect new `strategy.json` written by Synthesis Agent | P0 |
| FR-080 | Validate strategy.json schema before notifying Execution Service | P0 |
| FR-081 | Notify Execution Service via REST API when new strategy is ready | P0 |
| FR-082 | Track Execution Service status (acknowledged, executing, error) | P1 |

### 8.2 Execution Phase Management

| ID | Feature | Priority |
|----|---------|----------|
| FR-083 | Transition to `Execute` state after successful Synthesis | P0 |
| FR-084 | Monitor Execution Service for completion or failure | P1 |
| FR-085 | Trigger Evaluation after Execution phase completes (or after timeout) | P1 |

---

## 9. Testing Infrastructure

### 9.1 Unit Tests

| ID | Feature | Priority |
|----|---------|----------|
| FR-086 | 100% test coverage on state machine logic | P3 |
| FR-087 | Unit tests for mailbox operations | P3 |
| FR-088 | Unit tests for artifact validation | P3 |
| FR-089 | Unit tests for retry policy | P3 |

### 9.2 Integration Tests

| ID | Feature | Priority |
|----|---------|----------|
| FR-090 | End-to-end test of single cycle (mock LLM calls) | P3 |
| FR-091 | Test crash recovery (simulate failure mid-cycle, verify resume) | P3 |
| FR-092 | Test timeout handling | P3 |

### 9.3 Mock Agents

| ID | Feature | Priority |
|----|---------|----------|
| FR-093 | Create mock Plan Agent that outputs deterministic `plan.md` | P3 |
| FR-094 | Create mock Research/Analysis/Synthesis agents for testing | P3 |
| FR-095 | Create mock Execution Service for testing strategy handoff | P3 |

---

## 10. Deployment & Operations

### 10.1 Docker Containerization

| ID | Feature | Priority |
|----|---------|----------|
| FR-096 | Create Dockerfile for Agent Runner service | P3 |
| FR-097 | Create docker-compose.yml for local development stack | P3 |
| FR-098 | Configure volume mounts for artifact persistence | P3 |

### 10.2 Security

| ID | Feature | Priority |
|----|---------|----------|
| FR-099 | Implement API authentication (API key or JWT) | P3 |
| FR-100 | Encrypt sensitive configuration (LLM API keys) | P3 |
| FR-101 | Run Agent Runner as non-root user in container | P3 |

---

## 11. Tool Calling Infrastructure

### 11.1 Tool Registry

| ID | Feature | Priority |
|----|---------|----------|
| FR-102 | Define tool registry configuration (YAML/JSON) listing available tools, names, descriptions, parameter schemas | P0 |
| FR-103 | Implement tool discovery at startup - scan for registered tools and validate schemas | P0 |
| FR-104 | Provide tool registry API to query available tools (for agent introspection) | P0 |

### 11.2 Tool Schema Definitions

| ID | Feature | Priority |
|----|---------|----------|
| FR-105 | Define JSON Schema for `web_search`: `{ query: string, num_results?: number, recency?: string }` | P1 |
| FR-106 | Define JSON Schema for `web_fetch`: `{ url: string, format?: "markdown" | "text" }` | P1 |
| FR-107 | Define JSON Schema for `bird`: `{ query: string, mode: "timeline" | "search" | "user", limit?: number }` | P1 |
| FR-108 | Define JSON Schema for `current_position`: `{}` (no parameters) | P1 |
| FR-109 | Define JSON Schema for `technical_indicators`: `{ asset: string, timeframe: string, indicators?: string[] }` | P1 |

### 11.3 Tool Executor Engine

| ID | Feature | Priority |
|----|---------|----------|
| FR-110 | Implement `IToolExecutor` interface with `ExecuteAsync(ToolCall)` method | P0 |
| FR-111 | Implement tool call deserialization (parse tool name + parameters from agent output) | P0 |
| FR-112 | Implement tool result serialization (convert result to structured output for LLM) | P0 |
| FR-113 | Implement concurrent tool execution (allow multiple tools in single agent turn) | P0 |
| FR-114 | Implement tool timeout per tool type (default: 30s web operations, 10s data queries) | P0 |

### 11.4 Web Search Tool (Brave API)

| ID | Feature | Priority |
|----|---------|----------|
| FR-115 | Implement `BraveSearchTool` wrapping Brave Search API | P1 |
| FR-116 | Configure API key via configuration (environment variable) | P1 |
| FR-117 | Handle rate limiting with exponential backoff | P1 |
| FR-118 | Parse and normalize search results to structured format | P1 |

### 11.5 Web Fetch Tool

| ID | Feature | Priority |
|----|---------|----------|
| FR-119 | Implement `WebFetchTool` using HTTP client | P1 |
| FR-120 | Implement content extraction (strip ads, navigation, boilerplate) | P1 |
| FR-121 | Support markdown and plain text output formats | P1 |
| FR-122 | Handle malformed HTML gracefully | P1 |

### 11.6 Bird Tool (X/Twitter)

| ID | Feature | Priority |
|----|---------|----------|
| FR-123 | Implement `BirdTool` wrapper around `bird` CLI | P1 |
| FR-124 | Implement timeline query mode | P1 |
| FR-125 | Implement search query mode | P1 |
| FR-126 | Handle rate limiting from Twitter API | P1 |

### 11.7 Current Position Tool

| ID | Feature | Priority |
|----|---------|----------|
| FR-127 | Implement `CurrentPositionTool` as REST client to Execution Service | P1 |
| FR-128 | Handle connection failures gracefully (return error, not crash) | P1 |
| FR-129 | Cache position data for N seconds to reduce API calls | P1 |

### 11.8 Technical Indicators Tool

| ID | Feature | Priority |
|----|---------|----------|
| FR-130 | Implement `TechnicalIndicatorsTool` fetching from data provider | P1 |
| FR-131 | Support indicators: RSI, MACD, Bollinger Bands, EWMAs, volume profiles | P1 |
| FR-132 | Support timeframes: 1h, 4h, 1d, 1w | P1 |
| FR-133 | Cache indicator data (configurable TTL) | P1 |

### 11.9 Tool Error Handling

| ID | Feature | Priority |
|----|---------|----------|
| FR-134 | Implement retry logic for transient failures (network timeout, 5xx errors) | P1 |
| FR-135 | Implement circuit breaker for failing external services | P1 |
| FR-136 | Return structured error messages to agent (not raw exceptions) | P1 |
| FR-137 | Log all tool executions with input/output for debugging | P1 |

### 11.10 Tool Authentication

| ID | Feature | Priority |
|----|---------|----------|
| FR-138 | Store API keys securely (environment variables or secrets manager) | P1 |
| FR-139 | Validate API keys on startup (fail fast if missing/invalid) | P1 |
| FR-140 | Support API key rotation without restart | P1 |

### 11.11 Tool Logging & Audit

| ID | Feature | Priority |
|----|---------|----------|
| FR-141 | Log every tool call: timestamp, tool name, parameters, duration, result size | P1 |
| FR-142 | Log tool errors with full stack traces | P1 |
| FR-143 | Expose tool usage metrics via status API | P1 |

### 11.12 Agent Tool Integration

| ID | Feature | Priority |
|----|---------|----------|
| FR-144 | Inject tool descriptions into agent system prompts at runtime | P0 |
| FR-145 | Provide tools to agent in format matching LLM provider (OpenAI function calling or similar) | P0 |
| FR-146 | Parse agent's tool call responses and route to correct executor | P0 |
| FR-147 | Stream tool results back to agent for continued reasoning | P0 |

---

## 12. Agent Context Injection

The Agent Runner is responsible for passing all context to each agent at execution time. This section defines the injection sequence.

### 12.1 Agent Identity & Tools

| ID | Feature | Priority |
|----|---------|----------|
| FR-148 | Load all agent identity files (`agent_prompts/*.md`) at startup | P0 |
| FR-149 | Load tool definitions from `tools.md` at startup | P0 |
| FR-150 | Implement context builder that assembles agent prompt in correct sequence: identity → tools → mailbox placeholder → artifact paths | P0 |
| FR-151 | Provide input artifact paths to agent before execution begins | P0 |
| FR-152 | Validate that all required identity files exist before first cycle | P0 |

### 12.2 Output Template Management

| ID | Feature | Priority |
|----|---------|----------|
| FR-153 | Load all output templates (`output_templates/*.md`, `output_templates/*.json`) at startup | P0 |
| FR-154 | Provide correct output template path to agent before execution | P0 |
| FR-155 | Validate agent output conforms to template structure before accepting | P1 |
| FR-156 | Support both Markdown and JSON output templates | P0 |

---

## 13. Cycle Directory Management

| ID | Feature | Priority |
|----|---------|----------|
| FR-157 | Create timestamped cycle directory `cycles/YYYYMMDD_HHMMSS/` at cycle start | P0 |
| FR-158 | Configure base artifact directory (default: `./artifacts/`) | P3 |
| FR-159 | Store all step outputs in current cycle directory | P0 |
| FR-160 | Implement cycle archive: compress old cycles to `cycles/history/` | P3 |
| FR-161 | Configure archive retention policy (keep last N cycles uncompressed) | P3 |

---

## 14. Human Override Support

| ID | Feature | Priority |
|----|---------|----------|
| FR-162 | Implement REST API endpoint `POST /api/override/pause` - pause current cycle | P1 |
| FR-163 | Implement REST API endpoint `POST /api/override/abort` - abort current cycle immediately | P1 |
| FR-164 | Implement REST API endpoint `POST /api/override/inject` - inject context into running agent's mailbox | P1 |
| FR-165 | Implement REST API endpoint `POST /api/override/force-cycle` - trigger immediate new cycle | P1 |
| FR-166 | Require authentication for all override endpoints | P1 |
| FR-167 | Log all override commands with operator attribution | P1 |

---

## 15. Parallel Execution Configuration

| ID | Feature | Priority |
|----|---------|----------|
| FR-168 | Add configuration flag `EnableParallelAgentExecution: bool` (default: false) | P2 |
| FR-169 | When enabled, allow Analysis and Research to run concurrently after Research completes | P2 |
| FR-170 | Document which agents can safely run in parallel (Research → Analyze are parallelizable) | P2 |

---

## 16. Enhanced Artifact Validation

| ID | Feature | Priority |
|----|---------|----------|
| FR-171 | Validate `plan.md` has all 7 sections per template | P1 |
| FR-172 | Validate `research.md` has all required sections per template | P1 |
| FR-173 | Validate `analysis.md` has all required sections per template | P1 |
| FR-174 | Validate `evaluation.md` has performance metrics table | P1 |
| FR-175 | Provide detailed validation error messages showing missing sections | P1 |

---

## 17. Memory File Management

| ID | Feature | Priority |
|----|---------|----------|
| FR-176 | Create `plan/memory.md` if not exists on first run | P1 |
| FR-177 | Create `research/memory.md` if not exists on first run | P1 |
| FR-178 | Create `analysis/memory.md` if not exists on first run | P1 |
| FR-179 | Implement memory append API for agent use | P2 |
| FR-180 | Implement memory search API for cross-cycle context retrieval | P2 |

---

## Appendix A: Agent Execution Context Injection Sequences

### Plan Agent Execution Flow

```
1. Agent Runner loads identity: plan_agent.md
2. Agent Runner appends tools: web_search, web_fetch, bird, technical_indicators
3. Agent Runner provides: mailbox.plan path (agent reads at runtime)
4. Agent Runner provides: plan/memory.md path + past 7 days evaluation paths (agent reads at runtime)
5. Agent Runner provides: output template path (plan.md template)
6. Agent Runner spawns agent with assembled context
7. Agent executes Steps 3-9 per spec (produces plan.md)
8. Agent Runner validates: plan.md exists and has 7 sections
9. Agent Runner deposits: message in mailbox.research (forward context)
10. Agent Runner deposits: message in mailbox.plan (backward feedback)
11. Agent Runner moves: plan.md to current cycle directory
```

### Research Agent Execution Flow

```
1. Agent Runner loads identity: research_agent.md
2. Agent Runner appends tools: web_search, web_fetch, bird, technical_indicators
3. Agent Runner provides: mailbox.research path (agent reads at runtime)
4. Agent Runner provides: plan.md path (from Plan step)
5. Agent Runner provides: research/memory.md path
6. Agent Runner provides: output template path (research.md template)
7. Agent Runner spawns agent with assembled context
8. Agent executes Steps 4-9 per spec (produces research.md)
9. Agent Runner validates: research.md exists and has required sections
10. Agent Runner deposits: message in mailbox.analysis (forward context)
11. Agent Runner deposits: message in mailbox.plan (backward feedback)
12. Agent Runner moves: research.md to current cycle directory
```

### Analysis Agent Execution Flow

```
1. Agent Runner loads identity: analysis_agent.md
2. Agent Runner appends tools: current_position, technical_indicators
3. Agent Runner provides: mailbox.analysis path (agent reads at runtime)
4. Agent Runner provides: research.md path
5. Agent Runner provides: output template path (analysis.md template)
6. Agent Runner spawns agent with assembled context
7. Agent calls: tool.current_position (via tool executor)
8. Agent calls: tool.technical_indicators (via tool executor)
9. Agent executes Steps 6-12 per spec (produces analysis.md)
10. Agent Runner validates: analysis.md exists and has required sections
11. Agent Runner deposits: message in mailbox.synthesis (forward context)
12. Agent Runner deposits: message in mailbox.research (backward feedback)
13. Agent Runner moves: analysis.md to current cycle directory
```

### Synthesis Agent Execution Flow

```
1. Agent Runner loads identity: synthesis_agent.md
2. Agent Runner appends tools: current_position
3. Agent Runner provides: mailbox.synthesis path (agent reads at runtime)
4. Agent Runner provides: analysis.md path
5. Agent Runner provides: output template path (strategy.json template)
6. Agent Runner spawns agent with assembled context
7. Agent calls: tool.current_position (via tool executor)
8. Agent executes Step 5-6 per spec (produces strategy.json)
9. Agent Runner validates: strategy.json conforms to schema
10. Agent Runner deposits: message in mailbox.evaluation (forward context)
11. Agent Runner deposits: message in mailbox.analysis (backward feedback)
12. Agent Runner moves: strategy.json to current cycle directory
13. Agent Runner notifies: Execution Service of new strategy
```

### Evaluation Agent Execution Flow

```
1. Agent Runner loads identity: evaluation_agent.md
2. Agent Runner appends tools: current_position
3. Agent Runner provides: mailbox.evaluation path (agent reads at runtime)
4. Agent Runner provides: paths to strategy.json, analysis.md, current_position
5. Agent Runner provides: paths to 3 most recent evaluation.md files
6. Agent Runner provides: output template path (evaluation.md template)
7. Agent Runner spawns agent with assembled context
8. Agent calls: tool.current_position (via tool executor)
9. Agent executes Steps 7-13 per spec (produces evaluation.md)
10. Agent Runner validates: evaluation.md exists and has performance metrics
11. Agent Runner deposits: broadcast messages to mailbox.plan, mailbox.research, mailbox.analysis, mailbox.synthesis
12. Agent Runner moves: evaluation.md to current cycle directory
```

---

## Appendix B: File Structure

```
crypton/
├── agent_runner/
│   ├── src/
│   │   └── AgentRunner/
│   │       ├── Program.cs
│   │       ├── StateMachine/
│   │       ├── Agents/
│   │       ├── Tools/
│   │       ├── Mailbox/
│   │       ├── Artifacts/
│   │       └── Configuration/
│   └── tests/
├── agent_prompts/
│   ├── plan_agent.md
│   ├── research_agent.md
│   ├── analysis_agent.md
│   ├── synthesis_agent.md
│   ├── evaluation_agent.md
│   └── tools.md
├── output_templates/
│   ├── plan.md
│   ├── research.md
│   ├── analysis.md
│   ├── strategy.json
│   ├── evaluation.md
│   └── memory.md
├── cycles/
│   ├── 20260221_140000/
│   │   ├── plan.md
│   │   ├── research.md
│   │   ├── analysis.md
│   │   ├── strategy.json
│   │   └── evaluation.md
│   └── history/
├── mailboxes/
│   ├── mailbox.plan
│   ├── mailbox.research
│   ├── mailbox.analysis
│   ├── mailbox.synthesis
│   └── mailbox.evaluation
├── plan/
│   └── memory.md
├── research/
│   └── memory.md
├── analysis/
│   └── memory.md
└── docs/
    └── feature_backlog.md
```

---

*Backlog last updated: 2026-02-21*

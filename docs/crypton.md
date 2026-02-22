# Crypton

An autonomous, agentic cryptocurrency portfolio management system.

**Design Principles**
- Rational, explainable decision-making
- Deterministic, auditable strategy execution
- Continuous self-improvement through iterative learning
- Fully autonomous operation with optional human oversight
- Support for sophisticated, multi-factor trading strategies

## Overview

Crypton is an autonomous cryptocurrency portfolio management system built around a continuous learning loop. It consists of four major subsystems:

1. **Learning Loop** — An agentic pipeline of specialized AI agents that continuously monitors market conditions, performs analysis, and synthesizes trading strategies. Each agent maintains its own persistent memory and evolving knowledge base. Agents make heavy use of tool calls (e.g., web search, web fetch, on-chain data) and ad hoc Python scripts for analysis.

2. **Execution Service** — A deterministic trading engine that receives the strategy produced by the Learning Loop and executes it against the Kraken exchange. It runs continuously, managing active positions and responding to market events in real time until the strategy is superseded or a human override is issued.

3. **Agent Runner** — A service that manages the lifecycle of all agents in the Learning Loop via a state machine, enforcing each agent's contract and ensuring the loop advances correctly.

4. **Monitoring Dashboard** — A real-time visualization layer providing visibility into the current strategy, open positions, market conditions, technical indicators, and historical performance.

### System Contract: `strategy.json`

The `strategy.json` file is the formal contract between the Learning Loop and the Execution Service. It is produced by the `Synthesis Agent` at the end of each loop cycle and consumed deterministically by the `Execution Service`. It fully specifies all entry and exit rules, position sizing, and risk management parameters. Valid strategies include "no trade", "hold", and "liquidate all holdings". A domain-specific language (DSL) should be considered for expressing complex conditional rules within the strategy schema.

### Inter-Agent Communication

Each agent maintains a private inbox (`mailbox.<agent>`). Adjacent agents may deposit short, targeted messages (one to two sentences) into a downstream agent's inbox to carry forward context or flag high-priority signals across duty cycles.

### Exchange Support

The system is initially scoped to the Kraken exchange. An abstraction layer around the exchange API is required from the outset to enable support for additional exchanges in future iterations.

### Operation Modes

The system begins in **paper trading** mode to validate and refine strategies using simulated execution before transitioning to **live trading** mode against real capital. Both modes are supported by the Execution Service, which also enforces a safe-mode fallback upon detecting repeated failures.


## Learning Loop

```
Plan → Research → Analyze → Synthesize → Execute → Evaluate → Repeat
```

The Learning Loop is the intelligence core of Crypton. It runs as a continuous cycle in which each agent performs a specialized role, building on the output of the previous step. Agents share state through structured output files and short inter-agent mailbox messages. Each complete cycle produces a new `strategy.json`, which the Execution Service picks up and applies.

> **Possible Enhancement:** An ad hoc Deep Research Agent that designs and runs ML models to perform deep signal analysis, surfacing hidden market indicators not captured by standard technical analysis.


### Plan

**Perform a full-spectrum market sweep.**

The `Plan Agent` opens each learning cycle with a broad, first-pass scan across market data, on-chain activity, news, and other high-signal sources. Its goal is to surface research leads and signals worth deeper investigation, then translate those findings into a prioritized research agenda for the Research Agent to execute. It outputs a `plan.md` file.

**Inputs:** `plan_agent.md`, `mailbox.plan`, `plan/memory.md`, prior evaluation summaries  
**Output:** `plan.md`

**Steps:**

1. Read `plan_agent.md` (agent identity and standing instructions).
2. Read the last 5 messages from `mailbox.plan`.
3. Review `plan/memory.md` and evaluation summaries from the past 7 days. Identify persistent themes, recurring failures, and meta-signals — patterns that have appeared across multiple cycles and warrant continued attention. Note how each is trending (strengthening, weakening, or stable). These become **Section 1** of `plan.md`.
4. Assess current macro market conditions across all relevant dimensions: trend direction, sentiment (risk-on vs. risk-off), volatility regime, liquidity conditions, correlation with broader equity markets, and any near-term macro events (e.g., central bank decisions, economic data releases). Write a brief summary narrative. This becomes **Section 2** of `plan.md`.
5. Compute or retrieve technical indicators for all assets under consideration. For each asset with a notable signal, record the indicator, timeframe, specific observation, and a signal strength rating (High / Medium / Low). Organize findings per asset. This becomes **Section 3** of `plan.md`.
6. Scan on-chain data for notable events. For each event, record the event type, affected asset, specific details, and a signal strength rating. This becomes **Section 4** of `plan.md`.
7. Ingest and scan the following standing information sources for signals with plausible market impact. Organize findings by category. This becomes **Section 5** of `plan.md`.
   - **Government & Regulation:** New executive orders, pending legislation, regulatory agency actions, enforcement events
   - **Macro & Finance:** Central bank activity, macroeconomic data, banking and financial sector news
   - **Crypto-Specific News:** Protocol updates, exchange events, hack/exploit reports, major fundraises or failures
   - **Technology & Industry:** Relevant tech sector developments, AI, infrastructure, startup activity
   - **Social & Sentiment (X / Influencers):** Crypto/finance influencers (e.g., Unusual Whales), crypto-specific accounts, tech leaders, trending topics
   - **Congressional & Institutional Activity:** Findings from https://www.quiverquant.com/ and https://www.capitoltrades.com/buzz
8. Using all findings from steps 3–7, construct a prioritized **Research Agenda** for the Research Agent. For each investigation item, specify: a clear title, why it is important (which signal prompted it), the specific questions that must be answered, and suggested sources or search strategies. Rank items by estimated market impact and time sensitivity into three tiers: investigate immediately, investigate if time permits, and monitor only. This becomes **Section 6** of `plan.md`.
9. Log all observed signals that were considered but did not make the Research Agenda. Record a brief note on why each was deprioritized. This becomes **Section 7** of `plan.md`, and a summary should also be appended to `plan/memory.md` for future cycle awareness.
10. Produce the final `plan.md` containing all seven sections.

### Research

**Amplify and validate interesting signals.**

While the `Plan Agent` performs a broad full-spectrum sweep, the `Research Agent` conducts a deep, focused investigation of the specific leads and signals identified in `plan.md`. Its goal is to gather high-quality, well-sourced, and well-organized information that gives the Analysis Agent everything it needs to form a complete market picture — without requiring the Analysis Agent to do any further data gathering.

**Inputs:** `research_agent.md`, `mailbox.research`, `plan.md`  
**Output:** `research.md`

**Steps:**

1. Read `research_agent.md` (agent identity and standing instructions).
2. Read the last 5 messages from `mailbox.research`.
3. Read `plan.md` in full. The Research Agenda in Section 6 of `plan.md` defines your primary work order. Work through each item in priority order.
4. Establish the market context — the macro environment, broad crypto market structure, and relevant recent history. This becomes the **Market Context** section of `research.md` and orients everything that follows.
5. For each item in the Research Agenda, use `tool.web_search`, `tool.web_fetch`, `tool.bird`, and other available tools to answer the questions posed by the Plan Agent. For each finding, assess and record:
   - A verdict (Confirmed / Contradicted / Nuanced / Inconclusive)
   - A confidence level (High / Medium / Low)
   - The specific facts discovered, each with its source
   - Any questions that remain unanswered
   These become the **Investigation Findings** section of `research.md`, one subsection per Research Agenda item.
6. Record any additional material findings surfaced during research that were not in the Research Agenda but warrant the Analysis Agent's attention.
7. Note any source quality issues, known biases, or data freshness concerns encountered during research.
8. List any questions that could not be answered this cycle and recommend how the Plan Agent might pursue them next cycle.
9. Write the **Executive Summary** last: 3–5 sentences capturing the most important findings, whether key signals were confirmed or contradicted, and the overall character of the market environment.
10. Update `research/memory.md` with self-notes, dead ends, source quality observations, and leads worth revisiting in future cycles.

### Analysis

**Interpret signals and assess market implications.**

The `Analysis Agent` performs a deep, structured analysis of the research findings in the context of current positions and technical data. Where the `Plan Agent` identifies *what* is happening and the `Research Agent` establishes *why*, the `Analysis Agent` determines *what it means* and *what is likely to happen next*. Its output must be opinionated enough for the Synthesis Agent to generate a decisive strategy.

**Inputs:** `analysis_agent.md`, `mailbox.analysis`, `research.md`, `tool.current_position`, `tool.technical_indicators`  
**Output:** `analysis.md`

**Steps:**

1. Read `analysis_agent.md` (agent identity and standing instructions).
2. Read the last 5 messages from `mailbox.analysis`.
3. Read `research.md` in full.
4. Call `tool.current_position` to load the current portfolio state.
5. Call `tool.technical_indicators` for all relevant assets and timeframes to supplement and verify the technical picture in `research.md`.
6. Synthesize the macro, technical, on-chain, and sentiment data into a unified **Market Overview**: what the environment means collectively, what regime the market is operating in, and an overall market assessment (Strongly Bullish → Strongly Bearish). This becomes the **Market Overview** section of `analysis.md`.
7. For each asset with actionable signal, develop a full per-asset analysis: interpret the technical situation, fundamental and news factors, and on-chain data; identify key price levels; state a directional thesis with a conviction rating; and list the specific condition that would invalidate the thesis. These become the **Per-Asset Analysis** sections of `analysis.md`.
8. Assess all current open positions against the cycle's findings. For each position, determine whether it is aligned, misaligned, or neutral relative to the thesis, and note the action indicated. This becomes the **Current Position Assessment** section.
9. Build a **Risk Matrix** of meaningful risks, assessed by likelihood and portfolio impact. Include at least one tail-risk scenario per asset with an actionable mitigation.
10. Synthesize all conclusions into a direct **Synthesis Briefing**: a consolidated, unambiguous brief that states the market stance, recommended posture, per-asset direction and conviction, risk budget guidance, and stop/exit conditions.
11. Note any **Emerging Signals** — early or developing signals not yet actionable but worth tracking in the next cycle.
12. Write the **Executive Summary** last, once the full analysis is complete.

### Synthesize

**Translate analysis into an executable strategy.**

The `Synthesis Agent` is the decision-maker of the Learning Loop. It reads the full analysis and current portfolio state, weighs all factors, and produces a `strategy.json` file — the formal, machine-readable contract that the Execution Service will carry out. Every valid outcome is available, including "flat" (no new trades), "exit_all", and "hold existing positions". The Synthesis Agent must commit to a course of action; a non-decision is itself a decision.

**Inputs:** `synthesis_agent.md`, `mailbox.synthesis`, `analysis.md`, `tool.current_position`  
**Output:** `strategy.json`

**Steps:**

1. Read `synthesis_agent.md` (agent identity and standing instructions).
2. Read the last 5 messages from `mailbox.synthesis`.
3. Read `analysis.md` in full, with particular attention to the **Synthesis Briefing** section.
4. Call `tool.current_position` to load the current portfolio state.
5. Determine the most rational course of action given the analysis, current positions, and risk environment.
6. Generate a `strategy.json` file that strictly adheres to the strategy schema. The file must specify:
   - **Mode:** `paper` or `live`
   - **Validity window:** the datetime at which this strategy expires and must be replaced by the next cycle
   - **Posture:** overall risk stance (aggressive / moderate / defensive / flat / exit_all) with rationale
   - **Portfolio-level risk management:** maximum drawdown threshold, daily loss limit, maximum total exposure, maximum per-position size, and safe-mode trigger conditions
   - **Positions:** one entry per position to open, manage, or close. Each position specifies:
     - Asset, direction, and capital allocation percentage
     - Entry type (`market`, `limit`, or `conditional`) and entry conditions expressed as evaluable rules over live indicator data
     - Scaled take-profit targets with partial close percentages
     - Stop-loss (hard and/or trailing)
     - Optional time-based exit
     - Invalidation condition (the market state that signals the thesis is wrong)
   - **Strategy rationale:** a brief prose explanation of the overall logic for logging and dashboard display


### Execute

**Work the plan.**

The `Execution Service` picks up the latest `strategy.json` and carries it out against the Kraken exchange using the REST and WebSocket APIs. It provides continuous, active position management — monitoring entry and exit triggers, routing orders, enforcing risk rules, and handling real-time market events — until the strategy is superseded by a new cycle or a human operator issues an override.

Full requirements for the Execution Service are defined in the [Execution Service](#execution-service) section.

### Evaluate

**Measure, judge, and inform the next cycle.**

The `Evaluation Agent` closes each Learning Loop cycle and seeds the next. It approaches strategy performance with purely logical, dispassionate rigor — the analytical mind of a Vulcan — assessing outcomes against intent without bias toward preserving a failing strategy. Its `evaluation.md` report is a primary input to the `Plan Agent`'s meta-signal analysis in the following cycle.

**Inputs:** `evaluation_agent.md`, `mailbox.evaluation`, `tool.current_position`, `analysis.md`, `strategy.json`, prior `evaluation.md` files  
**Output:** `evaluation.md`

**Steps:**

1. Read `evaluation_agent.md` (agent identity and standing instructions).
2. Read the last 5 messages from `mailbox.evaluation`.
3. Call `tool.current_position` to load the current portfolio state and the complete trade history for this cycle.
4. Read `strategy.json` to recall the exact rules, allocations, validity window, and risk limits that were in effect.
5. Read `analysis.md` to recall every prediction, thesis, and risk assessment that the strategy was built upon.
6. Read the three most recent prior `evaluation.md` files to identify recurring patterns, persistent failure modes, or consistently accurate signals.
7. Compile the **Performance Metrics** table: net P&L, return, max drawdown, daily loss limit breaches, peak exposure, win rate, and average winning and losing trade sizes. Compare each against the limits set in `strategy.json`. This becomes the **Performance Metrics** section of `evaluation.md`.
8. Conduct a factual **Trade Review**: for each trade executed, record entry price and time, exit price and time, exit reason, and P&L. Note any execution failures, missed triggers, slippage, or API errors.
9. Conduct an **Analysis Accuracy Assessment**: for each key claim in `analysis.md` (market direction prediction, per-asset theses, flagged risks), compare the prediction against what actually occurred. State which predictions were accurate, partially accurate, or wrong, and identify the most significant miss.
10. Assess the **Strategy Specification**: were entry conditions correctly written, were stop levels appropriately placed relative to the thesis, was position sizing appropriate for conviction, was the validity window suitable?
11. Write the **Overall Assessment**: a 3–5 sentence diagnosis of what happened, why, and the state of the portfolio entering the next cycle.
12. Write **Recommendations for Next Cycle**: direct, actionable guidance for the Plan Agent, Analysis Agent, and Synthesis Agent covering what to prioritize, what errors to avoid, and the recommended portfolio stance.
13. Assign a performance rating (A through F) and overall verdict. Write `evaluation.md`.


## Execution Service

A long-running service that deterministically executes the active `strategy.json` against the exchange. It is the boundary between the agentic Learning Loop and live market activity. It requires no AI reasoning — all decisions are encoded in the strategy file.

**Strategy Execution**
- Watch for a new or updated `strategy.json` file and hot-reload it without restarting the service.
- Evaluate entry and exit conditions continuously against live market data.
- Route buy, sell, and limit orders to the exchange according to strategy rules.
- Enforce position sizing, capital allocation limits, and risk constraints defined in the strategy.
- Honor the strategy validity window; flag expiry and suspend new entries if no updated strategy has been received.

**Exchange Integration**
- Use the Kraken WebSocket API for real-time market data (price feeds, order book, trade events).
- Use the Kraken REST API for order management, account queries, and position updates.
- Implement an exchange abstraction layer from the outset so that additional exchange adapters (e.g., Coinbase, Binance) can be added without changes to core execution logic.

**Operation Modes**
- **Paper trading mode:** Simulate all order execution against live market data with no real capital at risk. Used during initial strategy validation and for regression testing new strategies.
- **Live trading mode:** Execute orders against the real exchange with real capital. Requires explicit human promotion from paper mode.

**Resilience and Safety**
- Detect N consecutive application failures (crashes or unclean exits) and automatically enter **safe mode**: suspend new position entries and attempt an orderly exit of all open positions to minimize risk exposure.
- Detect exchange rate-limiting responses and apply adaptive back-off to avoid API bans or order failures.
- Implement a dead man's switch: if the service loses connectivity or heartbeat for a configurable duration, enter safe mode automatically.
- On restart after failure, reconcile local state against the exchange's actual positions before resuming execution.

**Human Operator Interface**
- Expose a REST API for operator commands, including: override strategy, suspend trading, force safe mode, manually close a position, and promote from paper to live mode.
- All operator commands are authenticated, logged with attribution, and acknowledged with a confirmation response.

**Observability**
- Emit a structured, append-only event log capturing every decision, order attempt, fill, rejection, and state transition with full context.
- Expose current service status, active strategy summary, open positions, and recent trade history via the REST API for consumption by the Monitoring Dashboard.

## Monitoring Dashboard

A real-time web dashboard providing full visibility into system state, active strategy, market conditions, and historical performance. It is read-only — operator commands are issued via the Execution Service REST API, not through the dashboard.

**Portfolio & Positions**
- Display current holdings, open positions, unrealized P&L, and available capital.
- Show a full trade history for the current and past strategies with entry/exit prices, size, and outcome.

**Active Strategy**
- Display the active `strategy.json` in a human-readable format: target assets, entry/exit rules, risk parameters, and validity window.
- Visualize the thresholds and trigger levels at which the Execution Service will open or close positions, overlaid on a live price chart.
- Indicate current strategy mode (paper vs. live) and time remaining in the validity window.

**Market Conditions**
- Display live price feeds for all assets relevant to the active strategy.
- Display the technical indicators referenced by the current strategy, with configurable timeframe views.
- Surface macro market condition signals (e.g., trend direction, volatility regime, sentiment indicators).

**Performance**
- Show return metrics for the current strategy cycle: realized P&L, win rate, average trade return, and maximum drawdown.
- Show lifetime system performance across all completed strategy cycles.
- Provide a per-cycle performance history view, linking each cycle's result to its `evaluation.md` and `strategy.json`.

**Learning Loop Status**
- Show the current state of the Agent Runner loop (which step is active, when the last cycle completed, and when the next is expected).
- Surface the most recent `evaluation.md` summary.

## Agent Runner

A long-running orchestration service that drives the Learning Loop. It manages the lifecycle of all agents through a state machine, enforces timing and sequencing contracts, and handles the handoff of artifacts between steps.

**Loop Orchestration**
- Advance the loop through each stage in order: Plan → Research → Analyze → Synthesize → Execute → Evaluate → Repeat.
- Track the current loop stage in persistent state so the loop can resume correctly after a restart.
- Enforce a configurable cycle interval (i.e., the minimum and maximum time between loop completions).

**Agent Lifecycle Management**
- Spawn each agent as needed for its assigned step; do not run agents speculatively or in parallel unless explicitly designed to do so.
- Enforce a per-agent execution timeout. If an agent exceeds its time budget, mark the step as failed and apply the appropriate recovery policy.
- Validate each agent's output artifact (e.g., `plan.md`, `research.md`, `strategy.json`) before advancing to the next step. A missing or malformed output is treated as a step failure.

**Mailbox Management**
- Manage delivery of inter-agent messages to each agent's `mailbox.<agent>` inbox.
- Enforce the mailbox retention policy (e.g., retain last N messages per agent).

**Failure and Recovery**
- On a step failure, apply a configurable retry policy (e.g., retry up to N times with back-off).
- If a step exhausts its retries, escalate: log the failure, notify the operator via the REST API, and depending on configuration either halt the loop or skip the failed step and continue with the last known good artifact.
- Detect if the Synthesis step has not produced a new `strategy.json` within the strategy validity window and flag the Execution Service accordingly.

**Observability**
- Emit a structured event log for all loop transitions, agent invocations, step outcomes, and failures.
- Expose current loop state, last cycle summary, and error history via a status API for the Monitoring Dashboard.

## Tools

A collection of agent-callable tools that extend what agents can do beyond pure reasoning. Each tool has a well-defined interface, returns structured or clean-text output suitable for LLM consumption, and is invocable via a consistent calling convention.

**`tool.web_search`**  
Performs a web search via the Brave Search API. Accepts a query string and optional parameters (e.g., result count, recency filter). Returns a ranked list of results with title, URL, and snippet. Used by the Plan and Research Agents for broad discovery and signal validation.

**`tool.web_fetch`**  
Fetches and extracts the readable content of a web page at a given URL. Strips navigation, ads, and boilerplate, returning clean prose suitable for agent ingestion. Used by the Research and Plan Agents to read full articles, reports, and data pages identified during search.

**`tool.bird`**  
A wrapper around the `bird` CLI for retrieving posts and profile data from X (Twitter). Accepts queries such as user timelines, keyword searches, and trending topics. Used by the Plan and Research Agents to ingest signals from crypto influencers, market accounts, and tech leaders.

**`tool.current_position`**  
Makes a REST call to the Execution Service to retrieve the current portfolio state: open positions, holdings, available capital, unrealized P&L, and recent trade history. Used by the Analysis, Synthesis, and Evaluation Agents to ground their reasoning in the actual current state of the portfolio.

**`tool.technical_indicators`**  
Fetches or computes technical indicator data for a specified asset and timeframe (e.g., RSI, MACD, Bollinger Bands, EWMAs, volume profiles). Returns structured summaries suitable for inclusion in agent context. Used by the Plan and Analysis Agents.

## Ancilla

Cross-cutting technical standards and conventions that apply across the entire project.

**Technology Stack**
- Backend services: C#
- Frontend: React (lightweight alternatives may be considered)
- All services deployed as Docker containers; `docker compose` manages the full local stack

**Code Quality**
- 100% test coverage is non-negotiable
- All public-facing code must include inline documentation suitable for IDE ingestion (C# XML doc comments, JavaScript/TypeScript JSDoc)
- All features must be documented in code at the point of implementation

**Repository and Workflow**
- The project is a mono-repo
- All feature work is developed on dedicated branches; one branch per feature
- Follow the PR workflow: no direct commits to `main`
- Commit early and often with meaningful commit messages
- PRs targeting `main` trigger an automated build and test pipeline via GitHub Actions
- A local pipeline runner on the development workstation mirrors the CI pipeline for pre-push validation

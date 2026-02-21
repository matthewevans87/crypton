# Crypton
Agentic cryptocurrency day trader. 
Focus:
- Rational, explainable behavior
- Deterministic strategy execution
- Improvement over time through self-learning
- No human in the loop
- Actualize complex strategies

## Overview
The system consists of two parts: an agentic learning loop to devise trading strategies and a deterministic engine to execute them.

- Start with Kraken crypto exchange, but support other exchanges over time.
- Trading strategy is a highly structured contract. 
- Consider using a DSL for complex rules that the strategy `Execution Service` interprets
- The learning loop collects and analyzes information and produces a well defined `strategy.json` file.
- The agentic learning loop will adapt and evolve its strategies over time based on market conditions and past experiences.
- The `Execution Service` receives the `strategy.json` file and executes it deterministically.  
- The each step of the learning loop has its own specialized agent with its own memory and evolving knowledge base. 
- The agents will make heavy use of tool calls (e.g., web search and web fetch) for gathering information
- Agents will and ad hoc python scripts for analysis. The system is designed to be fast, adaptive, and robust, with a focus on maximizing profitability while managing risk effectively. The system starts with paper trading to learn and refine strategies before transitioning to real money trading.
- Agents can send small signals to one another (e.g., a one-two sentence message or request to help guide the other agents on their next duty cycle).


## Learning Loop

```
Plan -> Research -> Analyze -> Synthesize -> Execute -> Evaluate -> Repeat
```

> Possible Enhancement: Ad hoc Deep Research Agent which designs ML models to perform deep signal analysis, looking for hidden market indicators.


### Plan

**Scan for signals.**

The `Plan Agent` performs a wide first pass, looking for research leads and signals. It produces a `plan.md` file with a collection of research topics and leads for the Researcher Agent to investigate. 

`plan.md` is updated at each of the following steps.

1. Read `plan_agent.md`
2. Read last 5 `mailbox.planner` messages.
3. Analyze behavior summaries from the past 7 days looking for meta-signals.
4. Identify current market trends (bearish, bullish, etc.)
5. Compute technical indicators and make note of technical signals that deserve further exploration (e.g., price movements, volume changes, technical indicator crossovers, and other market data that may indicate potential trading opportunities or risks).
6. Identify on-chain events (e.g., "whale movements") of note that deserve further exploration
7. First News Search: Takes findings from the above steps and looks for relevant news, geopolitics, celebrity news, tech news, current events, economic news, social trends, tech leaders, startups, trending terms on X and other high-signal patterns. The Planner is looking for any information that could impact the market and influence trading decisions.
8. Ingest and interpret the following, looking for potentially relevant information
   - Government
     - New Executive Orders
     - Pending Legislation
   - News
     - Crypto
     - Geopolitics
     - Banking and Finance
     - Celebrity
     - Tech
     - Current events
   - X
     - Crypto and finance Influencers (e.g., unusual whales)
     - Crypto-specific accounts
     - Tech Leaders
   - Websites
     - https://www.quiverquant.com/
     - https://www.capitoltrades.com/buzz
9. Update `research/memory.md` with any self-notes.
10. Produce final `plan.md`.

### Research

**Amplify interesting signals.**

The `Plan Agent` performs a "full spectrum sweep"; the `Research Agent` performs a highly "focused scan" of any interesting signals that were identified and gathers high-quality, relevant information that can inform the Analysis Agent's work.

1. Read `research_agent.md`
2. Read last 5 `mailbox.research` messages.
3. Read `plan.md`.
4. Devise list of questions.
   - In general, what is happening in the market? What are the biggest factors driving market conditions?
   - What questions do I have? What information is missing? Where/how can I find answers to my questions?
5. Use `tool.web_search`, `tool.web_fetch`, `tool.bird`, and other tools to answer its questions.
9. Update `plan_memory.md` with any self-notes.
6. Complies a highly curated information rich `research.md` file for the Analysis Agent.

### Analysis

**Perform a signal analysis.**

The `Analysis Agent` perform a deep analysis of all of the information in `research.md`.

1. Read `analysis_agent.md`
2. Read last 5 `mailbox.analysis` messages.
3. Read `research.md`.
4. Read `tool.current_position`
5. Load a battery of technical indicator data summaries
6. Answer the following questions
  - What emerging trends are we seeing that might be relevant to the market?
  - What is the information saying? 
  - What does it mean?
  - What is going to happen as a result of this?
  - How is this impacting our current position?
  - How _will_ this impact our current position?
The Planner tells "what" and the Researcher answers "why".

The `Analysis Agent` also looks for emerging trends, shifts in sentiment, and other factors that may not be immediately obvious but could have a significant impact on the market. 

1. crawls the provided information sources, looking for causal factors, correlations, and patterns that may impact the market. 
2. Answers the questions:
  - What are the potential implications of ___?

### Synthesize

**Synthesize a plan.**

The `Synthesis Agent` uses the `analysis.md` to generate a `strategy.json` file: the bridge between the agentic workflow and the deterministic `Execution Service`. The `strategy.json` file gives exact rules for when to enter and exit positions, which risk management mechanisms to employ, etc.; it is a complete specification. Note that a strategy of "no change", "no trade", or "liquidate holdings" are all valid. 

1. Read `synthesis_agent.md`
2. Read last 5 `mailbox.synthesis` messages.
3. Read `analysis.md`.
4. Read `tool.current_position`
5. Answer the question: "Given all of this, what should we do?"
6. Generate a `strategy.json` file adhering to a strict schema.


### Execute

**Work the plan.**

The `Execution Service` carries out the strategy defined in the `strategy.json` file, interacting directly with the Kraken exchange using the REST or WebSocket API. It provide continuous active management and execution of the strategy until said strategy is changed, or an override command is issued by a human operator. 

### Evaluate

**Introspect.**

An Evaluation Agent constitutes both the beginning and the ending of the Learning Loop. It must determine if the prescribed strategy has been successful or of it is failing. It has the purely logical non-emotive mind of a Vulkan from Star Trek. It provides a concise `evaluation.md` report of the performance of the strategy.
1. Read `evaluation_agent.md`
2. Read last 5 `mailbox.evaluation` messages.
4. Read `tool.current_position`
5. Read `analysis.md`
6. Read `strategy.json`
7. Evaluate performance of the strategy according, answering the following questions
  - ...
  - ...
8. Write `evaluation.md`


## Execution Service
The service program that runs in a terminal (or as a service)
Known requirements
- Fully Containerized
- Paper vs Live mode
- Failure recovery mechanisms
- Complete action traceability and logging with agent-first approach
- Human operator intervention mechanism
- WebSocket for live datafeed
- REST Api for sending specific commands (e.g., human intervention, changing strategy parameters, etc.).
- Capture events
- Visualizations of market and thresholds for when actions will be taken
- Performance of current strategy
- Lifetime performance
- Abstraction layer around Crypto-exchange API enabling other exchange APIs to be implemented later
- Initially will only include support for Kraken exchange
- Detection of new strategy file
- Detect N previous failures (e.g., application crashes) and start in "safe mode" attempting emergency exit of positions so as to minimize risk. 
- Detect when being throttled and adjust request sends accordingly
To do: Full feature spec

## Monitoring Dashboard
A dashboard wrapper around the whole system.
Known Requirements:
- Presents visualizations, data tables, and strategy details of current and historical strategies
- Current positions
- Current holdings
- Market conditions
- Technical indicators (especially those which are being used in the current strategy)

## Agent Runner
A service program that manages a agents via a state machine and ensures each agent adheres to its contract.
Known Requirements:
- 

## Tools
A collection of agent-first tools. May be able to find existing tools to meet the following needs.
- web_search (Brave Search API)
- web_fetch (Agent-first web browser)
- bird CLI usage
- current_position (REST call to Execution Service)

## Ancilla
- Backend services written in C#
- All frontend written in React (though am open to something more lightweight).
- All features should be documented in code
- All functions should have inline documentation suitable for ingestion by IDEs (e.g., JavaScript docstrings, C# XML comments)
- 100% code coverage is non-negotiable
- The project is a mono-repo
- Use branches for feature work; each feature gets its own branch
- Commit early and often
- Follow the PR workflow
- PRs into `main` trigger a build pipeline (GitHub Actions)
- A local pipeline runner runs on this workstation
- Docker compose is used to set up all containers
- 

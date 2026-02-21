# Crypto Trading Strategy

## Overview
A robust, adaptive crypto trading system with continuous learning loop.

## Learning Loop
1. **PLAN**: Develop market approach, identify signals (technical, news, social, on-chain), identify effective strategies
2. **ANALYZE**: Analyze signals, backtest strategies on historical data, simulate trades, evaluate performance
3. **SYNTHESIZE**: Create YAML strategy file with signals, rules, risk parameters. "NO TRADE" is valid if conditions unfavorable
4. **EXECUTE**: Trade engine executes strategy, places/manages trades, monitors market, collects data
5. **EVALUATE**: Assess profitability, risk, adaptation, update understanding
6. **REPEAT**: Feed data back, continue loop

## Trade Engine Requirements
- Kraken API integration
- Order placement
- Position management
- Risk management
- Position tracking
- Data collection for learning loop

## Information Sources
- Technical indicators
- News articles
- Social media sentiment
- On-chain data
- Recognize "hype" and account for it (can impact market)

## Constraints
- Maximize determinism
- Keep growing memory of what works under what conditions
- Document all efforts for future recall
- Privacy-first, security-aware, financial responsibility
- Start with paper trading, not real money

## Strategy Philosophy
- Start conservative
- Learn before earning
- Paper trading first
- Monitor and iterate continuously
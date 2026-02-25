# Market Data Infrastructure Feature Backlog

## Overview

This backlog defines the features required to build a unified, real-time market data infrastructure that serves both the Monitoring Dashboard and the Execution Engine. The solution provides a single source of truth for all market data with exchange abstraction, real-time streaming, and fault tolerance.

**Design Principles:**
- Single source of truth for market data
- Real-time streaming via WebSocket with SignalR distribution
- Exchange abstraction for multi-exchange support
- Lightning-fast updates (<100ms end-to-end latency target)
- Fault-tolerant with automatic reconnection and caching

---

## Priority Legend

| Priority | Description |
|----------|-------------|
| P0 | Critical - Must work for basic functionality |
| P1 | High | Operational for trading and monitoring |
| P2 | Medium | Enhanced features and performance |
| P3 | Low | Polish, testing, deployment |

---

## 1. Market Data Service (Core)

### 1.1 Service Architecture

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| MDS-001 | Create MarketDataService project | P0 | New .NET service for market data management |
| MDS-002 | Implement IExchangeAdapter interface | P0 | Abstract interface defining exchange operations |
| MDS-003 | Configure dependency injection | P0 | Register services, adapters, SignalR |
| MDS-004 | Implement service configuration (YAML/JSON) | P1 | Exchange API keys, symbols, intervals |
| MDS-005 | Add health check endpoints | P1 | /health/live, /health/ready |
| MDS-006 | Implement structured logging | P1 | All market events, errors, state changes |
| MDS-007 | Docker containerization | P2 | Dockerfile and docker-compose |

### 1.2 Exchange Abstraction Layer

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| EAL-001 | Define unified data models | P0 | PriceTicker, OrderBook, OHLCV, Trade, Balance |
| EAL-002 | Implement KrakenExchangeAdapter | P0 | Kraken WebSocket + REST implementation |
| EAL-003 | Add exchange configuration per adapter | P0 | API keys, rate limits, symbols |
| EAL-004 | Implement rate limiting per exchange | P1 | Respect exchange API limits |
| EAL-005 | Add adapter for Binance (future) | P3 | Placeholder for Binance support |
| EAL-006 | Add adapter for Coinbase (future) | P3 | Placeholder for Coinbase support |

### 1.3 WebSocket Integration

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| WS-001 | Implement WebSocket connection manager | P0 | Manage connections, reconnection |
| WS-002 | Implement auto-reconnect with exponential backoff | P0 | Handle disconnection gracefully |
| WS-003 | Implement subscription management | P0 | Subscribe/unsubscribe to symbol feeds |
| WS-004 | Handle rate limiting responses | P1 | Adaptive backoff on 429 errors |
| WS-005 | Message parsing and normalization | P0 | Convert exchange format to unified model |

### 1.4 REST API Endpoints

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| API-001 | GET /api/prices - Current prices | P0 | All subscribed symbols |
| API-002 | GET /api/prices/{symbol} - Single price | P0 | Specific symbol price |
| API-003 | GET /api/orderbook/{symbol} - Order book | P1 | Current bids/asks |
| API-004 | GET /api/ohlcv - OHLCV data | P0 | Historical candles |
| API-005 | GET /api/balance - Account balance | P0 | Holdings from exchange |
| API-006 | GET /api/trades - Recent trades | P1 | Trade history |
| API-007 | POST /api/subscribe - Subscribe to symbols | P0 | WebSocket subscription management |
| API-008 | GET /api/status - Connection status | P1 | Per-exchange connection state |

### 1.5 Data Caching

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| CACHE-001 | In-memory price cache | P0 | Store latest prices with TTL |
| CACHE-002 | Configurable TTL per data type | P1 | Price: 1s, Indicators: 60s |
| CACHE-003 | Cache warming on startup | P1 | Preload critical data |
| CACHE-004 | Cache invalidation on update | P0 | Real-time cache refresh |

---

## 2. Real-Time Data Streaming

### 2.1 SignalR Hub

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| SHR-001 | Implement MarketDataHub | P0 | SignalR hub for streaming |
| SHR-002 | Price update broadcasting | P0 | Real-time price ticks |
| SHR-003 | Order book update broadcasting | P1 | Bid/ask changes |
| SHR-004 | Trade update broadcasting | P1 | New trade notifications |
| SHR-005 | Balance update broadcasting | P0 | Account changes |
| SHR-006 | Connection state events | P1 | Connect/disconnect notifications |
| SHR-007 | Authentication for hub | P2 | Secure SignalR connections |

### 2.2 Data Distribution

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| DD-001 | Broadcast to multiple subscribers | P0 | Support Dashboard + Execution Engine |
| DD-002 | Per-client subscription filtering | P1 | Subscribe to specific symbols |
| DD-003 | Message serialization optimization | P2 | Efficient binary protocol |
| DD-004 | Backpressure handling | P2 | Handle slow consumers |

---

## 3. Dashboard Integration

### 3.1 Backend API Updates

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| API-101 | Connect PortfolioController to MarketDataService | P0 | Fetch real positions/balance |
| API-102 | Connect MarketController to MarketDataService | P0 | Fetch real prices/indicators |
| API-103 | Implement SignalR client in Dashboard backend | P0 | Receive real-time updates |
| API-104 | Forward market data to frontend via SignalR | P0 | Dashboard → Frontend streaming |
| API-105 | Add reconnection handling | P1 | Auto-reconnect to Market Data Service |

### 3.2 Frontend Updates

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| FE-001 | Update SignalR service for market data | P0 | Connect to MarketDataHub |
| FE-002 | Real-time price updates in PriceTickerPanel | P0 | Live price streaming |
| FE-003 | Real-time position updates | P0 | P&L changes in OpenPositionsPanel |
| FE-004 | Connection status indicator | P1 | Show Market Data Service status |
| FE-005 | Graceful degradation | P1 | Show cached data on disconnect |

---

## 4. AgentRunner Integration

### 4.1 Tool Updates

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| TOOL-001 | Update CurrentPositionTool | P0 | Call Market Data Service REST API |
| TOOL-002 | Update TechnicalIndicatorsTool | P0 | Call Market Data Service for indicators |
| TOOL-003 | Add price caching to tools | P1 | Reduce API calls, improve latency |
| TOOL-004 | Handle tool failures gracefully | P0 | Return error, don't crash |

### 4.2 Agent Context

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| CTX-001 | Document Market Data Service endpoints | P0 | Add to tools.md |
| CTX-002 | Update agent prompts if needed | P2 | Adjust instructions for real data |

---

## 5. Execution Engine Integration (Future)

### 5.1 Market Data Connection

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| EXEC-001 | Execution Engine connects to Market Data Service | P0 | Same as Dashboard |
| EXEC-002 | Real-time position updates | P0 | Via SignalR |
| EXEC-003 | Real-time price for order execution | P0 | For limit orders, stops |
| EXEC-004 | Balance checks before trade | P0 | Via REST API |

---

## 6. Fault Tolerance & Resilience

### 6.1 Connection Management

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| FT-001 | WebSocket auto-reconnect | P0 | Exponential backoff |
| FT-002 | Circuit breaker for exchanges | P1 | Open on repeated failures |
| FT-003 | Graceful degradation | P1 | Serve cached data on failure |
| FT-004 | Dead man's switch | P2 | Detect service failure |

### 6.2 Monitoring & Alerts

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| MON-001 | Connection health metrics | P1 | Track reconnections, latency |
| MON-002 | Data freshness alerts | P1 | Warn if prices stale |
| MON-003 | Rate limit tracking | P1 | Monitor API usage |

---

## 7. Technical Indicators

### 7.1 Indicator Calculation

| ID | Feature | Priority | Description |
|----|---------|----------|-------------|
| IND-001 | RSI calculation | P0 | Relative Strength Index |
| IND-002 | MACD calculation | P0 | Moving Average Convergence Divergence |
| IND-003 | Bollinger Bands | P0 | Upper, middle, lower bands |
| IND-004 | EMA/SMA | P0 | Exponential/simple moving averages |
| IND-005 | Volume profile | P1 | Volume by price level |
| IND-006 | Support multiple timeframes | P0 | 1m, 5m, 15m, 1h, 4h, 1d |

---

## 8. Architecture Diagram

```
                            Market Data Service
┌─────────────────────────────────────────────────────────────────────────┐
├─────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │                     SignalR Hub                                │   │
│  │  • Price updates    • Order book updates    • Balance updates │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                  │
│  │   REST API  │  │   Cache     │  │   Health    │                  │
│  │  Endpoints │  │  Manager    │  │   Checks    │                  │
│  └─────────────┘  └─────────────┘  └─────────────┘                  │
│                                                                         │
│  ┌─────────────────────────────────────────────────────────────────┐   │
│  │              Exchange Abstraction Layer                        │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐        │   │
│  │  │   Kraken     │  │   Binance    │  │   Coinbase   │        │   │
│  │  │   Adapter    │  │   (future)   │  │   (future)   │        │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘        │   │
│  └─────────────────────────────────────────────────────────────────┘   │
│                                                                         │
│  ┌──────────────┐  ┌──────────────┐                                 │
│  │   WebSocket  │  │    REST      │                                 │
│  │  Connection  │  │   Client     │                                 │
│  │   Manager    │  │              │                                 │
│  └──────────────┘  └──────────────┘                                 │
│                                                                         │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                    ┌───────────────┼───────────────┐
                    │               │               │
                    ▼               ▼               ▼
            ┌───────────┐   ┌───────────┐   ┌───────────┐
            │ Dashboard │   │AgentRunner│   │Execution  │
            │           │   │  (Tools)  │   │  Engine   │
            └───────────┘   └───────────┘   └───────────┘
```

---

## 9. Data Flow

### Price Update Flow (<100ms target)

```
1. Kraken WebSocket → Market Data Service
2. Parse & normalize → In-memory cache
3. SignalR broadcast → All subscribers
4. Dashboard backend → SignalR → Frontend
5. Frontend panel re-render
```

### Tool Data Flow

```
1. Agent calls tool.current_position
2. Tool calls Market Data Service REST API
3. Service returns cached or fresh data
4. Tool returns structured data to Agent Runner
```

---

## 10. Implementation Order

### Phase 1: Foundation (P0)
1. Create MarketDataService project
2. Implement IExchangeAdapter interface
3. Implement KrakenExchangeAdapter (WebSocket + REST)
4. Implement in-memory cache
5. Implement REST API endpoints
6. Implement SignalR hub

### Phase 2: Integration (P0)
1. Connect Dashboard backend to Market Data Service
2. Update Dashboard frontend for real-time updates
3. Update AgentRunner tools to use Market Data Service

### Phase 3: Fault Tolerance (P1)
1. Auto-reconnection logic
2. Circuit breaker
3. Health checks and monitoring

### Phase 4: Enhancements (P2)
1. Technical indicators
2. Additional exchanges
3. Performance optimization

---

## 11. Configuration Schema

```yaml
marketData:
  service:
    host: "localhost"
    port: 5001
    
  cache:
    priceTtlSeconds: 1
    orderBookTtlSeconds: 5
    balanceTtlSeconds: 30
    
  exchanges:
    kraken:
      enabled: true
      apiKey: "${KRAKEN_API_KEY}"
      secretKey: "${KRAKEN_SECRET}"
      rateLimit:
        requestsPerMinute: 15
        requestsPerSecond: 5
      symbols:
        - "BTC/USD"
        - "ETH/USD"
        - "SOL/USD"
        
  signalr:
    hubPath: "/hubs/marketdata"
    enableAuthentication: false
```

---

## 12. Related Documentation

- [Monitoring Dashboard README](../monitoring_dashboard/README.md)
- [Agent Runner Feature Backlog](../agent_runner/feature_backlog.md)
- [Crypton System Overview](../crypton.md)

---

*Backlog last updated: 2026-02-25*

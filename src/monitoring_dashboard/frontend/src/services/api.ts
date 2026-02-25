const API_BASE = '/api';

async function fetchJson<T>(url: string): Promise<T> {
  const response = await fetch(`${API_BASE}${url}`);
  if (!response.ok) {
    throw new Error(`API error: ${response.status} ${response.statusText}`);
  }
  return response.json();
}

export const api = {
  // Portfolio
  portfolio: {
    summary: () => fetchJson('/portfolio/summary'),
    holdings: () => fetchJson('/portfolio/holdings'),
    positions: () => fetchJson('/portfolio/positions'),
    trades: (limit = 50, offset = 0) => fetchJson(`/portfolio/trades?limit=${limit}&offset=${offset}`),
  },
  
  // Strategy
  strategy: {
    current: () => fetchJson('/strategy/current'),
    history: (limit = 20) => fetchJson(`/strategy/history?limit=${limit}`),
    byId: (id: string) => fetchJson(`/strategy/${id}`),
  },
  
  // Market
  market: {
    prices: (assets?: string) => fetchJson(`/market/prices${assets ? `?assets=${assets}` : ''}`),
    indicators: (asset: string, timeframe = '1h') => fetchJson(`/market/indicators?asset=${asset}&timeframe=${timeframe}`),
    macro: () => fetchJson('/market/macro'),
    ohlcv: (asset: string, timeframe = '1h', limit = 100) => 
      fetchJson(`/market/ohlcv?asset=${asset}&timeframe=${timeframe}&limit=${limit}`),
  },
  
  // Agent
  agent: {
    state: () => fetchJson('/agent/state'),
    loop: () => fetchJson('/agent/loop'),
    toolCalls: (limit = 20) => fetchJson(`/agent/toolcalls?limit=${limit}`),
    reasoning: () => fetchJson('/agent/reasoning'),
  },
  
  // Performance
  performance: {
    currentCycle: () => fetchJson('/performance/cycle'),
    lifetime: () => fetchJson('/performance/lifetime'),
    cycleHistory: (limit = 20) => fetchJson(`/performance/cycles?limit=${limit}`),
    latestEvaluation: () => fetchJson('/performance/evaluation'),
  },
};

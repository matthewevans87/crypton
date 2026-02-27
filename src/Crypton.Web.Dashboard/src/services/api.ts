const API_BASE = process.env.REACT_APP_API_BASE_URL || '/api';

const DEFAULT_TIMEOUT = 10000;
const MAX_RETRIES = 3;
const RETRY_DELAYS = [1000, 2000, 4000];

interface FetchOptions {
  timeout?: number;
  retries?: number;
  retryableStatuses?: number[];
}

class ApiError extends Error {
  constructor(
    message: string,
    public status?: number,
    public isRetryable: boolean = false
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

async function fetchWithTimeout(
  url: string,
  timeout: number,
  signal?: AbortSignal
): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = setTimeout(() => controller.abort(), timeout);

  try {
    const response = await fetch(url, { signal: signal ?? controller.signal });
    return response;
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') {
      throw new ApiError('Request timed out', undefined, true);
    }
    throw error;
  } finally {
    clearTimeout(timeoutId);
  }
}

async function fetchWithRetry<T>(
  url: string,
  options: FetchOptions = {}
): Promise<T> {
  const {
    timeout = DEFAULT_TIMEOUT,
    retries = MAX_RETRIES,
    retryableStatuses = [500, 502, 503, 504, 408, 429],
  } = options;

  let lastError: Error | undefined;

  for (let attempt = 0; attempt <= retries; attempt++) {
    try {
      const response = await fetchWithTimeout(url, timeout);

      if (!response.ok) {
        const isRetryable =
          retryableStatuses.includes(response.status) ||
          response.status >= 500;

        if (isRetryable && attempt < retries) {
          await new Promise((resolve) =>
            setTimeout(resolve, RETRY_DELAYS[attempt] || RETRY_DELAYS[RETRY_DELAYS.length - 1])
          );
          lastError = new ApiError(
            `API error: ${response.status} ${response.statusText}`,
            response.status,
            true
          );
          continue;
        }

        throw new ApiError(
          `API error: ${response.status} ${response.statusText}`,
          response.status,
          false
        );
      }

      return response.json();
    } catch (error) {
      if (error instanceof ApiError) {
        if (error.isRetryable && attempt < retries) {
          lastError = error;
          await new Promise((resolve) =>
            setTimeout(resolve, RETRY_DELAYS[attempt] || RETRY_DELAYS[RETRY_DELAYS.length - 1])
          );
          continue;
        }
        throw error;
      }

      if (attempt < retries) {
        lastError = error instanceof Error ? error : new Error(String(error));
        await new Promise((resolve) =>
          setTimeout(resolve, RETRY_DELAYS[attempt] || RETRY_DELAYS[RETRY_DELAYS.length - 1])
        );
        continue;
      }

      throw error;
    }
  }

  throw lastError || new Error('Max retries exceeded');
}

async function fetchJson<T>(url: string, options: FetchOptions = {}): Promise<T> {
  return fetchWithRetry<T>(`${API_BASE}${url}`, options);
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

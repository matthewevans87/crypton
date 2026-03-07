/**
 * Typed REST helper for direct backend API calls during E2E test setup and assertions.
 * Bypasses the UI for test setup, state verification, and teardown.
 */

export type ApiClientOptions = {
  monitoringDashboardUrl: string;
  agentRunnerUrl:         string;
  marketDataUrl:          string;
  executionServiceUrl:    string;
  apiKey:                 string;
};

export type AgentStatus = {
  currentState: string;
  isPaused: boolean;
  currentCycleId?: string;
  lastCompletedStep?: string | null;
  nextScheduledTime?: string | null;
  pauseReason?: string | null;
};

export type SystemStatus = {
  services: Array<{ name: string; healthy: boolean; url: string }>;
};

export type PriceTicker = {
  asset: string;
  price: number;
  bid: number;
  ask: number;
  high24h: number;
  low24h: number;
  changePercent24h: number;
  volume24h: number;
  lastUpdated: string;
};

export type PortfolioSummary = {
  totalValue: number;
  unrealizedPnL: number;
  availableCapital: number;
};

export class ApiClient {
  constructor(private readonly opts: ApiClientOptions) {}

  // ---------------------------------------------------------------------------
  // Private fetch helper
  // ---------------------------------------------------------------------------
  private async get<T>(baseUrl: string, path: string): Promise<T> {
    const url = `${baseUrl.replace(/\/$/, '')}${path}`;
    const res = await fetch(url, {
      headers: { 'X-Api-Key': this.opts.apiKey },
      signal: AbortSignal.timeout(10_000),
    });
    if (!res.ok) {
      throw new Error(`GET ${url} → ${res.status} ${res.statusText}`);
    }
    return res.json() as Promise<T>;
  }

  private async post<T>(baseUrl: string, path: string, body?: unknown): Promise<T> {
    const url = `${baseUrl.replace(/\/$/, '')}${path}`;
    const res = await fetch(url, {
      method: 'POST',
      headers: {
        'X-Api-Key': this.opts.apiKey,
        'Content-Type': 'application/json',
      },
      body: body !== undefined ? JSON.stringify(body) : undefined,
      signal: AbortSignal.timeout(10_000),
    });
    if (!res.ok) {
      throw new Error(`POST ${url} → ${res.status} ${res.statusText}`);
    }
    return res.json() as Promise<T>;
  }

  // ---------------------------------------------------------------------------
  // Agent Runner
  // ---------------------------------------------------------------------------
  async getAgentStatus(): Promise<AgentStatus> {
    return this.get<AgentStatus>(this.opts.agentRunnerUrl, '/api/status');
  }

  async pauseAgent(): Promise<void> {
    await this.post(this.opts.agentRunnerUrl, '/api/override/pause');
  }

  async resumeAgent(): Promise<void> {
    await this.post(this.opts.agentRunnerUrl, '/api/override/resume');
  }

  async forceCycle(): Promise<void> {
    await this.post(this.opts.agentRunnerUrl, '/api/override/force-cycle');
  }

  // ---------------------------------------------------------------------------
  // Market Data
  // ---------------------------------------------------------------------------
  async getPrice(symbol: string): Promise<PriceTicker> {
    return this.get<PriceTicker>(this.opts.marketDataUrl, `/api/prices/${encodeURIComponent(symbol)}`);
  }

  async getPrices(symbols: string[]): Promise<PriceTicker[]> {
    const q = symbols.map(s => `symbols=${encodeURIComponent(s)}`).join('&');
    return this.get<PriceTicker[]>(this.opts.marketDataUrl, `/api/prices?${q}`);
  }

  // ---------------------------------------------------------------------------
  // Monitoring Dashboard (aggregated)
  // ---------------------------------------------------------------------------
  async getPortfolioSummary(): Promise<PortfolioSummary> {
    return this.get<PortfolioSummary>(this.opts.monitoringDashboardUrl, '/api/portfolio/summary');
  }

  async getSystemStatus(): Promise<SystemStatus> {
    return this.get<SystemStatus>(this.opts.monitoringDashboardUrl, '/api/system/status');
  }

  // ---------------------------------------------------------------------------
  // Health checks
  // ---------------------------------------------------------------------------
  async isServiceHealthy(baseUrl: string): Promise<boolean> {
    try {
      const res = await fetch(`${baseUrl}/health/live`, {
        signal: AbortSignal.timeout(3000),
      });
      return res.ok;
    } catch {
      return false;
    }
  }

  async allServicesHealthy(): Promise<boolean> {
    const results = await Promise.all([
      this.isServiceHealthy(this.opts.marketDataUrl),
      this.isServiceHealthy(this.opts.executionServiceUrl),
      this.isServiceHealthy(this.opts.agentRunnerUrl),
      this.isServiceHealthy(this.opts.monitoringDashboardUrl),
    ]);
    return results.every(Boolean);
  }
}

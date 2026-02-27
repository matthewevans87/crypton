import { api } from './api';

export interface PollIntervals {
  positions: number;
  strategy: number;
  trades: number;
  marketData: number;
  agentState: number;
  loop: number;
}

const DEFAULT_POLL_INTERVALS: PollIntervals = {
  positions: 5000,
  strategy: 30000,
  trades: 10000,
  marketData: 3000,
  agentState: 2000,
  loop: 5000,
};

export const pollIntervals: PollIntervals = {
  positions: parseInt(process.env.REACT_APP_POLL_POSITIONS ?? '') || DEFAULT_POLL_INTERVALS.positions,
  strategy: parseInt(process.env.REACT_APP_POLL_STRATEGY ?? '') || DEFAULT_POLL_INTERVALS.strategy,
  trades: parseInt(process.env.REACT_APP_POLL_TRADES ?? '') || DEFAULT_POLL_INTERVALS.trades,
  marketData: parseInt(process.env.REACT_APP_POLL_MARKET ?? '') || DEFAULT_POLL_INTERVALS.marketData,
  agentState: parseInt(process.env.REACT_APP_POLL_AGENT ?? '') || DEFAULT_POLL_INTERVALS.agentState,
  loop: parseInt(process.env.REACT_APP_POLL_LOOP ?? '') || DEFAULT_POLL_INTERVALS.loop,
};

export const SMART_POLL_INTERVALS = {
  ACTIVE: {
    positions: 2000,
    strategy: 10000,
    trades: 5000,
    marketData: 1000,
    agentState: 1000,
    loop: 2000,
  },
  IDLE: {
    positions: 10000,
    strategy: 60000,
    trades: 30000,
    marketData: 10000,
    agentState: 10000,
    loop: 15000,
  },
};

interface PollerOptions {
  enabled?: boolean;
  interval?: number;
  onError?: (error: Error) => void;
}

class Poller {
  private intervals: Map<string, ReturnType<typeof setInterval>> = new Map();
  private isPollingEnabled = true;

  start(key: string, fetchFn: () => Promise<void>, options: PollerOptions = {}) {
    this.stop(key);

    if (!this.isPollingEnabled) return;

    const interval = options.interval ?? pollIntervals[key as keyof PollIntervals] ?? 5000;

    fetchFn().catch((error) => {
      options.onError?.(error);
    });

    const intervalId = setInterval(() => {
      if (this.isPollingEnabled) {
        fetchFn().catch((error) => {
          options.onError?.(error);
        });
      }
    }, interval);

    this.intervals.set(key, intervalId);
  }

  stop(key: string): void {
    const intervalId = this.intervals.get(key);
    if (intervalId) {
      clearInterval(intervalId);
      this.intervals.delete(key);
    }
  }

  stopAll(): void {
    this.intervals.forEach((id) => clearInterval(id));
    this.intervals.clear();
  }

  enable(): void {
    this.isPollingEnabled = true;
  }

  disable(): void {
    this.isPollingEnabled = false;
  }

  isEnabled(): boolean {
    return this.isPollingEnabled;
  }
}

export const poller = new Poller();

export function getSmartInterval(key: keyof PollIntervals, agentIsRunning: boolean): number {
  const intervals = agentIsRunning ? SMART_POLL_INTERVALS.ACTIVE : SMART_POLL_INTERVALS.IDLE;
  return intervals[key] ?? pollIntervals[key] ?? DEFAULT_POLL_INTERVALS[key];
}

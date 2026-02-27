export interface CacheEntry<T> {
  data: T;
  timestamp: number;
  ttl: number;
}

export interface CacheConfig {
  maxSize: number;
  defaultTTL: number;
}

const DEFAULT_MAX_SIZE = 10 * 1024 * 1024;
const DEFAULT_TTL = 30000;

class LRUCache<T> {
  private cache = new Map<string, CacheEntry<T>>();
  private totalSize = 0;
  private maxSize: number;
  private defaultTTL: number;

  constructor(config: Partial<CacheConfig> = {}) {
    this.maxSize = config.maxSize ?? DEFAULT_MAX_SIZE;
    this.defaultTTL = config.defaultTTL ?? DEFAULT_TTL;
  }

  private calculateSize(_key: string, value: T): number {
    try {
      return JSON.stringify(value).length;
    } catch {
      return 1000;
    }
  }

  get(key: string): T | null {
    const entry = this.cache.get(key);
    if (!entry) return null;

    const now = Date.now();
    if (now - entry.timestamp > entry.ttl) {
      this.delete(key);
      return null;
    }

    this.cache.delete(key);
    this.cache.set(key, entry);

    return entry.data;
  }

  set(key: string, data: T, ttl: number = this.defaultTTL): void {
    const size = this.calculateSize(key, data);

    while (this.totalSize + size > this.maxSize && this.cache.size > 0) {
      const firstKey = this.cache.keys().next().value;
      if (firstKey) {
        this.delete(firstKey);
      }
    }

    const entry: CacheEntry<T> = {
      data,
      timestamp: Date.now(),
      ttl,
    };

    this.cache.set(key, entry);
    this.totalSize += size;
  }

  delete(key: string): boolean {
    const entry = this.cache.get(key);
    if (!entry) return false;

    this.cache.delete(key);
    this.totalSize -= this.calculateSize(key, entry.data);
    return true;
  }

  clear(): void {
    this.cache.clear();
    this.totalSize = 0;
  }

  has(key: string): boolean {
    const entry = this.cache.get(key);
    if (!entry) return false;

    const now = Date.now();
    if (now - entry.timestamp > entry.ttl) {
      this.delete(key);
      return false;
    }

    return true;
  }

  size(): number {
    return this.cache.size;
  }
}

const positionCache = new LRUCache<unknown>({ maxSize: 5 * 1024 * 1024, defaultTTL: 5000 });
const strategyCache = new LRUCache<unknown>({ maxSize: 5 * 1024 * 1024, defaultTTL: 30000 });
const tradeCache = new LRUCache<unknown>({ maxSize: 5 * 1024 * 1024, defaultTTL: 60000 });
const marketCache = new LRUCache<unknown>({ maxSize: 5 * 1024 * 1024, defaultTTL: 3000 });
const agentCache = new LRUCache<unknown>({ maxSize: 2 * 1024 * 1024, defaultTTL: 2000 });

export const cache = {
  positions: positionCache,
  strategy: strategyCache,
  trades: tradeCache,
  market: marketCache,
  agent: agentCache,

  invalidate(key: string): void {
    positionCache.delete(key);
    strategyCache.delete(key);
    tradeCache.delete(key);
    marketCache.delete(key);
    agentCache.delete(key);
  },

  invalidateAll(): void {
    positionCache.clear();
    strategyCache.clear();
    tradeCache.clear();
    marketCache.clear();
    agentCache.clear();
  },
};

export function invalidateCacheOnMessage(type: string): void {
  switch (type) {
    case 'position':
      positionCache.clear();
      break;
    case 'strategy':
      strategyCache.clear();
      break;
    case 'trade':
      tradeCache.clear();
      break;
    case 'market':
      marketCache.clear();
      break;
    case 'agent':
      agentCache.clear();
      break;
    default:
      cache.invalidate(type);
  }
}

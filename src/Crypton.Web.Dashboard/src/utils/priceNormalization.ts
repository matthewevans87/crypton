export interface NormalizedPrice {
  value: number;
  decimals: number;
  symbol: string;
  formatted: string;
}

const CRYPTO_DECIMALS: Record<string, number> = {
  BTC: 8,
  ETH: 8,
  SOL: 8,
  XRP: 6,
  ADA: 6,
  DOGE: 8,
};

const FIAT_DECIMALS = 2;

export function normalizePrice(
  raw: string | number | null | undefined,
  symbol: string = 'USD'
): NormalizedPrice | null {
  if (raw === null || raw === undefined || raw === '') {
    return null;
  }

  const value = typeof raw === 'string' ? parseFloat(raw) : raw;

  if (isNaN(value) || value < 0) {
    return null;
  }

  const isCrypto = symbol in CRYPTO_DECIMALS;
  const decimals = isCrypto ? CRYPTO_DECIMALS[symbol] || 8 : FIAT_DECIMALS;

  const formatted = new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: symbol,
    minimumFractionDigits: decimals,
    maximumFractionDigits: decimals,
  }).format(value);

  return {
    value,
    decimals,
    symbol,
    formatted,
  };
}

export function formatPrice(value: number, symbol: string = 'USD'): string {
  const normalized = normalizePrice(value, symbol);
  return normalized?.formatted ?? '—';
}

export function formatCompact(value: number): string {
  if (value >= 1_000_000_000) {
    return `$${(value / 1_000_000_000).toFixed(2)}B`;
  }
  if (value >= 1_000_000) {
    return `$${(value / 1_000_000).toFixed(2)}M`;
  }
  if (value >= 1_000) {
    return `$${(value / 1_000).toFixed(2)}K`;
  }
  return `$${value.toFixed(2)}`;
}

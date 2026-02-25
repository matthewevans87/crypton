import { useDashboardStore } from '../../store/dashboard';

interface PriceTickerPanelProps {
  config?: Record<string, unknown>;
}

export function PriceTickerPanel({ config }: PriceTickerPanelProps) {
  const { market } = useDashboardStore();
  const asset = (config?.asset as string) || 'BTC/USD';
  
  const ticker = market.prices.find((p) => p.asset === asset);

  if (!ticker) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      minimumFractionDigits: price < 1 ? 4 : 2,
      maximumFractionDigits: price < 1 ? 4 : 2,
    }).format(price);
  };

  const formatPercent = (value: number) => {
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toFixed(2)}%`;
  };

  const isPositive = ticker.changePercent24h >= 0;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      {/* Asset and Price */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start' }}>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', color: 'var(--text-secondary)' }}>
          {ticker.asset}
        </span>
        <span
          style={{
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-lg)',
            fontWeight: 600,
            color: 'var(--text-primary)',
          }}
        >
          {formatPrice(ticker.price)}
        </span>
      </div>

      {/* Change */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
        <span
          style={{
            color: isPositive ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
          }}
        >
          {isPositive ? '▲' : '▼'} {formatPercent(ticker.changePercent24h)}
        </span>
        <span
          style={{
            color: isPositive ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-xs)',
          }}
        >
          {formatPrice(ticker.change24h)}
        </span>
      </div>

      {/* Bid/Ask */}
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', marginTop: 'var(--space-1)' }}>
        <span>Bid</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.bid)}</span>
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
        <span>Ask</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.ask)}</span>
      </div>

      {/* 24h Range */}
      <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
        <span>24h: </span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.low24h)}</span>
        <span> - </span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.high24h)}</span>
      </div>
    </div>
  );
}

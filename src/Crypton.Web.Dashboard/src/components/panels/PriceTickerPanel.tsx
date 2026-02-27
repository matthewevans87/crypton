import React, { useDashboardStore } from '../../store/dashboard';
import { useMemo, useRef, useEffect, useState } from 'react';

interface PriceTickerPanelProps {
  config?: Record<string, unknown>;
}

const formatPrice = (price: number): string => {
  return new Intl.NumberFormat('en-US', {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: price < 1 ? 4 : 2,
    maximumFractionDigits: price < 1 ? 4 : 2,
  }).format(price);
};

const formatPercent = (value: number): string => {
  const sign = value >= 0 ? '+' : '';
  return `${sign}${value.toFixed(2)}%`;
};

export const PriceTickerPanel = React.memo(function PriceTickerPanel({ config }: PriceTickerPanelProps) {
  const { market } = useDashboardStore();
  const asset = (config?.asset as string) || 'BTC/USD';
  const prevPriceRef = useRef<number | null>(null);
  const [flashClass, setFlashClass] = useState('');
  
  const ticker = useMemo(() => 
    market.prices.find((p) => p.asset === asset),
    [market.prices, asset]
  );

  useEffect(() => {
    if (ticker && prevPriceRef.current !== null && prevPriceRef.current !== ticker.price) {
      const direction = ticker.price > prevPriceRef.current ? 'up' : 'down';
      setFlashClass(direction === 'up' ? 'price-flash-up' : 'price-flash-down');
      const timer = setTimeout(() => setFlashClass(''), 200);
      return () => clearTimeout(timer);
    }
    if (ticker) {
      prevPriceRef.current = ticker.price;
    }
  }, [ticker?.price]);

  const formattedPrice = useMemo(() => 
    ticker ? formatPrice(ticker.price) : null,
    [ticker?.price]
  );

  const formattedChange = useMemo(() => 
    ticker ? formatPercent(ticker.changePercent24h) : null,
    [ticker?.changePercent24h]
  );

  const formattedChange24h = useMemo(() => 
    ticker ? formatPrice(ticker.change24h) : null,
    [ticker?.change24h]
  );

  const isPositive = ticker?.changePercent24h ?? 0 >= 0;

  if (!ticker || !formattedPrice) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  return (
    <div 
      className={flashClass}
      style={{ 
        display: 'flex', 
        flexDirection: 'column', 
        gap: 'var(--space-1)',
        transition: 'background-color 50ms ease-in',
      }}>
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
          {formattedPrice}
        </span>
      </div>

      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
        <span
          style={{
            color: isPositive ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
          }}
        >
          {isPositive ? '▲' : '▼'} {formattedChange}
        </span>
        <span
          style={{
            color: isPositive ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-xs)',
          }}
        >
          {formattedChange24h}
        </span>
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', marginTop: 'var(--space-1)' }}>
        <span>Bid</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.bid)}</span>
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
        <span>Ask</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.ask)}</span>
      </div>

      <div style={{ marginTop: 'var(--space-1)', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
        <span>24h: </span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.low24h)}</span>
        <span> - </span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatPrice(ticker.high24h)}</span>
      </div>
    </div>
  );
});

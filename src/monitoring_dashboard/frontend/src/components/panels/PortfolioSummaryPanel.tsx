import { useDashboardStore } from '../../store/dashboard';

export function PortfolioSummaryPanel() {
  const { portfolio } = useDashboardStore();
  const summary = portfolio.summary;

  if (!summary) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const formatCurrency = (value: number) => {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      minimumFractionDigits: 2,
      maximumFractionDigits: 2,
    }).format(value);
  };

  const formatPercent = (value: number) => {
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toFixed(2)}%`;
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      <div
        style={{
          fontSize: '24px',
          fontFamily: 'var(--font-mono)',
          fontWeight: 600,
          color: 'var(--text-primary)',
        }}
      >
        {formatCurrency(summary.totalValue)}
      </div>
      
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
        <span
          style={{
            color: summary.changePercent24h >= 0 ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
          }}
        >
          {summary.changePercent24h >= 0 ? '▲' : '▼'} {formatCurrency(summary.change24h)}
        </span>
        <span
          style={{
            color: summary.changePercent24h >= 0 ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
          }}
        >
          ({formatPercent(summary.changePercent24h)})
        </span>
      </div>
      
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
        <span>Unrealized P&L</span>
        <span
          style={{
            color: summary.unrealizedPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
          }}
        >
          {formatCurrency(summary.unrealizedPnL)}
        </span>
      </div>
      
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
        <span>Available</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatCurrency(summary.availableCapital)}</span>
      </div>
    </div>
  );
}

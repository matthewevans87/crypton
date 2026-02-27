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

  const formatPercent = (value?: number) => {
    if (value === undefined || value === null) return '—';
    const sign = value >= 0 ? '+' : '';
    return `${sign}${value.toFixed(2)}%`;
  };

  const changePercent = summary.changePercent24h ?? 0;
  const change24h = summary.change24h ?? 0;
  const unrealizedPnL = summary.unrealizedPnL ?? 0;
  const availableCapital = summary.availableCapital ?? 0;
  const totalValue = summary.totalValue ?? 0;

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
        {formatCurrency(totalValue)}
      </div>
      
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
        <span
          style={{
            color: changePercent >= 0 ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
          }}
        >
          {changePercent >= 0 ? '▲' : '▼'} {formatCurrency(change24h)}
        </span>
        <span
          style={{
            color: changePercent >= 0 ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
          }}
        >
          ({formatPercent(changePercent)})
        </span>
      </div>
      
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
        <span>Unrealized P&L</span>
        <span
          style={{
            color: unrealizedPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)',
            fontFamily: 'var(--font-mono)',
          }}
        >
          {formatCurrency(unrealizedPnL)}
        </span>
      </div>
      
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
        <span>Available</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{formatCurrency(availableCapital)}</span>
      </div>
    </div>
  );
}

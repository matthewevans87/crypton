import { useDashboardStore } from '../../store/dashboard';

export function HoldingsPanel() {
  const { portfolio } = useDashboardStore();
  const holdings = portfolio.holdings;

  if (holdings.length === 0) {
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

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      {holdings.map((holding) => (
        <div
          key={holding.asset}
          style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            padding: '4px 0',
            borderBottom: '1px solid var(--border-default)',
          }}
        >
          <div>
            <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 500 }}>{holding.asset}</span>
            <span style={{ color: 'var(--text-tertiary)', fontSize: 'var(--font-size-xs)', marginLeft: '8px' }}>
              {holding.quantity}
            </span>
          </div>
          <div style={{ textAlign: 'right' }}>
            <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
              {formatCurrency(holding.value)}
            </div>
            <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
              {holding.allocationPercent.toFixed(1)}%
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

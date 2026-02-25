import { useDashboardStore } from '../../store/dashboard';

export function OpenPositionsPanel() {
  const { portfolio } = useDashboardStore();
  const positions = portfolio.positions;

  if (positions.length === 0) {
    return <div style={{ color: 'var(--text-tertiary)' }}>No open positions</div>;
  }

  const formatPrice = (price: number) => {
    return new Intl.NumberFormat('en-US', {
      style: 'currency',
      currency: 'USD',
      minimumFractionDigits: price < 1 ? 4 : 2,
      maximumFractionDigits: price < 1 ? 4 : 2,
    }).format(price);
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      {positions.map((pos) => {
        const pnlColor = pos.unrealizedPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)';
        const isNearSL = pos.isNearStopLoss;
        const isNearTP = pos.isNearTakeProfit;
        
        return (
          <div
            key={pos.id}
            style={{
              padding: '6px',
              backgroundColor: isNearSL || isNearTP ? 'rgba(255, 170, 0, 0.1)' : 'transparent',
              border: isNearSL || isNearTP ? '1px solid var(--color-warning)' : '1px solid var(--border-default)',
              borderRadius: '2px',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
              <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
                <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600 }}>{pos.asset}</span>
                <span
                  style={{
                    fontSize: 'var(--font-size-xs)',
                    color: pos.direction === 'long' ? 'var(--color-profit)' : 'var(--color-loss)',
                    textTransform: 'uppercase',
                  }}
                >
                  {pos.direction}
                </span>
              </div>
              <span style={{ fontFamily: 'var(--font-mono)', color: pnlColor, fontSize: 'var(--font-size-sm)' }}>
                {pos.unrealizedPnL >= 0 ? '+' : ''}{pos.unrealizedPnLPercent.toFixed(2)}%
              </span>
            </div>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', marginTop: '4px' }}>
              <span>Entry: {formatPrice(pos.entryPrice)}</span>
              <span>Now: {formatPrice(pos.currentPrice)}</span>
            </div>
            {pos.stopLoss && (
              <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-loss)', marginTop: '2px' }}>
                SL: {formatPrice(pos.stopLoss)}
              </div>
            )}
          </div>
        );
      })}
    </div>
  );
}

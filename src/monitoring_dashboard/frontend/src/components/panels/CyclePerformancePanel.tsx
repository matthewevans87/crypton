import { useDashboardStore } from '../../store/dashboard';

export function CyclePerformancePanel() {
  const { performance } = useDashboardStore();
  const cycle = performance.currentCycle;

  if (!cycle) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const formatCurrency = (value: number) => {
    const sign = value >= 0 ? '+' : '';
    return `${sign}$${Math.abs(value).toFixed(2)}`;
  };

  const totalPnL = cycle.realizedPnL + cycle.unrealizedPnL;
  const pnlColor = totalPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      {/* Total P&L */}
      <div>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)', marginBottom: '2px' }}>
          Total P&L
        </div>
        <div
          style={{
            fontSize: '20px',
            fontFamily: 'var(--font-mono)',
            fontWeight: 600,
            color: pnlColor,
          }}
        >
          {formatCurrency(totalPnL)}
        </div>
      </div>

      {/* Breakdown */}
      <div style={{ display: 'flex', gap: 'var(--space-4)' }}>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Realized</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', color: cycle.realizedPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)' }}>
            {formatCurrency(cycle.realizedPnL)}
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Unrealized</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', color: cycle.unrealizedPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)' }}>
            {formatCurrency(cycle.unrealizedPnL)}
          </div>
        </div>
      </div>

      {/* Stats Grid */}
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-1)', marginTop: 'var(--space-1)' }}>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Win Rate</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {cycle.winRate.toFixed(1)}%
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Trades</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {cycle.winningTrades}/{cycle.totalTrades}
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Max DD</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            -{cycle.maxDrawdown.toFixed(1)}%
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Avg Win</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', color: 'var(--color-profit)' }}>
            {formatCurrency(cycle.avgWin)}
          </div>
        </div>
      </div>

      {/* Daily Loss Limit Status */}
      <div style={{ 
        marginTop: 'var(--space-1)', 
        padding: '4px 6px', 
        backgroundColor: cycle.dailyLossLimitBreached ? 'rgba(255, 68, 102, 0.1)' : 'rgba(0, 255, 200, 0.1)',
        borderRadius: '2px',
        fontSize: 'var(--font-size-xs)',
        color: cycle.dailyLossLimitBreached ? 'var(--color-loss)' : 'var(--color-profit)',
        textAlign: 'center',
      }}>
        {cycle.dailyLossLimitBreached ? '⚠ DAILY LIMIT BREACHED' : '✓ Daily Limit Active'}
      </div>
    </div>
  );
}

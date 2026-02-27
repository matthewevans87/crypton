import { useDashboardStore } from '../../store/dashboard';

export function CycleHistoryPanel() {
  const { performance } = useDashboardStore();
  const cycles = performance.cycles;
  
  if (!cycles || cycles.length === 0) {
    return <div style={{ color: 'var(--text-tertiary)' }}>No cycle history</div>;
  }
  
  const formatDate = (date: string | Date) => {
    const d = new Date(date);
    return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  };
  
  const formatPnL = (value: number) => {
    const sign = value >= 0 ? '+' : '';
    return `${sign}$${value.toFixed(2)}`;
  };
  
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      <div style={{ 
        display: 'grid', 
        gridTemplateColumns: '1fr 70px 40px', 
        gap: 'var(--space-1)',
        fontSize: 'var(--font-size-xs)',
        color: 'var(--text-tertiary)',
        paddingBottom: 'var(--space-1)',
        borderBottom: '1px solid var(--border-default)',
      }}>
        <span>Date</span>
        <span style={{ textAlign: 'right' }}>P&L</span>
        <span style={{ textAlign: 'right' }}>WR</span>
      </div>
      
      <div style={{ overflow: 'auto', maxHeight: '200px' }}>
        {cycles.slice(0, 20).map((cycle, index) => {
          const pnl = cycle.realizedPnL + cycle.unrealizedPnL;
          const pnlColor = pnl >= 0 ? 'var(--color-profit)' : 'var(--color-loss)';
          
          return (
            <div
              key={cycle.cycleId || index}
              style={{
                display: 'grid',
                gridTemplateColumns: '1fr 70px 40px',
                gap: 'var(--space-1)',
                padding: '4px 0',
                fontSize: 'var(--font-size-xs)',
                borderBottom: '1px solid var(--border-default)',
                cursor: 'pointer',
              }}
              onMouseEnter={(e) => {
                e.currentTarget.style.backgroundColor = 'var(--bg-panel-header)';
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.backgroundColor = 'transparent';
              }}
            >
              <span style={{ color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)' }}>
                {formatDate(cycle.startDate || new Date())}
              </span>
              <span style={{ color: pnlColor, fontFamily: 'var(--font-mono)', textAlign: 'right' }}>
                {formatPnL(pnl)}
              </span>
              <span style={{ color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)', textAlign: 'right' }}>
                {cycle.winRate?.toFixed(0) || 0}%
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
}

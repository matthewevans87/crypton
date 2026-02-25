import { useDashboardStore } from '../../store/dashboard';

export function DailyLossLimitPanel() {
  const { performance } = useDashboardStore();
  const cycle = performance.currentCycle;
  
  if (!cycle) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }
  
  const dailyLimit = 1000;
  const currentLoss = Math.abs(Math.min(0, cycle.realizedPnL));
  const percentUsed = (currentLoss / dailyLimit) * 100;
  const isBreached = cycle.dailyLossLimitBreached;
  const isWarning = percentUsed >= 80 && !isBreached;
  
  const statusColor = isBreached 
    ? 'var(--color-loss)' 
    : isWarning 
      ? 'var(--color-warning)' 
      : 'var(--color-profit)';
  
  const bgColor = isBreached 
    ? 'rgba(255, 68, 102, 0.1)' 
    : isWarning 
      ? 'rgba(255, 170, 0, 0.1)' 
      : 'rgba(0, 255, 200, 0.1)';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      <div
        style={{
          padding: 'var(--space-2)',
          backgroundColor: bgColor,
          borderRadius: '2px',
          textAlign: 'center',
        }}
      >
        <div
          style={{
            fontSize: 'var(--font-size-xs)',
            color: statusColor,
            textTransform: 'uppercase',
            letterSpacing: '0.5px',
            marginBottom: '4px',
          }}
        >
          {isBreached ? '⚠ BREACHED' : isWarning ? '⚠ WARNING' : '✓ ACTIVE'}
        </div>
        <div
          style={{
            fontSize: 'var(--font-size-lg)',
            fontFamily: 'var(--font-mono)',
            fontWeight: 600,
            color: statusColor,
          }}
        >
          ${currentLoss.toFixed(2)} / ${dailyLimit}
        </div>
      </div>
      
      <div>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', marginBottom: '2px' }}>
          Usage
        </div>
        <div
          style={{
            height: '6px',
            backgroundColor: 'var(--border-default)',
            borderRadius: '2px',
            overflow: 'hidden',
          }}
        >
          <div
            style={{
              height: '100%',
              width: `${Math.min(100, percentUsed)}%`,
              backgroundColor: statusColor,
              transition: 'width 300ms ease',
            }}
          />
        </div>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)', marginTop: '2px' }}>
          {percentUsed.toFixed(1)}% used
        </div>
      </div>
    </div>
  );
}

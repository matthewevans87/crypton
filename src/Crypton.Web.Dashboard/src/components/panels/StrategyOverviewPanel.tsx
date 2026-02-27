import { useDashboardStore } from '../../store/dashboard';
import { useReducedMotion } from '../../hooks/useReducedMotion';

export function StrategyOverviewPanel() {
  const { strategy } = useDashboardStore();
  const current = strategy.current?.overview;
  const reducedMotion = useReducedMotion();

  if (!current) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const formatTimeRemaining = (ms: number) => {
    const hours = Math.floor(ms / (1000 * 60 * 60));
    const minutes = Math.floor((ms % (1000 * 60 * 60)) / (1000 * 60));
    return `${hours}h ${minutes}m`;
  };

  const modeColor = current.mode === 'live' ? 'var(--color-loss)' : 'var(--color-info)';
  const postureColors: Record<string, string> = {
    aggressive: 'var(--color-warning)',
    moderate: 'var(--color-info)',
    defensive: 'var(--color-profit)',
    flat: 'var(--text-secondary)',
    exit_all: 'var(--color-loss)',
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      {/* Mode and Time Remaining */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
          <div
            style={{
              width: '8px',
              height: '8px',
              borderRadius: '50%',
              backgroundColor: 'var(--color-active)',
              animation: reducedMotion ? 'none' : 'pulse 2s infinite',
            }}
          />
          <span style={{ color: modeColor, fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', textTransform: 'uppercase' }}>
            {current.mode}
          </span>
        </div>
        <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          {formatTimeRemaining(new Date(current.validUntil).getTime() - Date.now())}
        </span>
      </div>

      {/* Posture */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>Posture:</span>
        <span
          style={{
            color: postureColors[current.posture] || 'var(--text-primary)',
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-sm)',
            textTransform: 'capitalize',
          }}
        >
          {current.posture}
        </span>
      </div>

      {/* Risk Parameters */}
      <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)', marginTop: 'var(--space-2)' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          <span>Max Drawdown</span>
          <span style={{ fontFamily: 'var(--font-mono)' }}>{current.maxDrawdown}%</span>
        </div>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          <span>Daily Loss Limit</span>
          <span style={{ fontFamily: 'var(--font-mono)' }}>{current.dailyLossLimit}%</span>
        </div>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          <span>Max Exposure</span>
          <span style={{ fontFamily: 'var(--font-mono)' }}>{current.maxExposure}%</span>
        </div>
      </div>

      {/* Position Rules Count */}
      {strategy.current?.positionRules && (
        <div style={{ marginTop: 'var(--space-2)', paddingTop: 'var(--space-2)', borderTop: '1px solid var(--border-default)' }}>
          <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
            {strategy.current.positionRules.length} position rules
          </span>
        </div>
      )}
    </div>
  );
}

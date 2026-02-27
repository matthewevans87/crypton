import { useDashboardStore } from '../../store/dashboard';
import { useReducedMotion } from '../../hooks/useReducedMotion';

export function AgentStatePanel() {
  const { agent } = useDashboardStore();
  const state = agent.loop?.agentState;
  const reducedMotion = useReducedMotion();

  if (!state) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const stateColor = state.isRunning ? 'var(--color-active)' : 'var(--color-idle)';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      {/* State and Agent */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
        <div
          style={{
            width: '8px',
            height: '8px',
            borderRadius: '50%',
            backgroundColor: stateColor,
            animation: reducedMotion || !state.isRunning ? 'none' : 'pulse 1.5s infinite',
          }}
        />
        <span
          style={{
            fontFamily: 'var(--font-mono)',
            fontSize: 'var(--font-size-lg)',
            fontWeight: 600,
            color: 'var(--text-primary)',
          }}
        >
          {state.currentState}
        </span>
      </div>

      {/* Active Agent */}
      {state.activeAgent && (
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          {state.activeAgent}
        </div>
      )}

      {/* Progress Bar */}
      <div style={{ marginTop: 'var(--space-1)' }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)', marginBottom: '4px' }}>
          <span>Progress</span>
          <span style={{ fontFamily: 'var(--font-mono)' }}>{state.progressPercent.toFixed(0)}%</span>
        </div>
        <div
          style={{
            height: '4px',
            backgroundColor: 'var(--border-default)',
            borderRadius: '2px',
            overflow: 'hidden',
          }}
        >
          <div
            style={{
              height: '100%',
              width: `${state.progressPercent}%`,
              backgroundColor: 'var(--color-info)',
              transition: 'width 0.3s ease',
            }}
          />
        </div>
      </div>

      {/* Stats */}
      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 'var(--space-2)', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
        <span>Tokens</span>
        <span style={{ fontFamily: 'var(--font-mono)' }}>{state.tokensUsed.toLocaleString()}</span>
      </div>

      {state.currentTool && (
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          <span>Tool</span>
          <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--color-info)' }}>{state.currentTool}</span>
        </div>
      )}

      {state.lastLatencyMs && (
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          <span>Latency</span>
          <span style={{ fontFamily: 'var(--font-mono)' }}>{state.lastLatencyMs}ms</span>
        </div>
      )}
    </div>
  );
}

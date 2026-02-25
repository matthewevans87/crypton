import { useDashboardStore } from '../../store/dashboard';

export function LoopStatePanel() {
  const { agent } = useDashboardStore();
  const loop = agent.loop;

  if (!loop) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const state = loop.agentState;
  const steps = ['Plan', 'Research', 'Analyze', 'Synthesize', 'Execute', 'Evaluate'];
  const currentStepIndex = steps.indexOf(state.currentState);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      {/* Timeline */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', position: 'relative' }}>
        <div
          style={{
            position: 'absolute',
            top: '50%',
            left: 0,
            right: 0,
            height: '2px',
            backgroundColor: 'var(--border-default)',
            transform: 'translateY(-50%)',
          }}
        />
        {steps.map((step, idx) => {
          const isCompleted = idx < currentStepIndex;
          const isCurrent = idx === currentStepIndex;
          
          return (
            <div
              key={step}
              style={{
                position: 'relative',
                zIndex: 1,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: '4px',
              }}
            >
              <div
                style={{
                  width: '12px',
                  height: '12px',
                  borderRadius: '50%',
                  backgroundColor: isCompleted ? 'var(--color-profit)' : isCurrent ? 'var(--color-active)' : 'var(--border-default)',
                  border: isCurrent ? '2px solid var(--color-active)' : 'none',
                  transition: 'all 0.3s ease',
                }}
              />
              <span
                style={{
                  fontSize: '9px',
                  color: isCurrent ? 'var(--text-primary)' : isCompleted ? 'var(--text-secondary)' : 'var(--text-tertiary)',
                  textTransform: 'uppercase',
                }}
              >
                {step.substring(0, 3)}
              </span>
            </div>
          );
        })}
      </div>

      {/* Current State Details */}
      <div style={{ marginTop: 'var(--space-2)' }}>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>Current Step</div>
        <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-lg)', fontWeight: 600 }}>
          {state.currentState}
        </div>
      </div>

      {/* Cycle Info */}
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)' }}>
        <div>
          <div style={{ color: 'var(--text-tertiary)' }}>Cycle</div>
          <div style={{ fontFamily: 'var(--font-mono)' }}>#{loop.cycleNumber}</div>
        </div>
        <div>
          <div style={{ color: 'var(--text-tertiary)' }}>Artifact</div>
          <div style={{ fontFamily: 'var(--font-mono)', color: 'var(--color-info)' }}>{loop.currentArtifact || '—'}</div>
        </div>
      </div>

      {/* Timestamps */}
      {loop.lastCycleCompletedAt && (
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
          Last cycle: {new Date(loop.lastCycleCompletedAt).toLocaleString()}
        </div>
      )}
      {loop.nextCycleExpectedAt && (
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
          Next cycle: {new Date(loop.nextCycleExpectedAt).toLocaleString()}
        </div>
      )}
    </div>
  );
}

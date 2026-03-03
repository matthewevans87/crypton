import { useDashboardStore } from '../../store/dashboard';

// Loop order: Evaluate (step 0, conditional) → Plan → Research → Analyze → Synthesize
// Evaluate is skipped on the first cycle when no previous cycle history exists.
const LOOP_STEPS = [
  { id: 'Evaluate', label: 'Evaluate', icon: '📝', conditional: true },
  { id: 'Plan', label: 'Plan', icon: '📋' },
  { id: 'Research', label: 'Research', icon: '🔍' },
  { id: 'Analyze', label: 'Analyze', icon: '📊' },
  { id: 'Synthesize', label: 'Synth', icon: '🎯' },
];

export function LoopTimelinePanel() {
  const { agent } = useDashboardStore();
  const loop = agent.loop;

  if (!loop) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const agentState = loop.agentState;
  const progress = agentState?.progressPercent || 0;
  const isWaiting = agentState?.currentState === 'WaitingForNextCycle';
  // Derive step index from currentState name rather than progressPercent alone
  const namedIndex = LOOP_STEPS.findIndex((s) => s.id === agentState?.currentState);
  const stepIndex = isWaiting
    ? LOOP_STEPS.length  // all complete
    : namedIndex >= 0
      ? namedIndex
      : Math.floor((progress / 100) * LOOP_STEPS.length);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          Cycle {loop.cycleNumber}
        </span>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-info)', fontFamily: 'var(--font-mono)' }}>
          {progress.toFixed(0)}%
        </span>
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
            width: `${progress}%`,
            backgroundColor: 'var(--color-info)',
            transition: 'width 300ms ease',
          }}
        />
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', marginTop: 'var(--space-1)' }}>
        {LOOP_STEPS.map((step, index) => {
          const isCompleted = index < stepIndex;
          const isCurrent = !isWaiting && index === stepIndex;
          const isDimmed = (step as { conditional?: boolean }).conditional && !isCompleted && !isCurrent;

          return (
            <div
              key={step.id}
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: '2px',
                opacity: isDimmed ? 0.45 : 1,
              }}
            >
              <div
                style={{
                  width: '20px',
                  height: '20px',
                  borderRadius: (step as { conditional?: boolean }).conditional ? '4px' : '50%',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: '10px',
                  backgroundColor: isCompleted
                    ? 'var(--color-profit)'
                    : isCurrent
                      ? 'var(--color-info)'
                      : 'var(--border-default)',
                  color: isCompleted || isCurrent ? 'var(--bg-viewport)' : 'var(--text-tertiary)',
                  border: isCurrent ? '2px solid var(--color-info)' : 'none',
                }}
              >
                {isCompleted ? '✓' : isCurrent ? '●' : step.icon}
              </div>
              <span style={{ fontSize: '8px', color: isCurrent ? 'var(--color-info)' : 'var(--text-tertiary)' }}>
                {step.label}
              </span>
            </div>
          );
        })}
      </div>

      {agentState?.currentState && (
        <div style={{
          marginTop: 'var(--space-2)',
          padding: 'var(--space-1)',
          backgroundColor: 'var(--bg-panel-header)',
          borderRadius: '2px',
          fontSize: 'var(--font-size-xs)',
          color: 'var(--text-secondary)',
          textAlign: 'center',
        }}>
          {isWaiting ? 'Waiting for next cycle' : `Current: ${agentState.currentState}`}
        </div>
      )}
    </div>
  );
}

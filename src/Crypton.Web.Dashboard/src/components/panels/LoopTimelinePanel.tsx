import { useDashboardStore } from '../../store/dashboard';

const LOOP_STEPS = [
  { id: 'planning', label: 'Planning', icon: '📋' },
  { id: 'research', label: 'Research', icon: '🔍' },
  { id: 'analysis', label: 'Analysis', icon: '📊' },
  { id: 'strategy', label: 'Strategy', icon: '🎯' },
  { id: 'execution', label: 'Execution', icon: '🚀' },
  { id: 'evaluation', label: 'Evaluation', icon: '📝' },
];

export function LoopTimelinePanel() {
  const { agent } = useDashboardStore();
  const loop = agent.loop;
  
  if (!loop) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }
  
  const agentState = loop.agentState;
  const progress = agentState?.progressPercent || 0;
  const stepIndex = Math.floor((progress / 100) * LOOP_STEPS.length);
  
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
          const isCurrent = index === stepIndex;
          
          return (
            <div
              key={step.id}
              style={{
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: '2px',
              }}
            >
              <div
                style={{
                  width: '20px',
                  height: '20px',
                  borderRadius: '50%',
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
                {isCompleted ? '✓' : isCurrent ? '●' : '○'}
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
          Current: {agentState.currentState}
        </div>
      )}
    </div>
  );
}

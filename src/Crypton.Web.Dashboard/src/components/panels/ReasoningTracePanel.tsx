import { useDashboardStore } from '../../store/dashboard';

export function ReasoningTracePanel() {
  const { agent } = useDashboardStore();
  const reasoning = agent.reasoning;

  if (reasoning.length === 0) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Waiting for agent...</div>;
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      {reasoning.map((step, idx) => (
        <div
          key={idx}
          style={{
            padding: '4px 0',
            borderBottom: '1px solid var(--border-default)',
            fontSize: 'var(--font-size-xs)',
            color: idx === reasoning.length - 1 ? 'var(--text-primary)' : 'var(--text-secondary)',
          }}
        >
          <span style={{ color: 'var(--text-tertiary)', marginRight: '8px', fontFamily: 'var(--font-mono)' }}>
            {new Date(step.timestamp).toLocaleTimeString()}
          </span>
          {step.content}
        </div>
      ))}
    </div>
  );
}

import { useDashboardStore } from '../../store/dashboard';

export function ToolCallsPanel() {
  const { agent, selectedToolCallId, setSelectedToolCall } = useDashboardStore();
  const toolCalls = agent.toolCalls;

  if (toolCalls.length === 0) {
    return <div style={{ color: 'var(--text-tertiary)' }}>No tool calls yet</div>;
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
      {toolCalls.slice(0, 10).map((call) => (
        <div
          key={call.id}
          onClick={() => setSelectedToolCall(call.id)}
          style={{
            padding: '4px 6px',
            backgroundColor: call.isError ? 'rgba(255, 68, 102, 0.1)' : 'transparent',
            border: selectedToolCallId === call.id 
              ? '1px solid var(--color-info)' 
              : '1px solid var(--border-default)',
            borderRadius: '2px',
            fontSize: 'var(--font-size-xs)',
            cursor: 'pointer',
          }}
        >
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--color-info)' }}>
              {call.toolName}
            </span>
            <span style={{ color: 'var(--text-tertiary)', fontSize: '10px' }}>
              {call.durationMs}ms
            </span>
          </div>
          <div style={{ color: 'var(--text-tertiary)', marginTop: '2px', fontSize: '10px', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {call.input.substring(0, 50)}...
          </div>
          {call.isCompleted && (
            <div style={{ color: 'var(--color-profit)', fontSize: '10px', marginTop: '2px' }}>
              ✓ completed
            </div>
          )}
          {call.isError && (
            <div style={{ color: 'var(--color-loss)', fontSize: '10px', marginTop: '2px' }}>
              ✗ {call.errorMessage}
            </div>
          )}
        </div>
      ))}
    </div>
  );
}

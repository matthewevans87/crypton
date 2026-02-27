import { useDashboardStore } from '../../store/dashboard';
import { formatTimestamp } from '../../utils/dateUtils';
import { CodeBlock } from '../CodeBlock';

export function ToolCallDetailPanel() {
  const { agent, selectedToolCallId, setSelectedToolCall } = useDashboardStore();
  const toolCall = agent.toolCalls.find(tc => tc.id === selectedToolCallId);

  if (!toolCall) {
    return (
      <div style={{
        color: 'var(--text-tertiary)',
        padding: 'var(--space-4)',
        textAlign: 'center'
      }}>
        Select a tool call to view details
      </div>
    );
  }

  const handleClose = () => setSelectedToolCall(null);

  return (
    <div style={{
      display: 'flex',
      flexDirection: 'column',
      height: '100%',
      overflow: 'hidden'
    }}>
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        padding: 'var(--space-2)',
        borderBottom: '1px solid var(--border-default)',
        backgroundColor: 'var(--bg-panel-header)',
      }}>
        <span style={{
          fontFamily: 'var(--font-mono)',
          color: 'var(--color-info)',
          fontWeight: 600
        }}>
          {toolCall.toolName}
        </span>
        <button
          onClick={handleClose}
          style={{
            background: 'none',
            border: 'none',
            color: 'var(--text-tertiary)',
            cursor: 'pointer',
            fontSize: '14px',
          }}
        >
          ✕
        </button>
      </div>

      <div style={{
        flex: 1,
        overflow: 'auto',
        padding: 'var(--space-2)',
        fontSize: 'var(--font-size-xs)'
      }}>
        <div style={{ marginBottom: 'var(--space-3)' }}>
          <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Status</div>
          {toolCall.isCompleted && (
            <span style={{ color: 'var(--color-profit)' }}>✓ Completed</span>
          )}
          {toolCall.isError && (
            <span style={{ color: 'var(--color-loss)' }}>✗ {toolCall.errorMessage}</span>
          )}
          {!toolCall.isCompleted && !toolCall.isError && (
            <span style={{ color: 'var(--color-warning)' }}>Running...</span>
          )}
        </div>

        <div style={{ marginBottom: 'var(--space-3)' }}>
          <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Duration</div>
          <span>{toolCall.durationMs}ms</span>
        </div>

        <div style={{ marginBottom: 'var(--space-3)' }}>
          <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Timestamp</div>
          <span>{formatTimestamp(toolCall.calledAt, 'realtime')}</span>
        </div>

        <div style={{ marginBottom: 'var(--space-3)' }}>
          <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Input</div>
          <CodeBlock code={toolCall.input || '(No input)'} maxHeight="200px" />
        </div>

        {toolCall.output && (
          <div style={{ marginBottom: 'var(--space-3)' }}>
            <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Output</div>
            <CodeBlock 
              code={typeof toolCall.output === 'string' ? toolCall.output : JSON.stringify(toolCall.output, null, 2)} 
              maxHeight="300px" 
            />
          </div>
        )}
      </div>
    </div>
  );
}

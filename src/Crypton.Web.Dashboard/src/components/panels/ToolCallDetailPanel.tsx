import { useDashboardStore } from '../../store/dashboard';
import { formatTimestamp } from '../../utils/dateUtils';

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
          <span>{formatTimestamp(toolCall.startedAt, 'realtime')}</span>
        </div>

        <div style={{ marginBottom: 'var(--space-3)' }}>
          <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Input</div>
          <div style={{
            backgroundColor: 'var(--bg-viewport)',
            padding: 'var(--space-2)',
            borderRadius: '2px',
            fontFamily: 'var(--font-mono)',
            fontSize: '11px',
            whiteSpace: 'pre-wrap',
            wordBreak: 'break-all',
            maxHeight: '200px',
            overflow: 'auto',
          }}>
            {toolCall.input || '(No input)'}
          </div>
        </div>

        {toolCall.output && (
          <div style={{ marginBottom: 'var(--space-3)' }}>
            <div style={{ color: 'var(--text-secondary)', marginBottom: '4px' }}>Output</div>
            <div style={{
              backgroundColor: 'var(--bg-viewport)',
              padding: 'var(--space-2)',
              borderRadius: '2px',
              fontFamily: 'var(--font-mono)',
              fontSize: '11px',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-all',
              maxHeight: '300px',
              overflow: 'auto',
            }}>
              {typeof toolCall.output === 'string' 
                ? toolCall.output 
                : JSON.stringify(toolCall.output, null, 2)}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

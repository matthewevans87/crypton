import { useDashboardStore } from '../../store/dashboard';

export function StatusBar() {
  const { portfolio, agent, toggleCommandPalette, connectionStatus } = useDashboardStore();
  
  const statusColor = connectionStatus === 'connected' 
    ? 'var(--color-active)' 
    : connectionStatus === 'connecting' 
      ? 'var(--color-warning)' 
      : 'var(--color-loss)';

  return (
    <div
      style={{
        height: '24px',
        backgroundColor: 'var(--bg-panel-header)',
        borderBottom: '1px solid var(--border-default)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '0 var(--space-3)',
        fontSize: 'var(--font-size-xs)',
        color: 'var(--text-secondary)',
        flexShrink: 0,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-4)' }}>
        {/* Connection Status */}
        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-1)' }}>
          <div
            style={{
              width: '6px',
              height: '6px',
              borderRadius: '50%',
              backgroundColor: statusColor,
            }}
          />
          <span>{connectionStatus === 'connected' ? 'Connected' : 'Disconnected'}</span>
        </div>
        
        {/* Mode Indicator */}
        {portfolio.summary && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-1)' }}>
            <span style={{ color: 'var(--text-tertiary)' }}>Mode:</span>
            <span style={{ 
              color: 'var(--color-info)',
              fontFamily: 'var(--font-mono)',
              textTransform: 'uppercase',
            }}>
              Paper
            </span>
          </div>
        )}
        
        {/* Agent State */}
        {agent.loop?.agentState && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-1)' }}>
            <span style={{ color: 'var(--text-tertiary)' }}>Agent:</span>
            <span style={{ 
              color: agent.loop.agentState.isRunning ? 'var(--color-active)' : 'var(--color-idle)',
              fontFamily: 'var(--font-mono)',
            }}>
              {agent.loop.agentState.currentState}
            </span>
          </div>
        )}
      </div>
      
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}>
        {/* Command Palette Hint */}
        <button
          onClick={toggleCommandPalette}
          style={{
            background: 'none',
            border: '1px solid var(--border-default)',
            borderRadius: '4px',
            padding: '2px 8px',
            color: 'var(--text-tertiary)',
            cursor: 'pointer',
            fontSize: 'var(--font-size-xs)',
            fontFamily: 'var(--font-mono)',
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--space-2)',
          }}
        >
          <span>⌘K</span>
          <span>Commands</span>
        </button>
        
        {/* Time */}
        <span className="mono" style={{ color: 'var(--text-tertiary)' }}>
          {new Date().toLocaleTimeString()}
        </span>
      </div>
    </div>
  );
}

import { useDashboardStore } from '../../store/dashboard';
import type { ServiceHealth } from '../../types';

// Short label shown in the status bar for each service
const SERVICE_ABBREV: Record<string, string> = {
  MarketData: 'MD',
  ExecutionService: 'ES',
  AgentRunner: 'AR',
};

function serviceStatusColor(status: ServiceHealth['status']): string {
  if (status === 'online') return 'var(--color-active)';
  if (status === 'degraded') return 'var(--color-warning)';
  return 'var(--color-loss)';
}

function ServiceChip({ svc }: { svc: ServiceHealth }) {
  const color = serviceStatusColor(svc.status);
  const label = SERVICE_ABBREV[svc.name] ?? svc.name;
  const tooltip = `${svc.name}: ${svc.detail}`;
  return (
    <div
      title={tooltip}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: '4px',
        cursor: 'default',
        padding: '1px 5px',
        borderRadius: '3px',
        border: `1px solid ${color}22`,
        backgroundColor: `${color}11`,
      }}
    >
      <div style={{ width: '5px', height: '5px', borderRadius: '50%', backgroundColor: color, flexShrink: 0 }} />
      <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--text-secondary)', letterSpacing: '0.02em' }}>
        {label}
      </span>
    </div>
  );
}

export function StatusBar() {
  const { portfolio, agent, toggleCommandPalette, connectionStatus, systemHealth } = useDashboardStore();

  const dashColor = connectionStatus === 'connected'
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
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}>
        {/* Dashboard SignalR connection */}
        <div
          title="MonitoringDashboard — SignalR connection"
          style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-1)', cursor: 'default' }}
        >
          <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: dashColor }} />
          <span style={{ color: 'var(--text-tertiary)' }}>Dashboard</span>
        </div>

        {/* Divider */}
        <div style={{ width: '1px', height: '12px', backgroundColor: 'var(--border-default)' }} />

        {/* Per-service chips — populated once first system/status poll returns */}
        {systemHealth && systemHealth.length > 0
          ? systemHealth.map((svc) => <ServiceChip key={svc.name} svc={svc} />)
          : (
            <span style={{ color: 'var(--text-tertiary)', fontStyle: 'italic' }}>checking services…</span>
          )
        }

        {/* Divider */}
        {(portfolio.summary || agent.loop) && (
          <div style={{ width: '1px', height: '12px', backgroundColor: 'var(--border-default)' }} />
        )}

        {/* Mode Indicator */}
        {portfolio.summary && (
          <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-1)' }}>
            <span style={{ color: 'var(--text-tertiary)' }}>Mode:</span>
            <span style={{ color: 'var(--color-info)', fontFamily: 'var(--font-mono)', textTransform: 'uppercase' }}>
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

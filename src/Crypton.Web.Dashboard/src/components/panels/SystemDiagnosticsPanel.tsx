import { useState } from 'react';
import { useDashboardStore } from '../../store/dashboard';
import { api } from '../../services/api';
import type { ServiceHealth, SystemStatus } from '../../types';

const STATUS_LABEL: Record<ServiceHealth['status'], string> = {
  online: 'Online',
  degraded: 'Degraded',
  offline: 'Offline',
};

const STATUS_COLOR: Record<ServiceHealth['status'], string> = {
  online: 'var(--color-active)',
  degraded: 'var(--color-warning)',
  offline: 'var(--color-loss)',
};

const SERVICE_DESCRIPTIONS: Record<string, string> = {
  MarketData: 'Exchange feed, price cache, technical indicators',
  ExecutionService: 'Order management, position tracking, strategy execution',
  AgentRunner: 'AI agent loop, cycle orchestration, artifact generation',
};

function formatMetricKey(key: string): string {
  return key
    .replace(/([A-Z])/g, ' $1')
    .replace(/^./, (c) => c.toUpperCase())
    .trim();
}

function formatMetricValue(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (Array.isArray(value)) {
    if (value.length === 0) return 'None';
    return value.map((v) => String(v)).join(', ');
  }
  return String(value);
}

/** A single service card showing status, detail, and key metrics. */
function ServiceCard({ svc }: { svc: ServiceHealth }) {
  const color = STATUS_COLOR[svc.status];
  const metricEntries = Object.entries(svc.metrics).filter(
    ([key, val]) => val !== null && val !== undefined && key !== 'alerts'
  );
  const alertList = Array.isArray(svc.metrics['alerts']) ? (svc.metrics['alerts'] as string[]) : [];

  const checkedAt = svc.checkedAt
    ? new Date(svc.checkedAt).toLocaleTimeString()
    : null;

  return (
    <div
      style={{
        border: `1px solid ${color}44`,
        borderRadius: '6px',
        overflow: 'hidden',
        backgroundColor: `${color}08`,
      }}
    >
      {/* Card header */}
      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          padding: '8px 12px',
          borderBottom: `1px solid ${color}22`,
          backgroundColor: `${color}12`,
        }}
      >
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          <div style={{ width: '8px', height: '8px', borderRadius: '50%', backgroundColor: color, flexShrink: 0 }} />
          <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600, color: 'var(--text-primary)' }}>
            {svc.name}
          </span>
          <span
            style={{
              fontSize: '10px',
              padding: '1px 6px',
              borderRadius: '3px',
              border: `1px solid ${color}66`,
              color,
              fontFamily: 'var(--font-mono)',
              textTransform: 'uppercase',
              letterSpacing: '0.05em',
            }}
          >
            {STATUS_LABEL[svc.status]}
          </span>
          {svc.signalRConnected !== null && svc.signalRConnected !== undefined && (
            <span
              title={svc.signalRConnected ? 'SignalR connected' : 'SignalR disconnected'}
              style={{
                fontSize: '10px',
                padding: '1px 5px',
                borderRadius: '3px',
                border: `1px solid ${svc.signalRConnected ? 'var(--color-active)' : 'var(--border-default)'}66`,
                color: svc.signalRConnected ? 'var(--color-active)' : 'var(--text-tertiary)',
                fontFamily: 'var(--font-mono)',
                cursor: 'default',
              }}
            >
              WS
            </span>
          )}
        </div>
        {checkedAt && (
          <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
            {checkedAt}
          </span>
        )}
      </div>

      {/* Description */}
      {SERVICE_DESCRIPTIONS[svc.name] && (
        <div style={{ padding: '4px 12px 0', color: 'var(--text-tertiary)', fontSize: '11px' }}>
          {SERVICE_DESCRIPTIONS[svc.name]}
        </div>
      )}

      {/* Detail row */}
      <div style={{ padding: '6px 12px', color: 'var(--text-secondary)', fontSize: 'var(--font-size-xs)' }}>
        {svc.detail}
      </div>

      {/* Alerts (if any) */}
      {alertList.length > 0 && (
        <div
          style={{
            margin: '0 12px 6px',
            padding: '6px 8px',
            borderRadius: '4px',
            backgroundColor: 'var(--color-warning)18',
            border: '1px solid var(--color-warning)44',
          }}
        >
          {alertList.map((alert, i) => (
            <div key={i} style={{ fontSize: '11px', color: 'var(--color-warning)', lineHeight: 1.4 }}>
              ⚠ {alert}
            </div>
          ))}
        </div>
      )}

      {/* Metrics grid */}
      {metricEntries.length > 0 && (
        <div
          style={{
            display: 'grid',
            gridTemplateColumns: '1fr 1fr',
            gap: '2px 16px',
            padding: '4px 12px 10px',
          }}
        >
          {metricEntries.map(([key, val]) => (
            <div key={key} style={{ display: 'flex', justifyContent: 'space-between', gap: '8px' }}>
              <span style={{ fontSize: '11px', color: 'var(--text-tertiary)', whiteSpace: 'nowrap' }}>
                {formatMetricKey(key)}
              </span>
              <span
                style={{
                  fontSize: '11px',
                  color: 'var(--text-secondary)',
                  fontFamily: 'var(--font-mono)',
                  textAlign: 'right',
                  overflow: 'hidden',
                  textOverflow: 'ellipsis',
                  whiteSpace: 'nowrap',
                  maxWidth: '120px',
                }}
                title={formatMetricValue(val)}
              >
                {formatMetricValue(val)}
              </span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export function SystemDiagnosticsPanel() {
  const { systemHealth, setSystemHealth } = useDashboardStore();
  const [refreshing, setRefreshing] = useState(false);
  const [lastRefresh, setLastRefresh] = useState<string | null>(null);

  const handleRefresh = async () => {
    setRefreshing(true);
    try {
      const result = await api.system.status() as SystemStatus;
      if (result?.services) {
        setSystemHealth(result.services);
        setLastRefresh(new Date().toLocaleTimeString());
      }
    } catch {
      // silently ignore — panels never crash on refresh errors
    } finally {
      setRefreshing(false);
    }
  };

  const overallStatus = systemHealth
    ? systemHealth.every((s) => s.status === 'online')
      ? 'All Systems Operational'
      : systemHealth.some((s) => s.status === 'offline')
        ? 'One or more services offline'
        : 'Degraded — some services need attention'
    : null;

  const overallColor = systemHealth
    ? systemHealth.every((s) => s.status === 'online')
      ? 'var(--color-active)'
      : systemHealth.some((s) => s.status === 'offline')
        ? 'var(--color-loss)'
        : 'var(--color-warning)'
    : 'var(--text-tertiary)';

  if (!systemHealth) {
    return (
      <div style={{ color: 'var(--text-tertiary)', padding: '8px', fontSize: 'var(--font-size-xs)' }}>
        Waiting for first system status poll…
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)', height: '100%', overflow: 'auto' }}>
      {/* Header row: overall status + refresh */}
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', flexShrink: 0 }}>
        <span style={{ fontSize: 'var(--font-size-xs)', color: overallColor, fontWeight: 600 }}>
          {overallStatus}
        </span>
        <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
          {lastRefresh && (
            <span style={{ fontSize: '11px', color: 'var(--text-tertiary)' }}>
              refreshed {lastRefresh}
            </span>
          )}
          <button
            onClick={handleRefresh}
            disabled={refreshing}
            style={{
              background: 'none',
              border: '1px solid var(--border-default)',
              borderRadius: '4px',
              padding: '2px 8px',
              color: refreshing ? 'var(--text-tertiary)' : 'var(--text-secondary)',
              cursor: refreshing ? 'default' : 'pointer',
              fontSize: '11px',
              fontFamily: 'var(--font-mono)',
            }}
          >
            {refreshing ? 'checking…' : '↻ Refresh'}
          </button>
        </div>
      </div>

      {/* Service cards */}
      {systemHealth.map((svc) => (
        <ServiceCard key={svc.name} svc={svc} />
      ))}
    </div>
  );
}

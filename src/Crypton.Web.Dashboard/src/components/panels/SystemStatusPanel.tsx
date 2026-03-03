import { useDashboardStore } from '../../store/dashboard';

const STATUS_COLOR: Record<string, string> = {
    online: 'var(--color-active)',
    degraded: 'var(--color-warning)',
    offline: 'var(--color-loss)',
};

const STATUS_LABEL: Record<string, string> = {
    online: 'Online',
    degraded: 'Degraded',
    offline: 'Offline',
};

export function SystemStatusPanel() {
    const { systemHealth } = useDashboardStore();

    if (!systemHealth) {
        return <div style={{ color: 'var(--text-tertiary)', fontSize: 'var(--font-size-xs)' }}>Waiting for status…</div>;
    }

    const allOnline = systemHealth.every((s) => s.status === 'online');
    const anyOffline = systemHealth.some((s) => s.status === 'offline');
    const overallColor = allOnline ? 'var(--color-active)' : anyOffline ? 'var(--color-loss)' : 'var(--color-warning)';
    const overallLabel = allOnline ? 'All Systems Operational' : anyOffline ? 'Service Outage' : 'Degraded';

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
            <div style={{
                padding: 'var(--space-1) var(--space-2)',
                borderRadius: '3px',
                backgroundColor: `${overallColor}18`,
                border: `1px solid ${overallColor}44`,
                fontSize: 'var(--font-size-xs)',
                color: overallColor,
                fontWeight: 600,
                textAlign: 'center',
            }}>
                {overallLabel}
            </div>

            {systemHealth.map((svc) => {
                const color = STATUS_COLOR[svc.status] ?? 'var(--text-tertiary)';
                const checkedAt = svc.checkedAt ? new Date(svc.checkedAt).toLocaleTimeString() : null;

                return (
                    <div
                        key={svc.name}
                        style={{
                            display: 'flex',
                            alignItems: 'center',
                            justifyContent: 'space-between',
                            padding: 'var(--space-1) var(--space-2)',
                            borderRadius: '3px',
                            border: `1px solid ${color}33`,
                            backgroundColor: `${color}08`,
                        }}
                    >
                        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
                            <div style={{
                                width: '8px',
                                height: '8px',
                                borderRadius: '50%',
                                backgroundColor: color,
                                flexShrink: 0,
                            }} />
                            <span style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', color: 'var(--text-primary)' }}>
                                {svc.name}
                            </span>
                        </div>
                        <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
                            <span style={{
                                fontSize: '11px',
                                color,
                                fontFamily: 'var(--font-mono)',
                            }}>
                                {STATUS_LABEL[svc.status] ?? svc.status}
                            </span>
                            {checkedAt && (
                                <span style={{ fontSize: '10px', color: 'var(--text-tertiary)' }}>{checkedAt}</span>
                            )}
                        </div>
                    </div>
                );
            })}
        </div>
    );
}

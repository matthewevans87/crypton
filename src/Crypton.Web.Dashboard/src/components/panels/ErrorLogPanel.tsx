import { useDashboardStore } from '../../store/dashboard';

interface LogEntry {
    service: string;
    message: string;
    level: 'error' | 'warning';
}

export function ErrorLogPanel() {
    const { systemHealth, agent } = useDashboardStore();

    const entries: LogEntry[] = [];

    // Collect alerts from all service health cards
    if (systemHealth) {
        for (const svc of systemHealth) {
            if (svc.status === 'offline') {
                entries.push({ service: svc.name, message: `Service offline — ${svc.detail || 'no detail'}`, level: 'error' });
            } else if (svc.status === 'degraded') {
                entries.push({ service: svc.name, message: `Service degraded — ${svc.detail || 'no detail'}`, level: 'warning' });
            }

            const alerts = Array.isArray(svc.metrics?.['alerts']) ? (svc.metrics['alerts'] as string[]) : [];
            for (const alert of alerts) {
                entries.push({ service: svc.name, message: alert, level: 'warning' });
            }
        }
    }

    // Collect failed tool calls from agent state
    for (const tc of agent.toolCalls) {
        if (tc.isError && tc.errorMessage) {
            entries.push({ service: 'AgentRunner', message: `${tc.toolName}: ${tc.errorMessage}`, level: 'error' });
        }
    }

    const levelColor = (level: 'error' | 'warning') =>
        level === 'error' ? 'var(--color-loss)' : 'var(--color-warning)';

    const levelIcon = (level: 'error' | 'warning') =>
        level === 'error' ? '✗' : '⚠';

    if (entries.length === 0) {
        return (
            <div style={{
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                gap: 'var(--space-2)',
                color: 'var(--color-active)',
                fontSize: 'var(--font-size-xs)',
                padding: 'var(--space-4)',
            }}>
                <span>✓</span>
                <span>No errors or warnings</span>
            </div>
        );
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', overflow: 'auto', maxHeight: '300px' }}>
            {entries.map((entry, i) => {
                const color = levelColor(entry.level);
                return (
                    <div
                        key={i}
                        style={{
                            display: 'flex',
                            gap: 'var(--space-2)',
                            padding: '4px 6px',
                            borderRadius: '2px',
                            backgroundColor: `${color}0e`,
                            border: `1px solid ${color}2a`,
                        }}
                    >
                        <span style={{ color, flexShrink: 0, fontSize: '11px' }}>{levelIcon(entry.level)}</span>
                        <div style={{ minWidth: 0 }}>
                            <span style={{
                                fontSize: '10px',
                                color: 'var(--text-tertiary)',
                                fontFamily: 'var(--font-mono)',
                                marginRight: '6px',
                            }}>
                                {entry.service}
                            </span>
                            <span style={{ fontSize: '11px', color: 'var(--text-secondary)', wordBreak: 'break-word' }}>
                                {entry.message}
                            </span>
                        </div>
                    </div>
                );
            })}
        </div>
    );
}

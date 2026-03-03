import { useDashboardStore } from '../../store/dashboard';

export function ConnectionHealthPanel() {
    const { systemHealth, connectionStatus } = useDashboardStore();

    const dashboardColor =
        connectionStatus === 'connected' ? 'var(--color-active)' :
            connectionStatus === 'connecting' ? 'var(--color-warning)' :
                'var(--color-loss)';

    const dashboardLabel =
        connectionStatus === 'connected' ? 'Connected' :
            connectionStatus === 'connecting' ? 'Connecting…' :
                'Disconnected';

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-1)' }}>
            {/* Column headers */}
            <div style={{
                display: 'grid',
                gridTemplateColumns: '1fr 60px 60px',
                gap: 'var(--space-1)',
                fontSize: '10px',
                color: 'var(--text-tertiary)',
                paddingBottom: 'var(--space-1)',
                borderBottom: '1px solid var(--border-default)',
                textTransform: 'uppercase',
                letterSpacing: '0.4px',
            }}>
                <span>Service</span>
                <span style={{ textAlign: 'center' }}>HTTP</span>
                <span style={{ textAlign: 'center' }}>WS</span>
            </div>

            {/* Dashboard row */}
            <ConnectionRow
                name="Dashboard"
                httpStatus="connected"
                wsColor={dashboardColor}
                wsLabel={dashboardLabel}
            />

            {/* Service rows */}
            {systemHealth ? systemHealth.map((svc) => {
                const httpOnline = svc.status !== 'offline';
                const wsConnected = svc.signalRConnected;

                return (
                    <ConnectionRow
                        key={svc.name}
                        name={svc.name}
                        httpStatus={httpOnline ? 'connected' : 'disconnected'}
                        wsColor={
                            wsConnected === true ? 'var(--color-active)' :
                                wsConnected === false ? 'var(--color-loss)' :
                                    'var(--text-tertiary)'
                        }
                        wsLabel={
                            wsConnected === true ? 'Connected' :
                                wsConnected === false ? 'Disconnected' :
                                    'N/A'
                        }
                    />
                );
            }) : (
                <div style={{ color: 'var(--text-tertiary)', fontSize: 'var(--font-size-xs)', padding: 'var(--space-2) 0' }}>
                    Waiting for service data…
                </div>
            )}
        </div>
    );
}

function ConnectionRow({
    name,
    httpStatus,
    wsColor,
    wsLabel,
}: {
    name: string;
    httpStatus: 'connected' | 'disconnected';
    wsColor: string;
    wsLabel: string;
}) {
    const httpColor = httpStatus === 'connected' ? 'var(--color-active)' : 'var(--color-loss)';

    return (
        <div style={{
            display: 'grid',
            gridTemplateColumns: '1fr 60px 60px',
            gap: 'var(--space-1)',
            alignItems: 'center',
            padding: '3px 0',
            fontSize: 'var(--font-size-xs)',
        }}>
            <span style={{ color: 'var(--text-secondary)', fontFamily: 'var(--font-mono)' }}>{name}</span>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '4px' }}>
                <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: httpColor }} />
                <span style={{ color: httpColor, fontSize: '10px' }}>
                    {httpStatus === 'connected' ? 'Up' : 'Down'}
                </span>
            </div>
            <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', gap: '4px' }}>
                <div style={{ width: '6px', height: '6px', borderRadius: '50%', backgroundColor: wsColor }} />
                <span style={{ color: wsColor, fontSize: '10px' }}>{wsLabel}</span>
            </div>
        </div>
    );
}

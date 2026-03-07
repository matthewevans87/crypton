import { useEffect, useRef, useState } from 'react';
import { rawFeedBus } from '../../services/signalr';
import { useDashboardStore } from '../../store/dashboard';

const MAX_MESSAGES = 10;
const GLOW_MS = 800;

interface FeedMessage {
    ts: number;
    raw: string;
}

interface SubState {
    messages: FeedMessage[];
    totalCount: number;
    glowing: boolean;
}

type FeedState = Record<string, SubState>;

// SignalR method names grouped by their conceptual service origin.
// All events flow through the single shared /hubs/dashboard connection.
const SERVICE_METHODS: Record<string, string[]> = {
    'ws-feed-marketdata': [
        'PriceUpdated',
    ],
    'ws-feed-execution': [
        'PortfolioUpdated',
        'PositionUpdated',
        'PositionClosed',
        'StrategyUpdated',
        'CycleCompleted',
        'EvaluationCompleted',
    ],
    'ws-feed-agentrunner': [
        'AgentStateChanged',
        'ToolCallStarted',
        'ToolCallCompleted',
        'ReasoningUpdated',
        'ErrorOccurred',
    ],
};

function initFeed(methods: string[]): FeedState {
    return Object.fromEntries(methods.map((m) => [m, { messages: [], totalCount: 0, glowing: false }]));
}

function WsFeedPanel({ panelType }: { panelType: string }) {
    const methods = SERVICE_METHODS[panelType] ?? SERVICE_METHODS['ws-feed-marketdata'];
    const connectionStatus = useDashboardStore((state) => state.connectionStatus);

    const [feed, setFeed] = useState<FeedState>(() => initFeed(methods));
    const [expanded, setExpanded] = useState<Record<string, boolean>>({});

    const glowTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});

    useEffect(() => {
        setFeed(initFeed(methods));
        setExpanded({});

        const listeners = methods.map((method) => {
            const cb = (data: unknown) => {
                let raw: string;
                try { raw = JSON.stringify(data); } catch { raw = String(data); }

                const msg: FeedMessage = { ts: Date.now(), raw };

                setFeed((prev) => {
                    const sub = prev[method];
                    if (!sub) return prev;
                    const messages = sub.messages.length >= MAX_MESSAGES
                        ? [...sub.messages.slice(1), msg]
                        : [...sub.messages, msg];
                    return { ...prev, [method]: { messages, totalCount: sub.totalCount + 1, glowing: true } };
                });

                if (glowTimers.current[method]) clearTimeout(glowTimers.current[method]);
                glowTimers.current[method] = setTimeout(() => {
                    setFeed((prev) => prev[method]
                        ? { ...prev, [method]: { ...prev[method], glowing: false } }
                        : prev
                    );
                }, GLOW_MS);
            };

            rawFeedBus.on(method, cb);
            return { method, cb };
        });

        return () => {
            Object.values(glowTimers.current).forEach(clearTimeout);
            listeners.forEach(({ method, cb }) => rawFeedBus.off(method, cb));
        };
        // eslint-disable-next-line react-hooks/exhaustive-deps
    }, [panelType]);

    const dotColor = connectionStatus === 'connected'
        ? 'var(--color-active)'
        : connectionStatus === 'connecting'
            ? 'var(--color-warning)'
            : 'var(--text-tertiary)';

    const statusLabel = connectionStatus === 'connected'
        ? 'Connected'
        : connectionStatus === 'connecting'
            ? 'Connecting…'
            : 'Disconnected';

    return (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', height: '100%', overflow: 'hidden' }}>
            {/* Connection status strip */}
            <div style={{ display: 'flex', alignItems: 'center', gap: '6px', flexShrink: 0, marginBottom: '2px' }}>
                <div style={{
                    width: '6px', height: '6px', borderRadius: '50%',
                    backgroundColor: dotColor,
                    flexShrink: 0,
                }} />
                <span style={{ fontSize: '10px', color: dotColor, fontFamily: 'var(--font-mono)' }}>
                    {statusLabel}
                </span>
                <span style={{
                    fontSize: '10px', color: 'var(--text-tertiary)',
                    fontFamily: 'var(--font-mono)', marginLeft: 'auto',
                }}>
                    /hubs/dashboard
                </span>
            </div>

            {/* Subscription rows */}
            <div style={{ overflow: 'auto', flex: 1, display: 'flex', flexDirection: 'column', gap: '2px' }}>
                {methods.map((method) => {
                    const sub = feed[method];
                    const isExpanded = expanded[method] ?? false;
                    const glowing = sub?.glowing ?? false;

                    return (
                        <div
                            key={method}
                            style={{
                                border: `1px solid ${glowing ? 'var(--color-active)' : 'var(--border-default)'}`,
                                borderRadius: '4px',
                                overflow: 'hidden',
                                transition: 'border-color 0.2s ease, box-shadow 0.2s ease',
                                boxShadow: glowing ? '0 0 8px color-mix(in srgb, var(--color-active) 30%, transparent)' : 'none',
                            }}
                        >
                            {/* Row header — click to expand */}
                            <div
                                onClick={() => setExpanded((prev) => ({ ...prev, [method]: !isExpanded }))}
                                style={{
                                    display: 'flex',
                                    alignItems: 'center',
                                    justifyContent: 'space-between',
                                    padding: '5px 8px',
                                    cursor: 'pointer',
                                    backgroundColor: glowing
                                        ? 'color-mix(in srgb, var(--color-active) 6%, var(--bg-panel-header))'
                                        : 'var(--bg-panel-header)',
                                    transition: 'background-color 0.2s ease',
                                    userSelect: 'none',
                                }}
                            >
                                <div style={{ display: 'flex', alignItems: 'center', gap: '6px' }}>
                                    {glowing && (
                                        <div style={{
                                            width: '5px', height: '5px', borderRadius: '50%',
                                            backgroundColor: 'var(--color-active)',
                                            flexShrink: 0,
                                        }} />
                                    )}
                                    <span style={{
                                        fontSize: '11px',
                                        fontFamily: 'var(--font-mono)',
                                        color: 'var(--text-primary)',
                                    }}>
                                        {method}
                                    </span>
                                </div>
                                <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
                                    <span style={{
                                        fontSize: '10px',
                                        color: 'var(--text-tertiary)',
                                        fontFamily: 'var(--font-mono)',
                                    }}>
                                        {sub?.totalCount ?? 0}
                                    </span>
                                    <span style={{ fontSize: '9px', color: 'var(--text-tertiary)' }}>
                                        {isExpanded ? '▲' : '▼'}
                                    </span>
                                </div>
                            </div>

                            {/* Message list — last 10, newest first */}
                            {isExpanded && (
                                <div style={{
                                    borderTop: '1px solid var(--border-subtle)',
                                    maxHeight: '220px',
                                    overflow: 'auto',
                                }}>
                                    {(!sub || sub.messages.length === 0) ? (
                                        <div style={{
                                            padding: '6px 8px',
                                            fontSize: '10px',
                                            color: 'var(--text-tertiary)',
                                            fontStyle: 'italic',
                                        }}>
                                            No messages yet
                                        </div>
                                    ) : (
                                        [...sub.messages].reverse().map((msg, i) => (
                                            <div
                                                key={`${msg.ts}-${i}`}
                                                style={{
                                                    padding: '4px 8px',
                                                    borderBottom: i < sub.messages.length - 1
                                                        ? '1px solid var(--border-subtle)'
                                                        : 'none',
                                                    display: 'flex',
                                                    gap: '8px',
                                                    alignItems: 'flex-start',
                                                }}
                                            >
                                                <span style={{
                                                    fontSize: '9px',
                                                    color: 'var(--text-tertiary)',
                                                    fontFamily: 'var(--font-mono)',
                                                    flexShrink: 0,
                                                    paddingTop: '1px',
                                                    minWidth: '62px',
                                                }}>
                                                    {new Date(msg.ts).toLocaleTimeString([], {
                                                        hour: '2-digit',
                                                        minute: '2-digit',
                                                        second: '2-digit',
                                                    })}
                                                </span>
                                                <span style={{
                                                    fontSize: '10px',
                                                    fontFamily: 'var(--font-mono)',
                                                    color: 'var(--text-secondary)',
                                                    wordBreak: 'break-all',
                                                    whiteSpace: 'pre-wrap',
                                                    lineHeight: '1.4',
                                                }}>
                                                    {msg.raw.length > 400
                                                        ? msg.raw.slice(0, 400) + '…'
                                                        : msg.raw}
                                                </span>
                                            </div>
                                        ))
                                    )}
                                </div>
                            )}
                        </div>
                    );
                })}
            </div>
        </div>
    );
}

export function WsFeedMarketDataPanel(_props: { config?: Record<string, unknown> }) {
    return <WsFeedPanel panelType="ws-feed-marketdata" />;
}

export function WsFeedExecutionPanel(_props: { config?: Record<string, unknown> }) {
    return <WsFeedPanel panelType="ws-feed-execution" />;
}

export function WsFeedAgentRunnerPanel(_props: { config?: Record<string, unknown> }) {
    return <WsFeedPanel panelType="ws-feed-agentrunner" />;
}



import { useEffect, useRef, useState } from 'react';
import { useDashboardStore } from '../../store/dashboard';
import { CodeBlock } from '../CodeBlock';
import type { ReasoningStep, ToolCall } from '../../types';

// ────── Timeline data model ──────

interface ReasoningBlock {
    kind: 'reasoning';
    text: string;
    startTs: string;
    tokenCount: number;
}

interface ToolBlock {
    kind: 'tool';
    call: ToolCall;
}

type TimelineBlock = ReasoningBlock | ToolBlock;

function buildTimeline(reasoning: ReasoningStep[], toolCalls: ToolCall[]): TimelineBlock[] {
    type Ev =
        | { ts: number; kind: 'r'; step: ReasoningStep }
        | { ts: number; kind: 't'; call: ToolCall };

    const events: Ev[] = [
        ...reasoning.map((s) => ({ ts: new Date(s.timestamp).getTime(), kind: 'r' as const, step: s })),
        ...toolCalls.map((c) => ({ ts: new Date(c.calledAt).getTime(), kind: 't' as const, call: c })),
    ].sort((a, b) => a.ts - b.ts);

    const blocks: TimelineBlock[] = [];
    let buf: ReasoningStep[] = [];

    const flush = () => {
        if (buf.length > 0) {
            blocks.push({
                kind: 'reasoning',
                text: buf.map((s) => s.content).join(''),
                startTs: buf[0].timestamp,
                tokenCount: buf.length,
            });
            buf = [];
        }
    };

    for (const ev of events) {
        if (ev.kind === 'r') {
            buf.push(ev.step);
        } else {
            flush();
            blocks.push({ kind: 'tool', call: ev.call });
        }
    }
    flush();

    return blocks;
}

// ────── Colors ──────

const ACTOR_COLOR = {
    agent: 'var(--color-info)',
    toolOk: 'var(--color-profit)',
    toolPending: 'var(--color-warning)',
    toolError: 'var(--color-loss)',
    reqLabel: 'var(--color-warning)',
    resLabel: 'var(--color-profit)',
};

function toolColor(call: ToolCall): string {
    if (call.isError) return ACTOR_COLOR.toolError;
    if (!call.isCompleted) return ACTOR_COLOR.toolPending;
    return ACTOR_COLOR.toolOk;
}

// ────── Helpers ──────

function ts(iso: string): string {
    return new Date(iso).toLocaleTimeString(undefined, { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

function tryPrettyJson(value: string): string {
    try {
        return JSON.stringify(JSON.parse(value), null, 2);
    } catch {
        return value;
    }
}

// ────── Gutter line ──────

function Gutter({ color, pulse }: { color: string; pulse?: boolean }) {
    return (
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', flexShrink: 0, width: '12px', paddingTop: '4px' }}>
            <div
                style={{
                    width: '6px',
                    height: '6px',
                    borderRadius: '50%',
                    backgroundColor: color,
                    flexShrink: 0,
                    ...(pulse ? { animation: 'pulse 1.5s infinite' } : {}),
                }}
            />
            <div style={{ flex: 1, width: '1px', backgroundColor: 'var(--border-default)', marginTop: '3px' }} />
        </div>
    );
}

// ────── Reasoning block ──────

function ReasoningBlock({ block, isStreaming }: { block: ReasoningBlock; isStreaming: boolean }) {
    return (
        <div style={{ display: 'flex', gap: '8px', padding: '4px 0' }}>
            <Gutter color={ACTOR_COLOR.agent} pulse={isStreaming} />
            <div style={{ flex: 1, paddingBottom: '8px', minWidth: 0 }}>
                <div style={{ display: 'flex', gap: '8px', alignItems: 'center', marginBottom: '3px' }}>
                    <span style={{ fontSize: '10px', color: ACTOR_COLOR.agent, fontFamily: 'var(--font-mono)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                        agent
                    </span>
                    <span style={{ fontSize: '10px', color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)' }}>
                        {ts(block.startTs)}
                    </span>
                    <span style={{ fontSize: '10px', color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)' }}>
                        {block.tokenCount} tok
                    </span>
                </div>
                <div
                    style={{
                        fontSize: '11px',
                        color: isStreaming ? 'var(--text-primary)' : 'var(--text-secondary)',
                        lineHeight: '1.55',
                        whiteSpace: 'pre-wrap',
                        wordBreak: 'break-word',
                    }}
                >
                    {block.text}
                    {isStreaming && (
                        <span
                            style={{
                                display: 'inline-block',
                                width: '5px',
                                height: '11px',
                                backgroundColor: ACTOR_COLOR.agent,
                                marginLeft: '1px',
                                verticalAlign: 'middle',
                                animation: 'pulse 1s infinite',
                                opacity: 0.85,
                            }}
                        />
                    )}
                </div>
            </div>
        </div>
    );
}

// ────── Tool call block ──────

function ToolBlock({ call, expanded, onToggle }: { call: ToolCall; expanded: boolean; onToggle: () => void }) {
    const color = toolColor(call);
    const statusLabel = call.isError ? '✗ error' : call.isCompleted ? `✓ ${call.durationMs}ms` : '…';

    return (
        <div style={{ display: 'flex', gap: '8px', padding: '4px 0' }}>
            <Gutter color={color} />
            <div style={{ flex: 1, paddingBottom: '8px', minWidth: 0 }}>
                {/* Clickable header row */}
                <div
                    onClick={onToggle}
                    role='button'
                    style={{
                        display: 'flex',
                        alignItems: 'center',
                        gap: '6px',
                        cursor: 'pointer',
                        padding: '3px 6px',
                        border: `1px solid ${color}33`,
                        backgroundColor: `${color}0a`,
                        userSelect: 'none',
                        borderRadius: '2px',
                    }}
                >
                    <span style={{ fontSize: '10px', color, fontFamily: 'var(--font-mono)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
                        tool
                    </span>
                    <span style={{ fontSize: '11px', color: 'var(--text-primary)', fontFamily: 'var(--font-mono)', fontWeight: 600, flex: 1, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                        {call.toolName}
                    </span>
                    <span style={{ fontSize: '10px', color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)' }}>
                        {ts(call.calledAt)}
                    </span>
                    <span style={{ fontSize: '10px', color, fontFamily: 'var(--font-mono)', whiteSpace: 'nowrap' }}>
                        {statusLabel}
                    </span>
                    <span style={{ fontSize: '9px', color: 'var(--text-tertiary)', flexShrink: 0 }}>
                        {expanded ? '▲' : '▼'}
                    </span>
                </div>

                {/* Expanded payload */}
                {expanded && (
                    <div style={{ marginTop: '6px', display: 'flex', flexDirection: 'column', gap: '6px' }}>
                        {/* Request */}
                        <div>
                            <div style={{ fontSize: '10px', color: ACTOR_COLOR.reqLabel, fontFamily: 'var(--font-mono)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '3px' }}>
                                request
                            </div>
                            <CodeBlock code={tryPrettyJson(call.input || '(empty)')} language='json' maxHeight='200px' />
                        </div>

                        {/* Error */}
                        {call.isError && (
                            <div style={{ padding: '6px 8px', border: '1px solid var(--color-loss)44', backgroundColor: 'var(--color-loss)0a', borderRadius: '2px' }}>
                                <div style={{ fontSize: '10px', color: 'var(--color-loss)', fontFamily: 'var(--font-mono)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '3px' }}>
                                    error
                                </div>
                                <div style={{ fontSize: '11px', color: 'var(--color-loss)', fontFamily: 'var(--font-mono)', whiteSpace: 'pre-wrap', wordBreak: 'break-all' }}>
                                    {call.errorMessage ?? 'Unknown error'}
                                </div>
                            </div>
                        )}

                        {/* Response */}
                        {!call.isError && call.isCompleted && call.output && (
                            <div>
                                <div style={{ fontSize: '10px', color: ACTOR_COLOR.resLabel, fontFamily: 'var(--font-mono)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: '3px' }}>
                                    response
                                </div>
                                <CodeBlock code={tryPrettyJson(call.output)} language='json' maxHeight='300px' />
                            </div>
                        )}

                        {/* In-flight */}
                        {!call.isError && !call.isCompleted && (
                            <div style={{ fontSize: '11px', color: 'var(--color-warning)', fontFamily: 'var(--font-mono)' }}>
                                awaiting response…
                            </div>
                        )}
                    </div>
                )}
            </div>
        </div>
    );
}

// ────── Panel ──────

export function AgentTracePanel() {
    const { agent } = useDashboardStore();
    const { reasoning, toolCalls, state } = agent;

    const [expanded, setExpanded] = useState<Set<string>>(new Set());
    const [autoScroll, setAutoScroll] = useState(true);
    const scrollRef = useRef<HTMLDivElement>(null);
    const prevLengthRef = useRef(0);

    const timeline = buildTimeline(reasoning, toolCalls);
    const isStreaming = !!state?.isRunning;
    const errorCount = toolCalls.filter((c) => c.isError).length;

    // Auto-scroll when new blocks arrive
    useEffect(() => {
        if (autoScroll && timeline.length !== prevLengthRef.current && scrollRef.current) {
            scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
        }
        prevLengthRef.current = timeline.length;
    }, [timeline.length, autoScroll]);

    const handleScroll = () => {
        if (!scrollRef.current) return;
        const { scrollTop, scrollHeight, clientHeight } = scrollRef.current;
        setAutoScroll(scrollHeight - scrollTop - clientHeight < 64);
    };

    const toggleTool = (id: string) => {
        setExpanded((prev) => {
            const next = new Set(prev);
            if (next.has(id)) next.delete(id);
            else next.add(id);
            return next;
        });
    };

    const scrollToBottom = () => {
        setAutoScroll(true);
        scrollRef.current?.scrollTo({ top: scrollRef.current.scrollHeight, behavior: 'smooth' });
    };

    if (timeline.length === 0) {
        return (
            <div style={{ color: 'var(--text-tertiary)', padding: '8px', fontSize: 'var(--font-size-xs)' }}>
                {isStreaming ? 'Waiting for agent output…' : 'No trace. Agent is idle.'}
            </div>
        );
    }

    return (
        <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
            {/* Summary bar */}
            <div
                style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '12px',
                    padding: '3px 8px',
                    borderBottom: '1px solid var(--border-default)',
                    flexShrink: 0,
                    fontSize: '10px',
                    fontFamily: 'var(--font-mono)',
                }}
            >
                <span style={{ color: ACTOR_COLOR.agent }}>{reasoning.length} tok</span>
                <span style={{ color: 'var(--text-tertiary)' }}>{toolCalls.length} tools</span>
                {errorCount > 0 && (
                    <span style={{ color: 'var(--color-loss)' }}>{errorCount} err</span>
                )}
                {!autoScroll && (
                    <button
                        onClick={scrollToBottom}
                        style={{
                            marginLeft: 'auto',
                            background: 'none',
                            border: '1px solid var(--border-default)',
                            borderRadius: '2px',
                            color: 'var(--text-secondary)',
                            cursor: 'pointer',
                            fontSize: '10px',
                            fontFamily: 'var(--font-mono)',
                            padding: '0 5px',
                            lineHeight: '16px',
                        }}
                    >
                        ↓ latest
                    </button>
                )}
            </div>

            {/* Timeline */}
            <div
                ref={scrollRef}
                onScroll={handleScroll}
                style={{ flex: 1, overflow: 'auto', padding: '4px 8px 8px' }}
            >
                {timeline.map((block, i) => {
                    const isLast = i === timeline.length - 1;
                    if (block.kind === 'reasoning') {
                        return (
                            <ReasoningBlock
                                key={`r-${i}`}
                                block={block}
                                isStreaming={isLast && isStreaming}
                            />
                        );
                    }
                    return (
                        <ToolBlock
                            key={block.call.id}
                            call={block.call}
                            expanded={expanded.has(block.call.id)}
                            onToggle={() => toggleTool(block.call.id)}
                        />
                    );
                })}
            </div>
        </div>
    );
}

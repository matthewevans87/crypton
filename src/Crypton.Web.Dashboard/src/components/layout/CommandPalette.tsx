import { useState, useEffect, useRef, useMemo } from 'react';
import { useDashboardStore, type PanelType, type PanelConfig } from '../../store/dashboard';
import { api } from '../../services/api';

interface Command {
  id: string;
  label: string;
  description?: string;
  category: 'recent' | 'panel' | 'navigation' | 'action' | 'agent';
  shortcut?: string;
  /** Normal action: fires and closes the palette. */
  action: () => void;
  /** Right-arrow action: fires without closing the palette (panel add commands only). */
  keepOpenAction?: () => void;
}

/** Returns true when every whitespace-delimited token in `query` appears somewhere in `text`. */
function matchesAllTokens(text: string, query: string): boolean {
  if (!query.trim()) return true;
  const lower = text.toLowerCase();
  return query.trim().toLowerCase().split(/\s+/).every(token => lower.includes(token));
}

/** Wraps matched token substrings in a highlighted span. */
function HighlightedLabel({ text, tokens }: { text: string; tokens: string[] }) {
  if (!tokens.length) return <>{text}</>;

  const lower = text.toLowerCase();
  const ranges: [number, number][] = [];

  for (const token of tokens) {
    const idx = lower.indexOf(token);
    if (idx !== -1) ranges.push([idx, idx + token.length]);
  }

  if (!ranges.length) return <>{text}</>;

  // Sort and merge overlapping ranges
  ranges.sort((a, b) => a[0] - b[0]);
  const merged: [number, number][] = [];
  for (const [s, e] of ranges) {
    if (merged.length && s <= merged[merged.length - 1][1]) {
      merged[merged.length - 1][1] = Math.max(merged[merged.length - 1][1], e);
    } else {
      merged.push([s, e]);
    }
  }

  const parts: React.ReactNode[] = [];
  let cursor = 0;
  for (const [start, end] of merged) {
    if (start > cursor) parts.push(text.slice(cursor, start));
    parts.push(
      <span key={start} style={{ color: 'var(--color-info)', fontWeight: 700 }}>
        {text.slice(start, end)}
      </span>
    );
    cursor = end;
  }
  if (cursor < text.length) parts.push(text.slice(cursor));

  return <>{parts}</>;
}

const PANEL_COMMANDS: { type: PanelType; label: string; description: string }[] = [
  // Agent Runner
  { type: 'agent-state', label: 'Agent: State', description: 'Current agent state, progress, and active tool' },
  { type: 'loop-state', label: 'Agent: Loop', description: 'Step timeline, cycle interval, next cycle time' },
  { type: 'reasoning-trace', label: 'Agent: Reasoning Trace', description: 'Live agent thinking stream' },
  { type: 'tool-calls', label: 'Agent: Tool Calls', description: 'List of tool invocations' },
  { type: 'tool-call-detail', label: 'Agent: Tool Call Detail', description: 'Input/output of selected tool' },
  { type: 'evaluation-rating', label: 'Agent: Evaluation Rating', description: 'A-F rating with trend from latest evaluation' },
  { type: 'ws-feed-agentrunner', label: 'Agent: WS Feed', description: 'Raw SignalR events from the agent runner service' },
  // Execution Service
  { type: 'portfolio-summary', label: 'Execution: Portfolio Summary', description: 'Total value, 24h change, P&L' },
  { type: 'holdings', label: 'Execution: Holdings', description: 'Asset holdings with allocation' },
  { type: 'open-positions', label: 'Execution: Open Positions', description: 'Current positions with stops/targets' },
  { type: 'strategy-overview', label: 'Execution: Strategy Overview', description: 'Mode, posture, validity, risk parameters' },
  { type: 'daily-loss-limit', label: 'Execution: Daily Loss Limit', description: 'Status bar for daily loss limit usage' },
  { type: 'cycle-performance', label: 'Execution: Cycle Performance', description: 'Current cycle P&L, win rate, evaluation rating' },
  { type: 'cycle-history', label: 'Execution: Cycle History', description: 'List of past cycles with summary stats' },
  { type: 'cycle-detail', label: 'Execution: Cycle Detail', description: 'Detailed view of a specific cycle' },
  { type: 'ws-feed-execution', label: 'Execution: WS Feed', description: 'Raw SignalR events from the execution service' },
  // Market Data
  { type: 'price-ticker', label: 'Market: Price Ticker', description: 'Live price with 24h change, bid/ask' },
  { type: 'price-chart', label: 'Market: Price Chart', description: 'Candlestick chart with OHLCV data' },
  { type: 'technical-indicators', label: 'Market: Technical Indicators', description: 'RSI, MACD, Bollinger Bands' },
  { type: 'ws-feed-marketdata', label: 'Market: WS Feed', description: 'Raw SignalR events from the market data service' },
  // System
  { type: 'system-status', label: 'System: Status', description: 'At-a-glance status for all services' },
  { type: 'system-diagnostics', label: 'System: Diagnostics', description: 'Live health, metrics, and alerts for all services' },
  { type: 'connection-health', label: 'System: Connection Health', description: 'HTTP and SignalR connectivity per service' },
  { type: 'error-log', label: 'System: Error Log', description: 'Aggregated errors and warnings from all services' },
];

export function CommandPalette() {
  const [query, setQuery] = useState('');
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  const { toggleCommandPalette, activeTabId, addPanel, tabs, recentCommands, addRecentCommand, resetToDefaults } = useDashboardStore();

  const commands: Command[] = useMemo(() => {
    return [
      {
        id: 'agent-force-cycle',
        label: 'Agent: Force New Cycle',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-force-cycle');
          toggleCommandPalette();
          api.agent.forceCycle().catch((e) => console.error('Force cycle failed:', e));
        },
      },
      {
        id: 'agent-start',
        label: 'Agent: Start Loop',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-start');
          toggleCommandPalette();
          api.agent.start().catch((e) => console.error('Start failed:', e));
        },
      },
      {
        id: 'agent-recover',
        label: 'Agent: Recover From Degraded',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-recover');
          toggleCommandPalette();
          api.agent.recover().catch((e) => console.error('Recover failed:', e));
        },
      },
      {
        id: 'agent-degrade',
        label: 'Agent: Enter Degraded Mode',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-degrade');
          toggleCommandPalette();
          api.agent.degrade('manual operator action').catch((e) => console.error('Degrade failed:', e));
        },
      },
      {
        id: 'agent-pause',
        label: 'Agent: Pause Loop',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-pause');
          toggleCommandPalette();
          api.agent.pause().catch((e) => console.error('Pause failed:', e));
        },
      },
      {
        id: 'agent-resume',
        label: 'Agent: Resume Loop',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-resume');
          toggleCommandPalette();
          api.agent.resume().catch((e) => console.error('Resume failed:', e));
        },
      },
      {
        id: 'agent-abort',
        label: 'Agent: Abort Current Cycle',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('agent-abort');
          toggleCommandPalette();
          api.agent.abort().catch((e) => console.error('Abort failed:', e));
        },
      },
      {
        id: 'execution-degrade',
        label: 'Execution: Enter Degraded Mode',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('execution-degrade');
          toggleCommandPalette();
          api.execution.degrade('manual operator action').catch((e) => console.error('Execution degrade failed:', e));
        },
      },
      {
        id: 'execution-recover',
        label: 'Execution: Recover From Degraded',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('execution-recover');
          toggleCommandPalette();
          api.execution.recover().catch((e) => console.error('Execution recover failed:', e));
        },
      },
      {
        id: 'execution-promote-live',
        label: 'Execution: Promote To Live',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('execution-promote-live');
          toggleCommandPalette();
          api.execution.promoteToLive('manual operator action').catch((e) => console.error('Promote to live failed:', e));
        },
      },
      {
        id: 'execution-demote-paper',
        label: 'Execution: Demote To Paper',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('execution-demote-paper');
          toggleCommandPalette();
          api.execution.demoteToPaper('manual operator action').catch((e) => console.error('Demote to paper failed:', e));
        },
      },
      {
        id: 'execution-reload-strategy',
        label: 'Execution: Reload Strategy',
        category: 'agent' as const,
        action: () => {
          addRecentCommand('execution-reload-strategy');
          toggleCommandPalette();
          api.execution.reloadStrategy().catch((e) => console.error('Reload strategy failed:', e));
        },
      },
      ...PANEL_COMMANDS.map((pc) => {
        const addPanelFn = () => {
          const panel: PanelConfig = { id: `${pc.type}-${Date.now()}`, type: pc.type };
          addPanel(activeTabId, panel);
          addRecentCommand(`add-${pc.type}`);
        };
        return {
          id: `add-${pc.type}`,
          label: pc.label,
          description: pc.description,
          category: 'panel' as const,
          action: () => { addPanelFn(); toggleCommandPalette(); },
          keepOpenAction: () => { addPanelFn(); },
        };
      }),
      ...tabs.map((tab) => ({
        id: `nav-${tab.id}`,
        label: `Switch to ${tab.title}`,
        category: 'navigation' as const,
        action: () => {
          useDashboardStore.getState().setActiveTab(tab.id);
          addRecentCommand(`nav-${tab.id}`);
          toggleCommandPalette();
        },
      })),
      {
        id: 'action-new-tab',
        label: 'Create New Tab',
        category: 'action',
        shortcut: '⌘T',
        action: () => {
          useDashboardStore.getState().addTab();
          addRecentCommand('action-new-tab');
          toggleCommandPalette();
        },
      },
      {
        id: 'action-refresh',
        label: 'Refresh All Data',
        category: 'action',
        action: () => {
          addRecentCommand('action-refresh');
          window.location.reload();
        },
      },
      {
        id: 'action-reset-layout',
        label: 'Reset Tabs & Panels to Defaults',
        description: 'Restore the default tab layout and remove custom panels',
        category: 'action',
        action: () => {
          resetToDefaults();
          toggleCommandPalette();
        },
      },
    ];
  }, [activeTabId, addPanel, tabs, addRecentCommand, toggleCommandPalette]);

  const recentCommandItems: Command[] = useMemo(() => {
    return recentCommands
      .map(id => commands.find(c => c.id === id))
      .filter((c): c is Command => c !== undefined);
  }, [recentCommands, commands]);

  const tokens = useMemo(
    () => query.trim().toLowerCase().split(/\s+/).filter(Boolean),
    [query]
  );

  const filteredCommands = useMemo(() => {
    if (!query.trim()) return commands;
    return commands.filter(c =>
      matchesAllTokens(`${c.label} ${c.description ?? ''} ${c.category}`, query)
    );
  }, [commands, query]);

  const displayCommands: Command[] = useMemo(() => {
    if (query.trim()) return filteredCommands;
    return [
      ...recentCommandItems.map(cmd => ({ ...cmd, category: 'recent' as const })),
      ...filteredCommands.filter(cmd => !recentCommands.includes(cmd.id)),
    ];
  }, [query, filteredCommands, recentCommandItems, recentCommands]);

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

  // Keep selected item scrolled into view
  useEffect(() => {
    const list = listRef.current;
    if (!list) return;
    const selected = list.querySelector('[data-selected="true"]') as HTMLElement | null;
    selected?.scrollIntoView({ block: 'nearest' });
  }, [selectedIndex]);

  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        toggleCommandPalette();
      } else if (e.key === 'ArrowDown') {
        e.preventDefault();
        setSelectedIndex((i) => Math.min(i + 1, displayCommands.length - 1));
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setSelectedIndex((i) => Math.max(i - 1, 0));
      } else if (e.key === 'Enter' && displayCommands[selectedIndex]) {
        displayCommands[selectedIndex].action();
      } else if (e.key === 'ArrowRight') {
        const input = inputRef.current;
        if (
          input &&
          input.selectionStart === input.value.length &&
          input.selectionEnd === input.value.length &&
          displayCommands[selectedIndex]
        ) {
          e.preventDefault();
          const cmd = displayCommands[selectedIndex];
          (cmd.keepOpenAction ?? cmd.action)();
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [displayCommands, selectedIndex, toggleCommandPalette]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);

  const groupedCommands = useMemo(() =>
    displayCommands.reduce((acc, cmd) => {
      const key = cmd.category;
      if (!acc[key]) acc[key] = [];
      acc[key].push(cmd);
      return acc;
    }, {} as Record<string, Command[]>),
    [displayCommands]
  );

  const CATEGORY_LABELS: Record<string, string> = {
    recent: 'Recent',
    agent: 'Agent Controls',
    panel: 'Panels',
    navigation: 'Navigation',
    action: 'Actions',
  };

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'rgba(0, 0, 0, 0.7)',
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        paddingTop: '15vh',
        zIndex: 1000,
      }}
      onClick={toggleCommandPalette}
    >
      <div
        style={{
          width: '540px',
          maxHeight: '60vh',
          backgroundColor: 'var(--bg-panel)',
          border: '1px solid var(--border-default)',
          borderRadius: '8px',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column',
        }}
        onClick={(e) => e.stopPropagation()}
      >
        <div style={{ padding: 'var(--space-3)', borderBottom: '1px solid var(--border-default)' }}>
          <input
            ref={inputRef}
            type="text"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="Type a command or panel name..."
            style={{
              width: '100%',
              backgroundColor: 'transparent',
              border: 'none',
              outline: 'none',
              fontSize: 'var(--font-size-lg)',
              color: 'var(--text-primary)',
              fontFamily: 'var(--font-sans)',
            }}
          />
        </div>

        <div ref={listRef} style={{ overflow: 'auto', flex: 1 }}>
          {Object.entries(groupedCommands).map(([category, cmds]) => (
            <div key={category}>
              <div
                style={{
                  padding: 'var(--space-2) var(--space-3)',
                  fontSize: 'var(--font-size-xs)',
                  color: 'var(--text-tertiary)',
                  textTransform: 'uppercase',
                  letterSpacing: '0.5px',
                  backgroundColor: 'var(--bg-panel-header)',
                }}
              >
                {CATEGORY_LABELS[category] ?? category}
              </div>
              {cmds.map((cmd) => {
                const index = displayCommands.indexOf(cmd);
                const isSelected = index === selectedIndex;
                return (
                  <div
                    key={cmd.id}
                    data-selected={isSelected}
                    onClick={cmd.action}
                    style={{
                      padding: 'var(--space-2) var(--space-3)',
                      cursor: 'pointer',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      backgroundColor: isSelected ? 'var(--border-default)' : 'transparent',
                      color: 'var(--text-primary)',
                      gap: 'var(--space-3)',
                    }}
                    onMouseEnter={() => setSelectedIndex(index)}
                  >
                    <div style={{ display: 'flex', flexDirection: 'column', gap: '2px', minWidth: 0 }}>
                      <span>
                        <HighlightedLabel text={cmd.label} tokens={tokens} />
                      </span>
                      {cmd.description && (
                        <span style={{
                          fontSize: 'var(--font-size-xs)',
                          color: 'var(--text-tertiary)',
                          overflow: 'hidden',
                          textOverflow: 'ellipsis',
                          whiteSpace: 'nowrap',
                        }}>
                          <HighlightedLabel text={cmd.description} tokens={tokens} />
                        </span>
                      )}
                    </div>
                    <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)', flexShrink: 0 }}>
                      {cmd.shortcut && (
                        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)' }}>
                          {cmd.shortcut}
                        </span>
                      )}
                      {cmd.keepOpenAction && isSelected && (
                        <span style={{
                          fontSize: '10px',
                          color: 'var(--text-tertiary)',
                          fontFamily: 'var(--font-mono)',
                          border: '1px solid var(--border-default)',
                          borderRadius: '3px',
                          padding: '0 4px',
                          lineHeight: '16px',
                        }}>
                          → add
                        </span>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>
          ))}

          {displayCommands.length === 0 && (
            <div style={{ padding: 'var(--space-4)', textAlign: 'center', color: 'var(--text-secondary)' }}>
              No commands found
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

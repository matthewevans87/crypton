import { useState, useEffect, useRef, useMemo } from 'react';
import { useDashboardStore, type PanelType, type PanelConfig } from '../../store/dashboard';

interface Command {
  id: string;
  label: string;
  category: 'recent' | 'panel' | 'navigation' | 'action';
  shortcut?: string;
  action: () => void;
}

const PANEL_COMMANDS: { type: PanelType; label: string; description: string }[] = [
  { type: 'portfolio-summary', label: 'Portfolio Summary', description: 'Total value, 24h change, P&L' },
  { type: 'holdings', label: 'Holdings', description: 'Asset holdings with allocation' },
  { type: 'open-positions', label: 'Open Positions', description: 'Current positions with stops/targets' },
  { type: 'trade-history', label: 'Trade History', description: 'Recent trades with P&L' },
  { type: 'strategy-overview', label: 'Strategy Overview', description: 'Mode, posture, validity' },
  { type: 'strategy-parameters', label: 'Strategy Parameters', description: 'Risk limits and constraints' },
  { type: 'strategy-rationale', label: 'Strategy Rationale', description: 'Human-readable strategy text' },
  { type: 'position-rules', label: 'Position Rules', description: 'Entry conditions and allocations' },
  { type: 'price-ticker', label: 'Price Ticker', description: 'Live price with 24h change' },
  { type: 'price-chart', label: 'Price Chart', description: 'Candlestick chart with indicators' },
  { type: 'technical-indicators', label: 'Technical Indicators', description: 'RSI, MACD, Bollinger Bands' },
  { type: 'macro-signals', label: 'Macro Signals', description: 'Trend, volatility, sentiment' },
  { type: 'agent-state', label: 'Agent State', description: 'Current agent and state' },
  { type: 'agent-activity', label: 'Agent Activity', description: 'Activity indicator and progress' },
  { type: 'reasoning-trace', label: 'Reasoning Trace', description: 'Live agent thinking stream' },
  { type: 'tool-calls', label: 'Tool Calls', description: 'List of tool invocations' },
  { type: 'tool-call-detail', label: 'Tool Call Detail', description: 'Input/output of selected tool' },
  { type: 'cycle-performance', label: 'Cycle Performance', description: 'Current cycle P&L and metrics' },
  { type: 'daily-loss-limit', label: 'Daily Loss Limit', description: 'Status bar for daily limit' },
  { type: 'lifetime-performance', label: 'Lifetime Performance', description: 'Total P&L and statistics' },
  { type: 'cycle-history', label: 'Cycle History', description: 'List of past cycles' },
  { type: 'loop-state', label: 'Loop State', description: 'Current step and progress' },
  { type: 'loop-timeline', label: 'Loop Timeline', description: 'Visual timeline of cycle' },
  { type: 'evaluation-rating', label: 'Evaluation Rating', description: 'A-F rating with trend' },
  { type: 'recommendations', label: 'Recommendations', description: 'Key points from evaluation' },
];

export function CommandPalette() {
  const [query, setQuery] = useState('');
  const [selectedIndex, setSelectedIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const { toggleCommandPalette, activeTabId, addPanel, tabs, recentCommands, addRecentCommand } = useDashboardStore();

  const commands: Command[] = useMemo(() => {
    return [
      ...PANEL_COMMANDS.map((pc) => ({
        id: `add-${pc.type}`,
        label: pc.label,
        category: 'panel' as const,
        action: () => {
          const panel: PanelConfig = {
            id: `${pc.type}-${Date.now()}`,
            type: pc.type,
          };
          addPanel(activeTabId, panel);
          addRecentCommand(`add-${pc.type}`);
          toggleCommandPalette();
        },
      })),
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
    ];
  }, [activeTabId, addPanel, tabs, addRecentCommand, toggleCommandPalette]);

  const recentCommandItems: Command[] = useMemo(() => {
    return recentCommands
      .map(id => commands.find(c => c.id === id))
      .filter((c): c is Command => c !== undefined);
  }, [recentCommands, commands]);

  const filteredCommands = query
    ? commands.filter(
        (c) =>
          c.label.toLowerCase().includes(query.toLowerCase()) ||
          c.category.toLowerCase().includes(query.toLowerCase())
      )
    : commands;

  const displayCommands = query
    ? filteredCommands
    : [
        ...recentCommandItems.map(cmd => ({ ...cmd, category: 'recent' as const })),
        ...filteredCommands.filter(cmd => !recentCommands.includes(cmd.id)),
      ];

  useEffect(() => {
    inputRef.current?.focus();
  }, []);

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
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [displayCommands, selectedIndex, toggleCommandPalette]);

  useEffect(() => {
    setSelectedIndex(0);
  }, [query]);

  const groupedCommands = displayCommands.reduce((acc, cmd) => {
    const categoryLabel = cmd.category === 'recent' ? 'recent' : cmd.category;
    if (!acc[categoryLabel]) acc[categoryLabel] = [];
    acc[categoryLabel].push(cmd);
    return acc;
  }, {} as Record<string, Command[]>);

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
          width: '500px',
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
            placeholder="Type a command..."
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
        
        <div style={{ overflow: 'auto', flex: 1 }}>
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
                {category === 'recent' ? 'Recent' : category}
              </div>
              {cmds.map((cmd) => {
                const index = displayCommands.indexOf(cmd);
                return (
                  <div
                    key={cmd.id}
                    onClick={cmd.action}
                    style={{
                      padding: 'var(--space-2) var(--space-3)',
                      cursor: 'pointer',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'space-between',
                      backgroundColor: index === selectedIndex ? 'var(--border-default)' : 'transparent',
                      color: 'var(--text-primary)',
                    }}
                    onMouseEnter={() => setSelectedIndex(index)}
                  >
                    <span>{cmd.label}</span>
                    {cmd.shortcut && (
                      <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', fontFamily: 'var(--font-mono)' }}>
                        {cmd.shortcut}
                      </span>
                    )}
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

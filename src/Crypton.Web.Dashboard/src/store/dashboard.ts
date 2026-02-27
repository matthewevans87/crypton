import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import type {
  PortfolioSummary,
  Holding,
  Position,
  Trade,
  Strategy,
  StrategyHistoryItem,
  PriceTicker,
  TechnicalIndicator,
  MacroSignals,
  AgentState,
  LoopStatus,
  ToolCall,
  ReasoningStep,
  EvaluationSummary,
  CyclePerformance,
  LifetimePerformance,
} from '../types';

export interface Tab {
  id: string;
  title: string;
  panels: PanelConfig[];
}

export interface PanelConfig {
  id: string;
  type: PanelType;
  config?: Record<string, unknown>;
  x?: number;
  y?: number;
  width?: number;
  height?: number;
  collapsed?: boolean;
}

export type PanelType = 
  | 'portfolio-summary'
  | 'holdings'
  | 'open-positions'
  | 'trade-history'
  | 'strategy-overview'
  | 'strategy-parameters'
  | 'strategy-rationale'
  | 'position-rules'
  | 'price-ticker'
  | 'price-chart'
  | 'technical-indicators'
  | 'macro-signals'
  | 'agent-state'
  | 'agent-activity'
  | 'reasoning-trace'
  | 'tool-calls'
  | 'tool-call-detail'
  | 'cycle-performance'
  | 'daily-loss-limit'
  | 'lifetime-performance'
  | 'cycle-history'
  | 'cycle-detail'
  | 'loop-state'
  | 'loop-timeline'
  | 'last-cycle-summary'
  | 'evaluation-rating'
  | 'recommendations'
  | 'system-status'
  | 'connection-health'
  | 'error-log';

interface DashboardState {
  // UI State
  tabs: Tab[];
  activeTabId: string;
  maximizedPanelId: string | null;
  commandPaletteOpen: boolean;
  recentCommands: string[];
  panelGlows: Record<string, { type: 'info' | 'warning' | 'error' | 'success'; message: string }>;
  
  // Data State
  portfolio: {
    summary: PortfolioSummary | null;
    holdings: Holding[];
    positions: Position[];
    trades: Trade[];
  };
  strategy: {
    current: Strategy | null;
    history: StrategyHistoryItem[];
  };
  market: {
    prices: PriceTicker[];
    indicators: Record<string, TechnicalIndicator[]>;
    macro: MacroSignals | null;
  };
  agent: {
    state: AgentState | null;
    loop: LoopStatus | null;
    toolCalls: ToolCall[];
    reasoning: ReasoningStep[];
  };
  performance: {
    currentCycle: CyclePerformance | null;
    lifetime: LifetimePerformance | null;
    cycles: CyclePerformance[];
    evaluation: EvaluationSummary | null;
  };
  
  // Actions
  addTab: (title?: string) => void;
  removeTab: (id: string) => void;
  setActiveTab: (id: string) => void;
  addPanel: (tabId: string, panel: PanelConfig) => void;
  removePanel: (tabId: string, panelId: string) => void;
  updatePanelPosition: (tabId: string, panelId: string, x: number, y: number) => void;
  updatePanelSize: (tabId: string, panelId: string, width: number, height: number) => void;
  updatePanelConfig: (tabId: string, panelId: string, config: Record<string, unknown>) => void;
  reorderPanels: (tabId: string, fromIndex: number, toIndex: number) => void;
  setMaximizedPanel: (panelId: string | null) => void;
  togglePanelCollapse: (tabId: string, panelId: string) => void;
  toggleCommandPalette: () => void;
  addRecentCommand: (commandId: string) => void;
  setPanelGlow: (panelId: string, type: 'info' | 'warning' | 'error' | 'success', message: string, duration?: number) => void;
  clearPanelGlow: (panelId: string) => void;
  
  setPortfolioData: (data: Partial<DashboardState['portfolio']>) => void;
  setStrategyData: (data: Partial<DashboardState['strategy']>) => void;
  setMarketData: (data: Partial<DashboardState['market']>) => void;
  setAgentData: (data: Partial<DashboardState['agent']>) => void;
  setPerformanceData: (data: Partial<DashboardState['performance']>) => void;
}

const defaultTabs: Tab[] = [
  {
    id: 'main',
    title: 'Main',
    panels: [
      { id: 'portfolio-summary', type: 'portfolio-summary' },
      { id: 'strategy-overview', type: 'strategy-overview' },
      { id: 'agent-state', type: 'agent-state' },
      { id: 'cycle-performance', type: 'cycle-performance' },
      { id: 'price-ticker-btc', type: 'price-ticker', config: { asset: 'BTC/USD' } },
      { id: 'price-ticker-eth', type: 'price-ticker', config: { asset: 'ETH/USD' } },
    ],
  },
  {
    id: 'analysis',
    title: 'Analysis',
    panels: [
      { id: 'loop-state', type: 'loop-state' },
      { id: 'reasoning-trace', type: 'reasoning-trace' },
      { id: 'tool-calls', type: 'tool-calls' },
      { id: 'technical-indicators', type: 'technical-indicators', config: { asset: 'BTC/USD' } },
      { id: 'open-positions', type: 'open-positions' },
      { id: 'holdings', type: 'holdings' },
    ],
  },
];

export const useDashboardStore = create<DashboardState>()(
  persist(
    (set, get) => ({
      // Initial UI State
      tabs: defaultTabs,
      activeTabId: 'main',
      maximizedPanelId: null,
      commandPaletteOpen: false,
      recentCommands: [],
      panelGlows: {},
      
      // Initial Data State
      portfolio: {
        summary: null,
        holdings: [],
        positions: [],
        trades: [],
      },
      strategy: {
        current: null,
        history: [],
      },
      market: {
        prices: [],
        indicators: {},
        macro: null,
      },
      agent: {
        state: null,
        loop: null,
        toolCalls: [],
        reasoning: [],
      },
      performance: {
        currentCycle: null,
        lifetime: null,
        cycles: [],
        evaluation: null,
      },
      
      // UI Actions
      addTab: (title = 'New Tab') => {
        const id = `tab-${Date.now()}`;
        set((state) => ({
          tabs: [...state.tabs, { id, title, panels: [] }],
          activeTabId: id,
        }));
      },
      
      removeTab: (id) => {
        const state = get();
        if (state.tabs.length === 1) return;
        
        const newTabs = state.tabs.filter((t) => t.id !== id);
        const newActiveId = state.activeTabId === id 
          ? newTabs[0]?.id 
          : state.activeTabId;
        
        set({ tabs: newTabs, activeTabId: newActiveId });
      },
      
      setActiveTab: (id) => set({ activeTabId: id }),
      
      addPanel: (tabId, panel) => {
        set((state) => ({
          tabs: state.tabs.map((t) => 
            t.id === tabId 
              ? { ...t, panels: [...t.panels, panel] }
              : t
          ),
        }));
      },
      
      removePanel: (tabId, panelId) => {
        set((state) => ({
          tabs: state.tabs.map((t) => 
            t.id === tabId 
              ? { ...t, panels: t.panels.filter((p) => p.id !== panelId) }
              : t
          ),
        }));
      },
      
      updatePanelPosition: (tabId, panelId, x, y) => {
        set((state) => ({
          tabs: state.tabs.map((t) => 
            t.id === tabId 
              ? { 
                  ...t, 
                  panels: t.panels.map((p) => 
                    p.id === panelId ? { ...p, x, y } : p
                  ) 
                }
              : t
          ),
        }));
      },
      
      updatePanelSize: (tabId, panelId, width, height) => {
        set((state) => ({
          tabs: state.tabs.map((t) => 
            t.id === tabId 
              ? { 
                  ...t, 
                  panels: t.panels.map((p) => 
                    p.id === panelId ? { ...p, width, height } : p
                  ) 
                }
              : t
          ),
        }));
      },
      
      updatePanelConfig: (tabId, panelId, config) => {
        set((state) => ({
          tabs: state.tabs.map((t) => 
            t.id === tabId 
              ? { 
                  ...t, 
                  panels: t.panels.map((p) => 
                    p.id === panelId ? { ...p, config: { ...p.config, ...config } } : p
                  ) 
                }
              : t
          ),
        }));
      },
      
      reorderPanels: (tabId, fromIndex, toIndex) => {
        set((state) => ({
          tabs: state.tabs.map((t) => {
            if (t.id !== tabId) return t;
            const panels = [...t.panels];
            const [removed] = panels.splice(fromIndex, 1);
            panels.splice(toIndex, 0, removed);
            return { ...t, panels };
          }),
        }));
      },
      
      togglePanelCollapse: (tabId, panelId) => {
        set((state) => ({
          tabs: state.tabs.map((t) => 
            t.id === tabId 
              ? { 
                  ...t, 
                  panels: t.panels.map((p) => 
                    p.id === panelId ? { ...p, collapsed: !p.collapsed } : p
                  ) 
                }
              : t
          ),
        }));
      },
      
      setMaximizedPanel: (panelId) => set({ maximizedPanelId: panelId }),
      
      toggleCommandPalette: () => set((state) => ({ 
        commandPaletteOpen: !state.commandPaletteOpen 
      })),
      
      addRecentCommand: (commandId) => set((state) => {
        const filtered = state.recentCommands.filter(id => id !== commandId);
        const newRecent = [commandId, ...filtered].slice(0, 10);
        return { recentCommands: newRecent };
      }),
      
      setPanelGlow: (panelId, type, message, duration = 3000) => {
        set((state) => ({
          panelGlows: {
            ...state.panelGlows,
            [panelId]: { type, message },
          },
        }));
        setTimeout(() => {
          set((state) => {
            const { [panelId]: _, ...rest } = state.panelGlows;
            return { panelGlows: rest };
          });
        }, duration);
      },
      
      clearPanelGlow: (panelId) => set((state) => {
        const { [panelId]: _, ...rest } = state.panelGlows;
        return { panelGlows: rest };
      }),
      
      // Data Actions
      setPortfolioData: (data) => set((state) => ({
        portfolio: { ...state.portfolio, ...data },
      })),
      
      setStrategyData: (data) => set((state) => ({
        strategy: { ...state.strategy, ...data },
      })),
      
      setMarketData: (data) => set((state) => ({
        market: { ...state.market, ...data },
      })),
      
      setAgentData: (data) => set((state) => ({
        agent: { ...state.agent, ...data },
      })),
      
      setPerformanceData: (data) => set((state) => ({
        performance: { ...state.performance, ...data },
      })),
    }),
    {
      name: 'crypton-dashboard-storage',
      partialize: (state) => ({
        tabs: state.tabs,
        activeTabId: state.activeTabId,
        recentCommands: state.recentCommands,
      }),
    }
  )
);

import { useEffect } from 'react';
import { useDashboardStore } from './store/dashboard';
import { api } from './services/api';
import { signalRService } from './services/signalr';
import { poller, getSmartInterval } from './services/polling';
import { TabBar } from './components/layout/TabBar';
import { CommandPalette } from './components/layout/CommandPalette';
import { StatusBar } from './components/layout/StatusBar';
import { PanelGrid } from './components/panels/PanelGrid';
import { ErrorToast } from './components/ErrorToast';
import type { PortfolioSummary, Strategy, PriceTicker, LoopStatus, CyclePerformance, Holding, Position, Trade, ToolCall, ReasoningStep, EvaluationSummary } from './types';

function App() {
  const {
    tabs,
    activeTabId,
    setPortfolioData,
    setStrategyData,
    setMarketData,
    setAgentData,
    setPerformanceData,
    commandPaletteOpen,
    toggleCommandPalette,
    setActiveTab,
    removeTab,
    addTab,
    maximizedPanelId,
    setMaximizedPanel,
    setConnectionStatus,
  } = useDashboardStore();

  const market = useDashboardStore((state) => state.market);
  const agent = useDashboardStore((state) => state.agent);

  const startPolling = () => {
    const agentIsRunning = agent.loop?.agentState?.isRunning ?? false;

    poller.start('marketData', async () => {
      try {
        const prices = await api.market.prices() as PriceTicker[];
        setMarketData({ prices });
      } catch (error) {
        console.error('Polling error (market):', error);
      }
    }, { interval: getSmartInterval('marketData', agentIsRunning) });

    poller.start('positions', async () => {
      try {
        const positions = await api.portfolio.positions() as Position[];
        setPortfolioData({ positions });
      } catch (error) {
        console.error('Polling error (positions):', error);
      }
    }, { interval: getSmartInterval('positions', agentIsRunning) });

    poller.start('strategy', async () => {
      try {
        const strategy = await api.strategy.current() as Strategy;
        setStrategyData({ current: strategy });
      } catch (error) {
        console.error('Polling error (strategy):', error);
      }
    }, { interval: getSmartInterval('strategy', agentIsRunning) });
  };

  useEffect(() => {
    const loadData = async () => {
      try {
        const summary = await api.portfolio.summary() as PortfolioSummary;
        const holdings = await api.portfolio.holdings() as Holding[];
        const positions = await api.portfolio.positions() as Position[];
        const trades = await api.portfolio.trades() as Trade[];
        const strategy = await api.strategy.current() as Strategy;
        const prices = await api.market.prices() as PriceTicker[];
        const loop = await api.agent.loop() as LoopStatus;
        const toolCalls = await api.agent.toolCalls() as ToolCall[];
        const reasoning = await api.agent.reasoning() as ReasoningStep[];
        const cyclePerformance = await api.performance.currentCycle() as CyclePerformance;
        const evaluation = await api.performance.latestEvaluation() as EvaluationSummary;

        setPortfolioData({ summary, holdings, positions, trades });
        setStrategyData({ current: strategy });
        setMarketData({ prices });
        setAgentData({ loop, toolCalls, reasoning });
        setPerformanceData({ currentCycle: cyclePerformance, evaluation });
      } catch (error) {
        console.error('Failed to load data:', error);
        poller.enable();
        startPolling();
      }
    };

    loadData();

    signalRService.subscribe({
      onPriceUpdated: (ticker: PriceTicker) => {
        const currentPrices = market.prices || [];
        const existingIndex = currentPrices.findIndex(p => p.asset === ticker.asset);
        if (existingIndex >= 0) {
          const updatedPrices = [...currentPrices];
          updatedPrices[existingIndex] = ticker;
          setMarketData({ prices: updatedPrices });
        } else {
          setMarketData({ prices: [...currentPrices, ticker] });
        }
      },
      onPortfolioUpdated: (summary: PortfolioSummary) => {
        setPortfolioData({ summary });
      },
      onAgentStateChanged: (state) => {
        setAgentData({ state });
      },
      onStrategyUpdated: (strategy) => {
        setStrategyData({ current: strategy });
      },
      onCycleCompleted: (cycle) => {
        setPerformanceData({ currentCycle: cycle });
      },
      onConnected: () => {
        setConnectionStatus('connected');
        poller.disable();
      },
      onDisconnected: () => {
        setConnectionStatus('disconnected');
        poller.enable();
        startPolling();
      },
    });

    signalRService.connect();

    return () => {
      signalRService.unsubscribe();
      signalRService.disconnect();
      poller.stopAll();
    };
  }, []);

  // Keyboard shortcuts
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      const isMod = e.metaKey || e.ctrlKey;

      // Cmd+K or Ctrl+K - Command Palette
      if (isMod && e.key === 'k') {
        e.preventDefault();
        toggleCommandPalette();
      }

      // Cmd+1-8 - Switch to tab 1-8
      if (isMod && e.key >= '1' && e.key <= '8') {
        e.preventDefault();
        const index = parseInt(e.key) - 1;
        if (tabs[index]) {
          setActiveTab(tabs[index].id);
        }
      }

      // Cmd+W - Close current tab
      if (isMod && e.key === 'w') {
        e.preventDefault();
        if (tabs.length > 1) {
          removeTab(activeTabId);
        }
      }

      // Cmd+T - New tab
      if (isMod && e.key === 't') {
        e.preventDefault();
        addTab();
      }

      // Cmd+S - Force refresh all data
      if (isMod && e.key === 's') {
        e.preventDefault();
        window.location.reload();
      }

      // ESC - Restore maximized panel or close command palette
      if (e.key === 'Escape') {
        if (commandPaletteOpen) {
          toggleCommandPalette();
        } else if (maximizedPanelId) {
          setMaximizedPanel(null);
        }
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [tabs, activeTabId, commandPaletteOpen, maximizedPanelId, toggleCommandPalette, setActiveTab, removeTab, addTab, setMaximizedPanel]);

  const activeTab = tabs.find((t) => t.id === activeTabId);

  return (
    <div style={{
      height: '100vh',
      width: '100vw',
      display: 'flex',
      flexDirection: 'column',
      backgroundColor: 'var(--bg-viewport)',
      overflow: 'hidden',
    }}>
      <TabBar />
      <StatusBar />
      {activeTab && <PanelGrid panels={activeTab.panels} />}
      {commandPaletteOpen && <CommandPalette />}
      <ErrorToast />
    </div>
  );
}

export default App;

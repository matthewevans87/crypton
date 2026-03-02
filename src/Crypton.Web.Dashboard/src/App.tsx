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

  const offlineLoop: LoopStatus = {
    agentState: { currentState: 'Offline', isRunning: false, stateStartedAt: '', timeInState: 0, progressPercent: 0, tokensUsed: 0 },
    cycleNumber: 0,
  };

  useEffect(() => {
    const loadData = async () => {
      const [
        summaryResult, holdingsResult, positionsResult, tradesResult,
        strategyResult, pricesResult, loopResult, toolCallsResult,
        reasoningResult, cyclePerformanceResult, evaluationResult,
      ] = await Promise.allSettled([
        api.portfolio.summary(),
        api.portfolio.holdings(),
        api.portfolio.positions(),
        api.portfolio.trades(),
        api.strategy.current(),
        api.market.prices(),
        api.agent.loop(),
        api.agent.toolCalls(),
        api.agent.reasoning(),
        api.performance.currentCycle(),
        api.performance.latestEvaluation(),
      ]);

      function ok<T,>(r: PromiseSettledResult<unknown>): T | null {
        return r.status === 'fulfilled' ? (r.value as T) : null;
      }

      setPortfolioData({
        summary: ok<PortfolioSummary>(summaryResult),
        holdings: ok<Holding[]>(holdingsResult) ?? [],
        positions: ok<Position[]>(positionsResult) ?? [],
        trades: ok<Trade[]>(tradesResult) ?? [],
      });
      setStrategyData({ current: ok<Strategy>(strategyResult) });
      setMarketData({ prices: ok<PriceTicker[]>(pricesResult) ?? [] });
      setAgentData({
        loop: loopResult.status === 'fulfilled' ? (loopResult.value as LoopStatus) : offlineLoop,
        toolCalls: ok<ToolCall[]>(toolCallsResult) ?? [],
        reasoning: ok<ReasoningStep[]>(reasoningResult) ?? [],
      });
      setPerformanceData({
        currentCycle: ok<CyclePerformance>(cyclePerformanceResult),
        evaluation: ok<EvaluationSummary>(evaluationResult),
      });

      // Log partial failures for debugging
      [summaryResult, holdingsResult, positionsResult, tradesResult, strategyResult,
        pricesResult, loopResult, toolCallsResult, reasoningResult, cyclePerformanceResult, evaluationResult]
        .filter((r) => r.status === 'rejected')
        .forEach((r) => console.warn('Initial load partial failure:', (r as PromiseRejectedResult).reason));

      // Register pollers as fallback; SignalR onConnected will disable them when live
      poller.enable();
      startPolling();
    };

    loadData();

    signalRService.subscribe({
      onPriceUpdated: (ticker: PriceTicker) => {
        const currentPrices = useDashboardStore.getState().market.prices || [];
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

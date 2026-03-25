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
import type { PortfolioSummary, Strategy, PriceTicker, LoopStatus, CyclePerformance, Holding, Position, Trade, ToolCall, ReasoningStep, EvaluationSummary, SystemStatus } from './types';

function App() {
  const {
    tabs,
    activeTabId,
    setPortfolioData,
    setStrategyData,
    setMarketData,
    setAgentData,
    setPerformanceData,
    setSystemHealth,
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

    poller.start('systemHealth', async () => {
      try {
        const result = await api.system.status() as SystemStatus;
        if (result?.services) setSystemHealth(result.services);
      } catch (error) {
        console.warn('Polling error (systemHealth):', error);
      }
    }, { interval: 15_000 });
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
        systemStatusResult,
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
        api.system.status(),
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
      const systemStatus = ok<SystemStatus>(systemStatusResult);
      if (systemStatus?.services) setSystemHealth(systemStatus.services);

      // Log partial failures for debugging
      [summaryResult, holdingsResult, positionsResult, tradesResult, strategyResult,
        pricesResult, loopResult, toolCallsResult, reasoningResult, cyclePerformanceResult,
        evaluationResult, systemStatusResult]
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
      onPositionUpdated: (position: Position) => {
        const currentPositions = useDashboardStore.getState().portfolio.positions || [];
        const idx = currentPositions.findIndex((p) => p.id === position.id);
        if (idx >= 0) {
          const updated = [...currentPositions];
          updated[idx] = position;
          setPortfolioData({ positions: updated });
        } else {
          setPortfolioData({ positions: [...currentPositions, position] });
        }
      },
      onPositionClosed: (positionId: string) => {
        const currentPositions = useDashboardStore.getState().portfolio.positions || [];
        setPortfolioData({ positions: currentPositions.filter((p) => p.id !== positionId) });
      },
      onAgentStateChanged: (state) => {
        const prevData = useDashboardStore.getState().agent;
        const prevState = prevData.state;
        // Keep LoopStatePanel + AgentStatePanel in sync — agent.loop.agentState is only
        // loaded once at initial REST load and never refreshed otherwise.
        const loopUpdate = prevData.loop ? { loop: { ...prevData.loop, agentState: state } } : {};
        if (!state.isStalled && state.isRunning && prevState?.currentState !== state.currentState) {
          // New step started — clear per-step streaming buffers
          setAgentData({ state, ...loopUpdate, reasoning: [], toolCalls: [] });
        } else {
          setAgentData({ state, ...loopUpdate });
        }
      },
      onToolCallUpdated: (toolCall: ToolCall) => {
        const currentCalls = useDashboardStore.getState().agent.toolCalls || [];
        const idx = currentCalls.findIndex((c) => c.id === toolCall.id);
        let updated: ToolCall[];
        if (idx >= 0) {
          updated = [...currentCalls];
          // Preserve input from ToolCallStarted — ToolCallCompleted does not carry it
          updated[idx] = { ...currentCalls[idx], ...toolCall, input: toolCall.input || currentCalls[idx].input };
        } else {
          updated = [...currentCalls, toolCall];
        }
        if (updated.length > 50) updated = updated.slice(-50);
        setAgentData({ toolCalls: updated });
      },
      onReasoningUpdated: (step: ReasoningStep) => {
        const current = useDashboardStore.getState().agent.reasoning || [];
        const updated = [...current, step];
        setAgentData({ reasoning: updated.length > 200 ? updated.slice(-200) : updated });
      },
      onStrategyUpdated: (strategy) => {
        setStrategyData({ current: strategy });
      },
      onCycleCompleted: (cycle) => {
        setPerformanceData({ currentCycle: cycle });
      },
      onSystemHealthUpdated: (services) => {
        setSystemHealth(services);
      },
      onConnected: () => {
        setConnectionStatus('connected');
        // Stop polling for data that is now pushed via SignalR.
        // Positions polling remains active to keep unrealized PnL up to date.
        poller.stop('marketData');
        poller.stop('strategy');
        poller.stop('systemHealth');
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

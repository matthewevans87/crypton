import * as signalR from '@microsoft/signalr';
import type { 
  PortfolioSummary, 
  PriceTicker, 
  AgentState, 
  ToolCall, 
  ReasoningStep, 
  Strategy, 
  CyclePerformance, 
  EvaluationSummary 
} from '../types';
import { messageQueue } from './messageQueue';
import { createThrottle, batchUpdates } from '../utils/throttle';

const WS_BASE_URL = process.env.REACT_APP_WS_BASE_URL || '';

const MAX_UPDATES_PER_SECOND = 10;
const DEFAULT_HUB_PATH = '/hubs/dashboard';

type EventHandlers = {
  onPortfolioUpdated?: (data: PortfolioSummary) => void;
  onPriceUpdated?: (data: PriceTicker) => void;
  onAgentStateChanged?: (data: AgentState) => void;
  onToolCallUpdated?: (data: ToolCall) => void;
  onReasoningUpdated?: (data: ReasoningStep) => void;
  onStrategyUpdated?: (data: Strategy) => void;
  onCycleCompleted?: (data: CyclePerformance) => void;
  onEvaluationCompleted?: (data: EvaluationSummary) => void;
  onError?: (error: string) => void;
  onConnected?: () => void;
  onDisconnected?: () => void;
};

let hubConnection: signalR.HubConnection | null = null;
let handlers: EventHandlers = {};

const throttledPriceUpdates = batchUpdates<PriceTicker>((updates) => {
  updates.forEach((data) => handlers.onPriceUpdated?.(data));
}, 100);

const throttledPortfolioUpdates = batchUpdates<PortfolioSummary>((updates) => {
  const latest = updates[updates.length - 1];
  handlers.onPortfolioUpdated?.(latest);
}, 100);

export const signalRService = {
  connect: (url = `${WS_BASE_URL}${DEFAULT_HUB_PATH}`) => {
    if (hubConnection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 1000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    hubConnection.on('PortfolioUpdated', (data: PortfolioSummary) => {
      if (hubConnection?.state !== signalR.HubConnectionState.Connected) {
        messageQueue.enqueue('PortfolioUpdated', data);
        return;
      }
      handlers.onPortfolioUpdated?.(data);
    });

    hubConnection.on('PositionUpdated', (data: { asset: string; quantity: number; entryPrice: number; currentPrice: number; pnl: number }) => {
      handlers.onPortfolioUpdated?.({
        totalValue: data.currentPrice * data.quantity,
        realizedPnL: data.pnl,
        unrealizedPnL: 0,
      } as PortfolioSummary);
    });

    hubConnection.on('PositionClosed', (data: { asset: string }) => {
      console.log('Position closed:', data.asset);
    });

    hubConnection.on('PriceUpdated', (data: PriceTicker) => {
      if (hubConnection?.state !== signalR.HubConnectionState.Connected) {
        messageQueue.enqueue('PriceUpdated', data);
        return;
      }
      throttledPriceUpdates(data);
    });

    hubConnection.on('AgentStateChanged', (data: AgentState) => {
      handlers.onAgentStateChanged?.(data);
    });

    hubConnection.on('ToolCallStarted', (data: ToolCall) => {
      handlers.onToolCallUpdated?.(data);
    });

    hubConnection.on('ToolCallCompleted', (data: ToolCall) => {
      handlers.onToolCallUpdated?.(data);
    });

    hubConnection.on('ReasoningUpdated', (data: ReasoningStep) => {
      handlers.onReasoningUpdated?.(data);
    });

    hubConnection.on('StrategyUpdated', (data: Strategy) => {
      handlers.onStrategyUpdated?.(data);
    });

    hubConnection.on('CycleCompleted', (data: CyclePerformance) => {
      handlers.onCycleCompleted?.(data);
    });

    hubConnection.on('EvaluationCompleted', (data: EvaluationSummary) => {
      handlers.onEvaluationCompleted?.(data);
    });

    hubConnection.on('ErrorOccurred', (error: string) => {
      handlers.onError?.(error);
    });

    hubConnection.onclose(() => {
      handlers.onDisconnected?.();
    });

    hubConnection.onreconnecting(() => {
      console.log('SignalR reconnecting...');
      messageQueue.replay((message) => {
        switch (message.type) {
          case 'PortfolioUpdated':
            handlers.onPortfolioUpdated?.(message.data as PortfolioSummary);
            break;
          case 'PriceUpdated':
            handlers.onPriceUpdated?.(message.data as PriceTicker);
            break;
          case 'AgentStateChanged':
            handlers.onAgentStateChanged?.(message.data as AgentState);
            break;
          case 'StrategyUpdated':
            handlers.onStrategyUpdated?.(message.data as Strategy);
            break;
          case 'CycleCompleted':
            handlers.onCycleCompleted?.(message.data as CyclePerformance);
            break;
        }
      });
    });

    hubConnection.onreconnected(() => {
      handlers.onConnected?.();
      console.log('SignalR reconnected');
    });

    hubConnection.start()
      .then(() => {
        handlers.onConnected?.();
        console.log('SignalR connected');
      })
      .catch((err) => {
        console.error('SignalR connection failed:', err);
        handlers.onError?.(err.message);
      });
  },

  disconnect: () => {
    if (hubConnection) {
      hubConnection.stop();
      hubConnection = null;
    }
  },

  subscribe: (eventHandlers: EventHandlers) => {
    handlers = { ...handlers, ...eventHandlers };
  },

  unsubscribe: () => {
    handlers = {};
  },

  joinGroup: (groupName: string) => {
    if (hubConnection?.state === signalR.HubConnectionState.Connected) {
      hubConnection.invoke('JoinGroup', groupName);
    }
  },

  leaveGroup: (groupName: string) => {
    if (hubConnection?.state === signalR.HubConnectionState.Connected) {
      hubConnection.invoke('LeaveGroup', groupName);
    }
  },

  isConnected: () => {
    return hubConnection?.state === signalR.HubConnectionState.Connected;
  },
};

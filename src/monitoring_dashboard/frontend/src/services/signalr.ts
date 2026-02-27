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

export const signalRService = {
  connect: (url = '/hubs/dashboard') => {
    if (hubConnection?.state === signalR.HubConnectionState.Connected) {
      return;
    }

    hubConnection = new signalR.HubConnectionBuilder()
      .withUrl(url)
      .withAutomaticReconnect([0, 1000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    hubConnection.on('PortfolioUpdated', (data: PortfolioSummary) => {
      handlers.onPortfolioUpdated?.(data);
    });

    hubConnection.on('PriceUpdated', (data: PriceTicker) => {
      handlers.onPriceUpdated?.(data);
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

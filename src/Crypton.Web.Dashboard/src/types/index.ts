export interface PortfolioSummary {
  totalValue: number;
  change24h: number;
  changePercent24h: number;
  unrealizedPnL: number;
  availableCapital: number;
  lastUpdated: string;
}

export interface Holding {
  asset: string;
  quantity: number;
  currentPrice: number;
  value: number;
  allocationPercent: number;
}

export interface Position {
  id: string;
  asset: string;
  direction: 'long' | 'short';
  entryPrice: number;
  currentPrice: number;
  size: number;
  stopLoss?: number;
  takeProfit?: number;
  unrealizedPnL: number;
  unrealizedPnLPercent: number;
  openedAt: string;
  timeInPosition: number;
  isNearStopLoss: boolean;
  isNearTakeProfit: boolean;
}

export interface Trade {
  id: string;
  asset: string;
  direction: string;
  entryPrice: number;
  exitPrice: number;
  size: number;
  pnl: number;
  pnlPercent: number;
  entryTime: string;
  exitTime: string;
  exitReason: string;
  isWin: boolean;
}

export interface StrategyOverview {
  mode: 'paper' | 'live';
  posture: 'aggressive' | 'moderate' | 'defensive' | 'flat' | 'exit_all';
  validUntil: string;
  rationale: string;
  maxDrawdown: number;
  dailyLossLimit: number;
  maxExposure: number;
  lastUpdated: string;
  timeRemaining: number;
  isExpired: boolean;
}

export interface PositionRule {
  asset: string;
  entryCondition: string;
  allocation: number;
  stopLoss?: number;
  takeProfit?: number;
  takeProfitTargets: TakeProfitTarget[];
  invalidationCondition?: string;
  timeBasedExit?: string;
}

export interface TakeProfitTarget {
  price: number;
  closePercent: number;
}

export interface Strategy {
  overview: StrategyOverview;
  positionRules: PositionRule[];
}

export interface StrategyHistoryItem {
  id: string;
  createdAt: string;
  mode: string;
  posture: string;
  positionCount: number;
}

export interface PriceTicker {
  asset: string;
  price: number;
  change24h: number;
  changePercent24h: number;
  bid: number;
  ask: number;
  high24h: number;
  low24h: number;
  volume24h: number;
  lastUpdated: string;
}

export interface TechnicalIndicator {
  asset: string;
  timeframe: string;
  rsi?: number;
  macd?: number;
  macdSignal?: number;
  macdHistogram?: number;
  bollingerUpper?: number;
  bollingerMiddle?: number;
  bollingerLower?: number;
  signal?: 'overbought' | 'oversold' | 'neutral';
  lastUpdated: string;
}

export interface MacroSignals {
  trend: 'bullish' | 'bearish' | 'neutral';
  volatilityRegime: 'low' | 'normal' | 'high';
  fearGreedIndex?: number;
  sentiment?: string;
  btcDominance?: number;
  totalMarketCap?: number;
  lastUpdated: string;
}

export interface Ohlcv {
  timestamp: string;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface AgentState {
  currentState: string;
  activeAgent?: string;
  stateStartedAt: string;
  isRunning: boolean;
  timeInState: number;
  progressPercent: number;
  currentTool?: string;
  tokensUsed: number;
  lastLatencyMs?: number;
}

export interface LoopStatus {
  agentState: AgentState;
  lastCycleCompletedAt?: string;
  nextCycleExpectedAt?: string;
  currentArtifact?: string;
  cycleNumber: number;
}

export interface ToolCall {
  id: string;
  toolName: string;
  input: string;
  output?: string;
  calledAt: string;
  durationMs: number;
  isCompleted: boolean;
  isError: boolean;
  errorMessage?: string;
}

export interface ReasoningStep {
  timestamp: string;
  content: string;
  token?: string;
}

export interface EvaluationSummary {
  cycleId: string;
  evaluatedAt: string;
  rating: 'A' | 'B' | 'C' | 'D' | 'F';
  netPnL: number;
  return: number;
  maxDrawdown: number;
  winRate: number;
  totalTrades: number;
  verdict: string;
  recommendations: string[];
  ratingTrend?: 'up' | 'down' | 'stable';
}

export interface CyclePerformance {
  cycleId: string;
  startDate: string;
  endDate?: string;
  realizedPnL: number;
  unrealizedPnL: number;
  totalPnL: number;
  winRate: number;
  avgWin: number;
  avgLoss: number;
  maxDrawdown: number;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  dailyLossLimitBreached: boolean;
}

export interface LifetimePerformance {
  totalPnL: number;
  totalReturn: number;
  winRate: number;
  totalTrades: number;
  winningTrades: number;
  losingTrades: number;
  longestWinningStreak: number;
  longestLosingStreak: number;
  sharpeRatio?: number;
}

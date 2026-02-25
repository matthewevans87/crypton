import { useDashboardStore } from '../../store/dashboard';

interface TechIndPanelProps {
  config?: Record<string, unknown>;
}

interface IndicatorData {
  rsi: number;
  macd: number;
  macdSignal: number;
  macdHistogram: number;
  bbUpper: number;
  bbMiddle: number;
  bbLower: number;
}

export function TechnicalIndicatorsPanel({ config }: TechIndPanelProps) {
  const { market } = useDashboardStore();
  const asset = (config?.asset as string) || 'BTC/USD';
  
  const indicators = market.indicators[asset]?.[0];

  const mockIndicators: IndicatorData = {
    rsi: 62.4,
    macd: 125.50,
    macdSignal: 118.30,
    macdHistogram: 7.20,
    bbUpper: 46200,
    bbMiddle: 45200,
    bbLower: 44200,
  };

  const ind: IndicatorData = indicators ? {
    rsi: indicators.rsi ?? 0,
    macd: indicators.macd ?? 0,
    macdSignal: indicators.macdSignal ?? 0,
    macdHistogram: indicators.macdHistogram ?? 0,
    bbUpper: indicators.bollingerUpper ?? 0,
    bbMiddle: indicators.bollingerMiddle ?? 0,
    bbLower: indicators.bollingerLower ?? 0,
  } : mockIndicators;

  const getRsiColor = (rsi: number) => {
    if (rsi >= 70) return 'var(--color-loss)';
    if (rsi <= 30) return 'var(--color-profit)';
    return 'var(--text-secondary)';
  };

  const getMacdColor = (hist: number) => {
    if (hist > 0) return 'var(--color-profit)';
    if (hist < 0) return 'var(--color-loss)';
    return 'var(--text-secondary)';
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>RSI (14)</span>
        <span style={{ fontFamily: 'var(--font-mono)', color: getRsiColor(ind.rsi), fontWeight: 600 }}>
          {ind.rsi.toFixed(1)}
        </span>
      </div>
      <div style={{ height: '4px', backgroundColor: 'var(--border-default)', borderRadius: '2px', overflow: 'hidden' }}>
        <div style={{ height: '100%', width: `${(ind.rsi / 100) * 100}%`, backgroundColor: getRsiColor(ind.rsi) }} />
      </div>

      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginTop: 'var(--space-1)' }}>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>MACD</span>
        <span style={{ fontFamily: 'var(--font-mono)', color: getMacdColor(ind.macdHistogram) }}>
          {ind.macd.toFixed(2)}
        </span>
      </div>
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
        <span>Signal: {ind.macdSignal.toFixed(2)}</span>
        <span>Hist: <span style={{ color: getMacdColor(ind.macdHistogram) }}>{ind.macdHistogram > 0 ? '+' : ''}{ind.macdHistogram.toFixed(2)}</span></span>
      </div>

      <div style={{ marginTop: 'var(--space-1)' }}>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)', marginBottom: '4px' }}>Bollinger Bands</div>
        <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)', fontFamily: 'var(--font-mono)' }}>
          <span style={{ color: 'var(--color-loss)' }}>{ind.bbUpper.toLocaleString()}</span>
          <span style={{ color: 'var(--text-secondary)' }}>{ind.bbMiddle.toLocaleString()}</span>
          <span style={{ color: 'var(--color-profit)' }}>{ind.bbLower.toLocaleString()}</span>
        </div>
      </div>
    </div>
  );
}

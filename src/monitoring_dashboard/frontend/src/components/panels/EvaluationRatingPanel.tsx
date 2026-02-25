import { useDashboardStore } from '../../store/dashboard';

const RATING_CONFIG = {
  A: { color: 'var(--color-profit)', label: 'Excellent', trend: '▲' },
  B: { color: '#00ccaa', label: 'Good', trend: '▲' },
  C: { color: 'var(--color-warning)', label: 'Average', trend: '─' },
  D: { color: '#ff8844', label: 'Below Average', trend: '▼' },
  F: { color: 'var(--color-loss)', label: 'Poor', trend: '▼' },
};

export function EvaluationRatingPanel() {
  const { performance } = useDashboardStore();
  const evaluation = performance.evaluation;
  
  if (!evaluation) {
    return <div style={{ color: 'var(--text-tertiary)' }}>No evaluation data</div>;
  }
  
  const rating = evaluation.rating?.toUpperCase() || 'N/A';
  const config = RATING_CONFIG[rating as keyof typeof RATING_CONFIG] || RATING_CONFIG.C;
  
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-3)' }}>
        <div
          style={{
            width: '48px',
            height: '48px',
            borderRadius: '4px',
            backgroundColor: `${config.color}20`,
            border: `2px solid ${config.color}`,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: '24px',
            fontFamily: 'var(--font-mono)',
            fontWeight: 700,
            color: config.color,
          }}
        >
          {rating}
        </div>
        
        <div>
          <div style={{ fontSize: 'var(--font-size-sm)', color: config.color, fontWeight: 500 }}>
            {config.label}
          </div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
            {evaluation.ratingTrend === 'up' ? '▲ Improving' : evaluation.ratingTrend === 'down' ? '▼ Declining' : '─ Stable'}
          </div>
        </div>
      </div>
      
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-1)' }}>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Net P&L</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)', color: evaluation.netPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)' }}>
            {evaluation.netPnL >= 0 ? '+' : ''}{evaluation.netPnL.toFixed(2)}
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Return</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {evaluation.return >= 0 ? '+' : ''}{evaluation.return.toFixed(2)}%
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Win Rate</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {evaluation.winRate.toFixed(1)}%
          </div>
        </div>
        <div>
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>Trades</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {evaluation.totalTrades}
          </div>
        </div>
      </div>
      
      {evaluation.verdict && (
        <div style={{ 
          marginTop: 'var(--space-1)', 
          padding: 'var(--space-1)', 
          backgroundColor: 'var(--bg-panel-header)', 
          borderRadius: '2px',
          fontSize: 'var(--font-size-xs)',
          color: 'var(--text-secondary)',
        }}>
          {evaluation.verdict}
        </div>
      )}
    </div>
  );
}

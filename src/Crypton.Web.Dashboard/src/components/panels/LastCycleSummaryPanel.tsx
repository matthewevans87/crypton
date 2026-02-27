import { useDashboardStore } from '../../store/dashboard';
import { formatTimestamp } from '../../utils/dateUtils';
import { formatCompact } from '../../utils/priceNormalization';

export function LastCycleSummaryPanel() {
  const { performance } = useDashboardStore();
  const cycle = performance.currentCycle;
  const evaluation = performance.evaluation;

  if (!cycle) {
    return (
      <div style={{ color: 'var(--text-tertiary)', textAlign: 'center', padding: 'var(--space-4)' }}>
        Waiting for first cycle...
      </div>
    );
  }

  const totalPnL = cycle.realizedPnL + cycle.unrealizedPnL;
  const pnlColor = totalPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)';

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      <div style={{
        display: 'flex',
        justifyContent: 'space-between',
        alignItems: 'center',
        paddingBottom: 'var(--space-2)',
        borderBottom: '1px solid var(--border-default)',
      }}>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>
          Last Cycle
        </span>
        <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
          {formatTimestamp(cycle.startDate, 'relative')}
        </span>
      </div>

      <div style={{ display: 'flex', gap: 'var(--space-3)' }}>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: '10px', color: 'var(--text-tertiary)' }}>P&L</div>
          <div style={{
            fontFamily: 'var(--font-mono)',
            fontSize: '16px',
            fontWeight: 600,
            color: pnlColor
          }}>
            {totalPnL >= 0 ? '+' : ''}{formatCompact(totalPnL)}
          </div>
        </div>
        <div style={{ flex: 1 }}>
          <div style={{ fontSize: '10px', color: 'var(--text-tertiary)' }}>Win Rate</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: '16px', fontWeight: 600 }}>
            {cycle.winRate.toFixed(1)}%
          </div>
        </div>
      </div>

      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-1)' }}>
        <div>
          <div style={{ fontSize: '10px', color: 'var(--text-tertiary)' }}>Trades</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {cycle.totalTrades}
          </div>
        </div>
        <div>
          <div style={{ fontSize: '10px', color: 'var(--text-tertiary)' }}>Duration</div>
          <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-sm)' }}>
            {'—'}
          </div>
        </div>
      </div>

      {evaluation && (
        <div style={{
          marginTop: 'var(--space-1)',
          padding: 'var(--space-2)',
          backgroundColor: 'var(--bg-viewport)',
          borderRadius: '2px',
        }}>
          <div style={{ fontSize: '10px', color: 'var(--text-tertiary)', marginBottom: '4px' }}>
            Rating
          </div>
          <div style={{
            fontFamily: 'var(--font-mono)',
            fontSize: '18px',
            fontWeight: 600,
            color: evaluation.rating === 'A' ? 'var(--color-profit)'
              : evaluation.rating === 'B' ? 'var(--color-info)'
                : evaluation.rating === 'C' ? 'var(--color-warning)'
                  : 'var(--color-loss)'
          }}>
            {evaluation.rating}
            {evaluation.ratingTrend === 'up' && ' ↑'}
            {evaluation.ratingTrend === 'down' && ' ↓'}
          </div>
        </div>
      )}


    </div>
  );
}

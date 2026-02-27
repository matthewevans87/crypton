import { useState } from 'react';
import { useDashboardStore } from '../../store/dashboard';
import { api } from '../../services/api';
import { formatTimestamp } from '../../utils/dateUtils';


interface CycleDetail {
  id: string;
  startedAt: string;
  completedAt?: string;
  durationSeconds?: number;
  realizedPnL: number;
  unrealizedPnL: number;
  totalTrades: number;
  winningTrades: number;
  winRate: number;
  maxDrawdown: number;
  avgWin: number;
  avgLoss: number;
  strategy?: {
    id: string;
    name: string;
  };
}

export function CycleDetailPanel() {
  const { performance } = useDashboardStore();
  const [selectedCycleId, setSelectedCycleId] = useState<string | null>(null);
  const [cycleDetail, setCycleDetail] = useState<CycleDetail | null>(null);
  const [loading, setLoading] = useState(false);

  const cycles = performance.cycles;

  const handleSelectCycle = async (cycleId: string) => {
    setSelectedCycleId(cycleId);
    setLoading(true);
    try {
      const detail = await api.strategy.byId(cycleId) as unknown as CycleDetail;
      setCycleDetail(detail);
    } catch (error) {
      console.error('Failed to fetch cycle detail:', error);
    } finally {
      setLoading(false);
    }
  };

  if (cycles.length === 0) {
    return (
      <div style={{ color: 'var(--text-tertiary)', textAlign: 'center', padding: 'var(--space-4)' }}>
        No cycles yet
      </div>
    );
  }

  const formatCurrency = (value: number) => {
    const sign = value >= 0 ? '+' : '';
    return `${sign}$${Math.abs(value).toFixed(2)}`;
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100%', overflow: 'hidden' }}>
      <div style={{
        flex: 1,
        overflow: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--space-1)'
      }}>
        {cycles.slice(0, 10).map((cycle) => (
          <div
            key={cycle.cycleId}
            onClick={() => handleSelectCycle(cycle.cycleId)}
            style={{
              padding: 'var(--space-2)',
              backgroundColor: selectedCycleId === cycle.cycleId
                ? 'var(--bg-viewport)'
                : 'transparent',
              border: selectedCycleId === cycle.cycleId
                ? '1px solid var(--color-info)'
                : '1px solid var(--border-default)',
              borderRadius: '2px',
              cursor: 'pointer',
              fontSize: 'var(--font-size-xs)',
            }}
          >
            <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: '4px' }}>
              <span style={{ color: 'var(--text-secondary)' }}>
                {formatTimestamp(cycle.startDate, 'history')}
              </span>
              <span style={{
                fontFamily: 'var(--font-mono)',
                color: (cycle.realizedPnL + cycle.unrealizedPnL) >= 0
                  ? 'var(--color-profit)'
                  : 'var(--color-loss)'
              }}>
                {formatCurrency(cycle.realizedPnL + cycle.unrealizedPnL)}
              </span>
            </div>
            <div style={{ display: 'flex', gap: 'var(--space-2)', color: 'var(--text-tertiary)' }}>
              <span>{cycle.totalTrades} trades</span>
              <span>•</span>
              <span>{cycle.winRate.toFixed(0)}% WR</span>
            </div>
          </div>
        ))}
      </div>

      {loading && (
        <div style={{
          padding: 'var(--space-4)',
          textAlign: 'center',
          color: 'var(--text-tertiary)'
        }}>
          Loading...
        </div>
      )}

      {cycleDetail && !loading && (
        <div style={{
          borderTop: '1px solid var(--border-default)',
          padding: 'var(--space-2)',
          maxHeight: '200px',
          overflow: 'auto',
          fontSize: 'var(--font-size-xs)',
        }}>
          <div style={{
            display: 'flex',
            justifyContent: 'space-between',
            alignItems: 'center',
            marginBottom: 'var(--space-2)'
          }}>
            <span style={{ fontWeight: 600 }}>Cycle Details</span>
            <button
              onClick={() => { setCycleDetail(null); setSelectedCycleId(null); }}
              style={{
                background: 'none',
                border: 'none',
                color: 'var(--text-tertiary)',
                cursor: 'pointer',
              }}
            >
              ✕
            </button>
          </div>

          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 'var(--space-1)' }}>
            <div>
              <div style={{ color: 'var(--text-tertiary)' }}>P&L</div>
              <div style={{
                fontFamily: 'var(--font-mono)',
                color: cycleDetail.realizedPnL >= 0 ? 'var(--color-profit)' : 'var(--color-loss)'
              }}>
                {formatCurrency(cycleDetail.realizedPnL)}
              </div>
            </div>
            <div>
              <div style={{ color: 'var(--text-tertiary)' }}>Win Rate</div>
              <div style={{ fontFamily: 'var(--font-mono)' }}>
                {cycleDetail.winRate.toFixed(1)}%
              </div>
            </div>
            <div>
              <div style={{ color: 'var(--text-tertiary)' }}>Trades</div>
              <div style={{ fontFamily: 'var(--font-mono)' }}>
                {cycleDetail.winningTrades}/{cycleDetail.totalTrades}
              </div>
            </div>
            <div>
              <div style={{ color: 'var(--text-tertiary)' }}>Max DD</div>
              <div style={{ fontFamily: 'var(--font-mono)' }}>
                -{cycleDetail.maxDrawdown.toFixed(1)}%
              </div>
            </div>
          </div>

          {cycleDetail.strategy && (
            <div style={{ marginTop: 'var(--space-2)' }}>
              <div style={{ color: 'var(--text-tertiary)' }}>Strategy</div>
              <div style={{ fontFamily: 'var(--font-mono)' }}>
                {cycleDetail.strategy.name}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}

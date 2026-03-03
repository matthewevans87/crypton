import { useState, useEffect } from 'react';
import { useDashboardStore } from '../../store/dashboard';
import { api } from '../../services/api';
import type { CycleIntervalConfig } from '../../types';

const INTERVAL_OPTIONS: { label: string; minutes: number }[] = [
  { label: '1 hour', minutes: 60 },
  { label: '2 hours', minutes: 120 },
  { label: '4 hours', minutes: 240 },
  { label: '6 hours', minutes: 360 },
  { label: '12 hours', minutes: 720 },
  { label: '24 hours', minutes: 1440 },
];

function formatMinutes(minutes: number): string {
  if (minutes < 60) return `${minutes}m`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m > 0 ? `${h}h ${m}m` : `${h}h`;
}

export function LoopStatePanel() {
  const { agent } = useDashboardStore();
  const loop = agent.loop;

  const [intervalCfg, setIntervalCfg] = useState<CycleIntervalConfig | null>(null);
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);

  useEffect(() => {
    api.agent.getCycleInterval()
      .then((cfg) => setIntervalCfg(cfg as CycleIntervalConfig))
      .catch(() => { /* silently ignore if AgentRunner is unavailable */ });
  }, []);

  const handleIntervalChange = async (minutes: number) => {
    if (!intervalCfg || minutes === intervalCfg.cycleIntervalMinutes) return;
    setIsSaving(true);
    setSaveError(null);
    try {
      const updated = await api.agent.setCycleInterval(minutes) as CycleIntervalConfig;
      setIntervalCfg((prev) => prev ? { ...prev, cycleIntervalMinutes: updated.cycleIntervalMinutes ?? minutes } : prev);
    } catch (err) {
      setSaveError('Failed to update — is AgentRunner reachable?');
    } finally {
      setIsSaving(false);
    }
  };

  if (!loop) {
    return <div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>;
  }

  const state = loop.agentState;
  // Loop order: Evaluate (step 0, skipped on first cycle) → Plan → Research → Analyze → Synthesize
  const steps: { name: string; abbr: string; conditional?: boolean }[] = [
    { name: 'Evaluate', abbr: 'EVA', conditional: true },
    { name: 'Plan', abbr: 'PLN' },
    { name: 'Research', abbr: 'RES' },
    { name: 'Analyze', abbr: 'ANA' },
    { name: 'Synthesize', abbr: 'SYN' },
  ];
  const isWaiting = state.currentState === 'WaitingForNextCycle';
  const currentStepIndex = steps.findIndex((s) => s.name === state.currentState);
  // Between cycles all steps are considered complete
  const effectiveIndex = isWaiting ? steps.length : currentStepIndex;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--space-2)' }}>
      {/* Timeline */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', position: 'relative' }}>
        <div
          style={{
            position: 'absolute',
            top: '50%',
            left: 0,
            right: 0,
            height: '2px',
            backgroundColor: 'var(--border-default)',
            transform: 'translateY(-50%)',
          }}
        />
        {steps.map((step, idx) => {
          const isCompleted = idx < effectiveIndex;
          const isCurrent = !isWaiting && idx === currentStepIndex;

          return (
            <div
              key={step.name}
              style={{
                position: 'relative',
                zIndex: 1,
                display: 'flex',
                flexDirection: 'column',
                alignItems: 'center',
                gap: '4px',
              }}
            >
              <div
                style={{
                  width: '12px',
                  height: '12px',
                  borderRadius: step.conditional ? '2px' : '50%',
                  backgroundColor: isCompleted ? 'var(--color-profit)' : isCurrent ? 'var(--color-active)' : 'var(--border-default)',
                  border: isCurrent ? '2px solid var(--color-active)' : 'none',
                  transition: 'all 0.3s ease',
                  opacity: step.conditional && !isCurrent && !isCompleted ? 0.5 : 1,
                }}
              />
              <span
                style={{
                  fontSize: '9px',
                  color: isCurrent ? 'var(--text-primary)' : isCompleted ? 'var(--text-secondary)' : 'var(--text-tertiary)',
                  textTransform: 'uppercase',
                  opacity: step.conditional && !isCurrent && !isCompleted ? 0.5 : 1,
                }}
              >
                {step.abbr}
              </span>
            </div>
          );
        })}
      </div>

      {/* Current State Details */}
      <div style={{ marginTop: 'var(--space-2)' }}>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)' }}>Current Step</div>
        <div style={{ fontFamily: 'var(--font-mono)', fontSize: 'var(--font-size-lg)', fontWeight: 600 }}>
          {isWaiting ? 'Waiting' : state.currentState}
        </div>
        {state.currentState === 'Evaluate' && (
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', marginTop: '2px' }}>
            Step 0 — skipped on first cycle
          </div>
        )}
        {isWaiting && loop.nextCycleExpectedAt && (
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)', marginTop: '2px' }}>
            Next: {new Date(loop.nextCycleExpectedAt).toLocaleTimeString()}
          </div>
        )}
      </div>

      {/* Cycle Info */}
      <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 'var(--font-size-xs)' }}>
        <div>
          <div style={{ color: 'var(--text-tertiary)' }}>Cycle</div>
          <div style={{ fontFamily: 'var(--font-mono)' }}>#{loop.cycleNumber}</div>
        </div>
        <div>
          <div style={{ color: 'var(--text-tertiary)' }}>Artifact</div>
          <div style={{ fontFamily: 'var(--font-mono)', color: 'var(--color-info)' }}>{loop.currentArtifact || '—'}</div>
        </div>
      </div>

      {/* Timestamps */}
      {loop.lastCycleCompletedAt && (
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
          Last cycle: {new Date(loop.lastCycleCompletedAt).toLocaleString()}
        </div>
      )}
      {!isWaiting && loop.nextCycleExpectedAt && (
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>
          Next cycle: {new Date(loop.nextCycleExpectedAt).toLocaleString()}
        </div>
      )}

      {/* Cycle Interval Picker */}
      <div style={{ marginTop: 'var(--space-2)', borderTop: '1px solid var(--border-default)', paddingTop: 'var(--space-2)' }}>
        <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-secondary)', marginBottom: '4px' }}>
          Cycle Interval
          {intervalCfg && (
            <span style={{ color: 'var(--text-tertiary)', marginLeft: '6px' }}>
              ({formatMinutes(intervalCfg.minInterval)}–{formatMinutes(intervalCfg.maxInterval)})
            </span>
          )}
        </div>
        {intervalCfg ? (
          <div style={{ display: 'flex', alignItems: 'center', gap: '8px' }}>
            <select
              value={intervalCfg.cycleIntervalMinutes}
              disabled={isSaving}
              onChange={(e) => handleIntervalChange(Number(e.target.value))}
              style={{
                fontFamily: 'var(--font-mono)',
                fontSize: 'var(--font-size-xs)',
                background: 'var(--bg-secondary)',
                color: 'var(--text-primary)',
                border: '1px solid var(--border-default)',
                borderRadius: '4px',
                padding: '2px 6px',
                cursor: isSaving ? 'not-allowed' : 'pointer',
                opacity: isSaving ? 0.6 : 1,
              }}
            >
              {INTERVAL_OPTIONS
                .filter((o) => o.minutes >= intervalCfg.minInterval && o.minutes <= intervalCfg.maxInterval)
                .map((opt) => (
                  <option key={opt.minutes} value={opt.minutes}>{opt.label}</option>
                ))}
              {/* Fallback if current value is not in preset list */}
              {!INTERVAL_OPTIONS.some((o) => o.minutes === intervalCfg.cycleIntervalMinutes) && (
                <option value={intervalCfg.cycleIntervalMinutes}>
                  {formatMinutes(intervalCfg.cycleIntervalMinutes)} (custom)
                </option>
              )}
            </select>
            {isSaving && (
              <span style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>saving…</span>
            )}
          </div>
        ) : (
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--text-tertiary)' }}>—</div>
        )}
        {saveError && (
          <div style={{ fontSize: 'var(--font-size-xs)', color: 'var(--color-loss)', marginTop: '4px' }}>
            {saveError}
          </div>
        )}
      </div>
    </div>
  );
}

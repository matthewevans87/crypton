import { useState, useRef, useCallback, useEffect } from 'react';
import { useDashboardStore, type PanelConfig } from '../../store/dashboard';
import { PortfolioSummaryPanel } from '../panels/PortfolioSummaryPanel';
import { StrategyOverviewPanel } from '../panels/StrategyOverviewPanel';
import { AgentStatePanel } from '../panels/AgentStatePanel';
import { PriceTickerPanel } from '../panels/PriceTickerPanel';
import { PriceChartPanel } from '../panels/PriceChartPanel';
import { CyclePerformancePanel } from '../panels/CyclePerformancePanel';
import { HoldingsPanel } from '../panels/HoldingsPanel';
import { OpenPositionsPanel } from '../panels/OpenPositionsPanel';
import { ReasoningTracePanel } from '../panels/ReasoningTracePanel';
import { ToolCallsPanel } from '../panels/ToolCallsPanel';
import { LoopStatePanel } from '../panels/LoopStatePanel';
import { TechnicalIndicatorsPanel } from '../panels/TechnicalIndicatorsPanel';
import { DailyLossLimitPanel } from '../panels/DailyLossLimitPanel';
import { LoopTimelinePanel } from '../panels/LoopTimelinePanel';
import { EvaluationRatingPanel } from '../panels/EvaluationRatingPanel';
import { CycleHistoryPanel } from '../panels/CycleHistoryPanel';
import { ToolCallDetailPanel } from '../panels/ToolCallDetailPanel';
import { LastCycleSummaryPanel } from '../panels/LastCycleSummaryPanel';
import { CycleDetailPanel } from '../panels/CycleDetailPanel';

const GRID_SIZE = 8;
const MIN_PANEL_WIDTH = 200;
const MIN_PANEL_HEIGHT = 100;

const GLOW_COLORS = {
  info: 'var(--color-info)',
  warning: 'var(--color-warning)',
  error: 'var(--color-loss)',
  success: 'var(--color-profit)',
};

const PANEL_COMPONENTS: Record<string, React.FC<{ config?: Record<string, unknown> }>> = {
  'portfolio-summary': PortfolioSummaryPanel,
  'holdings': HoldingsPanel,
  'open-positions': OpenPositionsPanel,
  'strategy-overview': StrategyOverviewPanel,
  'agent-state': AgentStatePanel,
  'agent-activity': AgentStatePanel,
  'price-ticker': PriceTickerPanel,
  'price-chart': PriceChartPanel,
  'technical-indicators': TechnicalIndicatorsPanel,
  'cycle-performance': CyclePerformancePanel,
  'daily-loss-limit': DailyLossLimitPanel,
  'loop-state': LoopStatePanel,
  'loop-timeline': LoopTimelinePanel,
  'cycle-history': CycleHistoryPanel,
  'evaluation-rating': EvaluationRatingPanel,
  'reasoning-trace': ReasoningTracePanel,
  'tool-calls': ToolCallsPanel,
  'tool-call-detail': ToolCallDetailPanel,
  'last-cycle-summary': LastCycleSummaryPanel,
  'cycle-detail': CycleDetailPanel,
};

function snapToGrid(value: number): number {
  return Math.round(value / GRID_SIZE) * GRID_SIZE;
}

export function PanelGrid({ panels }: { panels: PanelConfig[] }) {
  const { 
    maximizedPanelId, 
    setMaximizedPanel, 
    activeTabId,
    removePanel,
    updatePanelPosition,
    updatePanelSize,
    togglePanelCollapse,
    panelGlows,
  } = useDashboardStore();

  const [draggingPanel, setDraggingPanel] = useState<string | null>(null);
  const [resizingPanel, setResizingPanel] = useState<string | null>(null);
  const [dragOffset, setDragOffset] = useState({ x: 0, y: 0 });
  const [resizeStart, setResizeStart] = useState({ x: 0, y: 0, width: 0, height: 0 });
  const gridRef = useRef<HTMLDivElement>(null);

  const handleDragStart = useCallback((e: React.MouseEvent, panelId: string) => {
    if (maximizedPanelId) return;
    e.preventDefault();
    const rect = (e.currentTarget.parentElement as HTMLElement).getBoundingClientRect();
    setDragOffset({
      x: e.clientX - rect.left,
      y: e.clientY - rect.top,
    });
    setDraggingPanel(panelId);
  }, [maximizedPanelId]);

  const handleResizeStart = useCallback((e: React.MouseEvent, panelId: string) => {
    if (maximizedPanelId) return;
    e.preventDefault();
    e.stopPropagation();
    const rect = (e.currentTarget.parentElement as HTMLElement).getBoundingClientRect();
    setResizeStart({
      x: e.clientX,
      y: e.clientY,
      width: rect.width,
      height: rect.height,
    });
    setResizingPanel(panelId);
  }, [maximizedPanelId]);

  useEffect(() => {
    if (!draggingPanel && !resizingPanel) return;

    const handleMouseMove = (e: MouseEvent) => {
      if (!gridRef.current) return;
      const gridRect = gridRef.current.getBoundingClientRect();

      if (draggingPanel) {
        const x = snapToGrid(e.clientX - gridRect.left - dragOffset.x);
        const y = snapToGrid(e.clientY - gridRect.top - dragOffset.y);
        const snappedX = Math.max(0, x);
        const snappedY = Math.max(0, y);
        updatePanelPosition(activeTabId, draggingPanel, snappedX, snappedY);
      }

      if (resizingPanel) {
        const deltaX = e.clientX - resizeStart.x;
        const deltaY = e.clientY - resizeStart.y;
        const newWidth = snapToGrid(Math.max(MIN_PANEL_WIDTH, resizeStart.width + deltaX));
        const newHeight = snapToGrid(Math.max(MIN_PANEL_HEIGHT, resizeStart.height + deltaY));
        updatePanelSize(activeTabId, resizingPanel, newWidth, newHeight);
      }
    };

    const handleMouseUp = () => {
      setDraggingPanel(null);
      setResizingPanel(null);
    };

    document.addEventListener('mousemove', handleMouseMove);
    document.addEventListener('mouseup', handleMouseUp);

    return () => {
      document.removeEventListener('mousemove', handleMouseMove);
      document.removeEventListener('mouseup', handleMouseUp);
    };
  }, [draggingPanel, resizingPanel, dragOffset, resizeStart, activeTabId, updatePanelPosition, updatePanelSize]);

  if (panels.length === 0) {
    return (
      <div
        style={{
          flex: 1,
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--text-secondary)',
          fontSize: 'var(--font-size-sm)',
        }}
      >
        Press ⌘K to add panels
      </div>
    );
  }

  if (maximizedPanelId) {
    const maximizedPanel = panels.find((p) => p.id === maximizedPanelId);
    if (maximizedPanel) {
      const PanelComponent = PANEL_COMPONENTS[maximizedPanel.type];
      return (
        <div ref={gridRef} style={gridStyle}>
          <DraggablePanel
            panel={maximizedPanel}
            isMaximized
            onMaximize={() => setMaximizedPanel(null)}
            onRemove={() => {
              removePanel(activeTabId, maximizedPanel.id);
              setMaximizedPanel(null);
            }}
            onToggleCollapse={() => togglePanelCollapse(activeTabId, maximizedPanel.id)}
          >
            {PanelComponent ? <PanelComponent config={maximizedPanel.config} /> : <div>Unknown panel: {maximizedPanel.type}</div>}
          </DraggablePanel>
        </div>
      );
    }
  }

  return (
    <div ref={gridRef} style={gridStyle}>
      {panels.map((panel) => {
        const PanelComponent = PANEL_COMPONENTS[panel.type];
        const glow = panelGlows[panel.id];
        
        return (
          <DraggablePanel
            key={panel.id}
            panel={panel}
            isDragging={draggingPanel === panel.id}
            isResizing={resizingPanel === panel.id}
            glowType={glow?.type}
            onDragStart={(e) => handleDragStart(e, panel.id)}
            onResizeStart={(e) => handleResizeStart(e, panel.id)}
            onMaximize={() => setMaximizedPanel(panel.id)}
            onRemove={() => removePanel(activeTabId, panel.id)}
            onToggleCollapse={() => togglePanelCollapse(activeTabId, panel.id)}
          >
            {panel.collapsed ? (
              <CollapsedPanelContent panel={panel} />
            ) : (
              <div style={{ flex: 1, overflow: 'auto', padding: 'var(--panel-padding)' }}>
                {PanelComponent ? (
                  <PanelComponent config={panel.config} />
                ) : (
                  <div style={{ color: 'var(--text-tertiary)' }}>Unknown panel: {panel.type}</div>
                )}
              </div>
            )}
          </DraggablePanel>
        );
      })}
    </div>
  );
}

interface DraggablePanelProps {
  panel: PanelConfig;
  isMaximized?: boolean;
  isDragging?: boolean;
  isResizing?: boolean;
  glowType?: 'info' | 'warning' | 'error' | 'success';
  children: React.ReactNode;
  onDragStart?: (e: React.MouseEvent) => void;
  onResizeStart?: (e: React.MouseEvent) => void;
  onMaximize: () => void;
  onRemove: () => void;
  onToggleCollapse: () => void;
}

function DraggablePanel({
  panel,
  isMaximized,
  isDragging,
  isResizing,
  glowType,
  children,
  onDragStart,
  onResizeStart,
  onMaximize,
  onRemove,
  onToggleCollapse,
}: DraggablePanelProps) {
  const [isHovered, setIsHovered] = useState(false);
  
  const glowColor = glowType ? GLOW_COLORS[glowType] : undefined;
  const hasGlow = !!glowType;

  return (
    <div
      style={{
        display: 'flex',
        flexDirection: 'column',
        backgroundColor: 'var(--bg-panel)',
        border: isDragging || isResizing
          ? '2px solid var(--color-info)'
          : hasGlow
            ? `2px solid ${glowColor}`
            : '1px solid var(--border-default)',
        borderRadius: '4px',
        overflow: 'hidden',
        minHeight: panel.collapsed ? 'var(--panel-header-height)' : '150px',
        width: panel.width ? `${panel.width}px` : undefined,
        height: panel.collapsed ? 'var(--panel-header-height)' : (panel.height ? `${panel.height}px` : undefined),
        opacity: isDragging ? 0.8 : 1,
        cursor: isDragging ? 'grabbing' : 'default',
        transition: isDragging || isResizing ? 'none' : 'border-color 150ms ease, box-shadow 150ms ease',
        boxShadow: hasGlow ? `0 0 12px ${glowColor}40` : undefined,
      }}
      onMouseEnter={() => setIsHovered(true)}
      onMouseLeave={() => setIsHovered(false)}
    >
      <PanelHeader
        title={panel.type}
        isMaximized={isMaximized}
        isCollapsed={panel.collapsed}
        showControls={isHovered || isMaximized}
        onDragStart={onDragStart}
        onMaximize={onMaximize}
        onRemove={onRemove}
        onToggleCollapse={onToggleCollapse}
        onDoubleClick={onMaximize}
      />
      {children}
      {!panel.collapsed && onResizeStart && (
        <ResizeHandle onResizeStart={onResizeStart} />
      )}
    </div>
  );
}

function ResizeHandle({ onResizeStart }: { onResizeStart?: (e: React.MouseEvent) => void }) {
  if (!onResizeStart) return null;
  return (
    <div
      onMouseDown={onResizeStart}
      style={{
        position: 'absolute',
        right: 0,
        bottom: 0,
        width: '12px',
        height: '12px',
        cursor: 'se-resize',
        background: 'linear-gradient(135deg, transparent 50%, var(--border-default) 50%)',
      }}
    />
  );
}

function CollapsedPanelContent({ panel }: { panel: PanelConfig }) {
  return (
    <div
      style={{
        height: 'var(--panel-header-height)',
        display: 'flex',
        alignItems: 'center',
        padding: '0 var(--space-2)',
        color: 'var(--text-secondary)',
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--font-size-xs)',
      }}
    >
      <span style={{ textTransform: 'uppercase' }}>{panel.type}</span>
    </div>
  );
}

function PanelHeader({
  title,
  isMaximized,
  isCollapsed,
  showControls,
  onDragStart,
  onMaximize,
  onRemove,
  onToggleCollapse,
  onDoubleClick,
}: {
  title: string;
  isMaximized?: boolean;
  isCollapsed?: boolean;
  showControls?: boolean;
  onDragStart?: (e: React.MouseEvent) => void;
  onMaximize: () => void;
  onRemove: () => void;
  onToggleCollapse: () => void;
  onDoubleClick: () => void;
}) {
  return (
    <div
      style={{
        height: 'var(--panel-header-height)',
        backgroundColor: 'var(--bg-panel-header)',
        borderBottom: isCollapsed ? 'none' : '1px solid var(--border-default)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        padding: '0 var(--space-2)',
        flexShrink: 0,
        cursor: onDragStart ? 'grab' : 'default',
        userSelect: 'none',
      }}
      onMouseDown={onDragStart ?? undefined}
      onDoubleClick={onDoubleClick}
    >
      <span
        style={{
          fontSize: 'var(--font-size-xs)',
          color: 'var(--text-secondary)',
          textTransform: 'uppercase',
          letterSpacing: '0.5px',
          fontFamily: 'var(--font-mono)',
        }}
      >
        {title}
      </span>
      {showControls && (
        <div style={{ display: 'flex', gap: '2px' }}>
          <button
            onClick={(e) => {
              e.stopPropagation();
              onToggleCollapse();
            }}
            title={isCollapsed ? 'Expand' : 'Collapse'}
            style={{
              width: '18px',
              height: '18px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: 'transparent',
              border: 'none',
              borderRadius: '2px',
              color: 'var(--text-tertiary)',
              cursor: 'pointer',
              fontSize: '12px',
            }}
          >
            {isCollapsed ? '◢' : '◥'}
          </button>
          <button
            onClick={(e) => {
              e.stopPropagation();
              onMaximize();
            }}
            title="Maximize"
            style={{
              width: '18px',
              height: '18px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: 'transparent',
              border: 'none',
              borderRadius: '2px',
              color: 'var(--text-tertiary)',
              cursor: 'pointer',
              fontSize: '12px',
            }}
          >
            {isMaximized ? '❐' : '□'}
          </button>
          <button
            onClick={(e) => {
              e.stopPropagation();
              onRemove();
            }}
            title="Close"
            style={{
              width: '18px',
              height: '18px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: 'transparent',
              border: 'none',
              borderRadius: '2px',
              color: 'var(--text-tertiary)',
              cursor: 'pointer',
              fontSize: '12px',
            }}
          >
            ×
          </button>
        </div>
      )}
    </div>
  );
}

const gridStyle: React.CSSProperties = {
  flex: 1,
  display: 'grid',
  gridTemplateColumns: 'repeat(3, 1fr)',
  gap: 'var(--panel-gap)',
  padding: 'var(--panel-gap)',
  overflow: 'hidden',
  position: 'relative',
};

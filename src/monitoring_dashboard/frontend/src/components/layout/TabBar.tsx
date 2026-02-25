import { useState, useRef, useCallback, useEffect } from 'react';
import { useDashboardStore } from '../../store/dashboard';

const MAX_TABS = 8;

export function TabBar() {
  const { 
    tabs, 
    activeTabId, 
    setActiveTab, 
    removeTab, 
    addTab, 
    reorderPanels,
    tabs: allTabs,
  } = useDashboardStore();
  
  const [contextMenu, setContextMenu] = useState<{ x: number; y: number; tabId: string } | null>(null);
  const [draggingTab, setDraggingTab] = useState<string | null>(null);
  const [dragOverTab, setDragOverTab] = useState<string | null>(null);
  const tabRefs = useRef<Map<string, HTMLDivElement>>(new Map());

  const handleContextMenu = useCallback((e: React.MouseEvent, tabId: string) => {
    e.preventDefault();
    setContextMenu({ x: e.clientX, y: e.clientY, tabId });
  }, []);

  const closeContextMenu = useCallback(() => {
    setContextMenu(null);
  }, []);

  useEffect(() => {
    if (contextMenu) {
      const handleClick = () => closeContextMenu();
      document.addEventListener('click', handleClick);
      return () => document.removeEventListener('click', handleClick);
    }
  }, [contextMenu, closeContextMenu]);

  const handleDragStart = useCallback((e: React.DragEvent, tabId: string) => {
    setDraggingTab(tabId);
    e.dataTransfer.effectAllowed = 'move';
    e.dataTransfer.setData('text/plain', tabId);
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent, tabId: string) => {
    e.preventDefault();
    if (draggingTab && draggingTab !== tabId) {
      setDragOverTab(tabId);
    }
  }, [draggingTab]);

  const handleDragEnd = useCallback(() => {
    if (draggingTab && dragOverTab) {
      const fromIndex = allTabs.findIndex(t => t.id === draggingTab);
      const toIndex = allTabs.findIndex(t => t.id === dragOverTab);
      if (fromIndex !== -1 && toIndex !== -1 && fromIndex !== toIndex) {
        reorderPanels(activeTabId, fromIndex, toIndex);
      }
    }
    setDraggingTab(null);
    setDragOverTab(null);
  }, [draggingTab, dragOverTab, allTabs, activeTabId, reorderPanels]);

  const handleDuplicateTab = useCallback((tabId: string) => {
    const tab = allTabs.find(t => t.id === tabId);
    if (tab && allTabs.length < MAX_TABS) {
      addTab(`${tab.title} (copy)`);
    }
    closeContextMenu();
  }, [allTabs, addTab, closeContextMenu]);

  const handleCloseOthers = useCallback((tabId: string) => {
    const tabIdsToClose = allTabs.filter(t => t.id !== tabId).map(t => t.id);
    tabIdsToClose.forEach(id => removeTab(id));
    closeContextMenu();
  }, [allTabs, removeTab, closeContextMenu]);

  const handleCloseAll = useCallback(() => {
    const mainTab = allTabs.find(t => t.id === 'main');
    if (mainTab) {
      const tabIdsToClose = allTabs.filter(t => t.id !== 'main').map(t => t.id);
      tabIdsToClose.forEach(id => removeTab(id));
    } else if (allTabs.length > 0) {
      removeTab(allTabs[0].id);
    }
    closeContextMenu();
  }, [allTabs, removeTab, closeContextMenu]);

  return (
    <>
      <div
        style={{
          height: '36px',
          backgroundColor: 'var(--bg-panel-header)',
          borderBottom: '1px solid var(--border-default)',
          display: 'flex',
          alignItems: 'center',
          paddingLeft: 'var(--space-2)',
          gap: '2px',
          flexShrink: 0,
        }}
      >
        {tabs.map((tab) => (
          <div
            key={tab.id}
            ref={(el) => {
              if (el) tabRefs.current.set(tab.id, el);
            }}
            draggable
            onDragStart={(e) => handleDragStart(e, tab.id)}
            onDragOver={(e) => handleDragOver(e, tab.id)}
            onDragEnd={handleDragEnd}
            onClick={() => setActiveTab(tab.id)}
            onContextMenu={(e) => handleContextMenu(e, tab.id)}
            style={{
              height: '28px',
              padding: '0 var(--space-3)',
              display: 'flex',
              alignItems: 'center',
              gap: 'var(--space-2)',
              backgroundColor: tab.id === activeTabId ? 'var(--bg-viewport)' : 'transparent',
              border: tab.id === activeTabId 
                ? '1px solid var(--border-default)' 
                : '1px solid transparent',
              borderBottom: tab.id === activeTabId ? 'none' : '1px solid transparent',
              borderRadius: '4px 4px 0 0',
              cursor: 'grab',
              color: tab.id === activeTabId ? 'var(--text-primary)' : 'var(--text-secondary)',
              fontSize: 'var(--font-size-sm)',
              fontFamily: 'var(--font-sans)',
              position: 'relative',
              marginBottom: tab.id === activeTabId ? '-1px' : 0,
              opacity: draggingTab === tab.id ? 0.5 : 1,
              borderTop: dragOverTab === tab.id ? '2px solid var(--color-info)' : undefined,
              transform: dragOverTab === tab.id ? 'translateY(-2px)' : undefined,
              transition: 'border-top 100ms ease, transform 100ms ease',
            }}
          >
            <span>{tab.title}</span>
            {tabs.length > 1 && (
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  removeTab(tab.id);
                }}
                style={{
                  background: 'none',
                  border: 'none',
                  color: 'var(--text-tertiary)',
                  cursor: 'pointer',
                  padding: '2px',
                  display: 'flex',
                  alignItems: 'center',
                  borderRadius: '2px',
                }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.color = 'var(--text-primary)';
                  e.currentTarget.style.backgroundColor = 'var(--border-default)';
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.color = 'var(--text-tertiary)';
                  e.currentTarget.style.backgroundColor = 'transparent';
                }}
              >
                ×
              </button>
            )}
          </div>
        ))}
        
        {tabs.length < MAX_TABS && (
          <button
            onClick={() => addTab()}
            style={{
              height: '28px',
              width: '28px',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
              backgroundColor: 'transparent',
              border: '1px solid transparent',
              borderRadius: '4px',
              color: 'var(--text-secondary)',
              cursor: 'pointer',
              fontSize: '16px',
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.borderColor = 'var(--border-default)';
              e.currentTarget.style.color = 'var(--text-primary)';
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.borderColor = 'transparent';
              e.currentTarget.style.color = 'var(--text-secondary)';
            }}
          >
            +
          </button>
        )}
      </div>

      {contextMenu && (
        <div
          style={{
            position: 'fixed',
            top: contextMenu.y,
            left: contextMenu.x,
            backgroundColor: 'var(--bg-panel)',
            border: '1px solid var(--border-default)',
            borderRadius: '4px',
            padding: 'var(--space-1)',
            zIndex: 1000,
            minWidth: '150px',
            boxShadow: '0 4px 12px rgba(0, 0, 0, 0.5)',
          }}
        >
          <ContextMenuItem onClick={() => { removeTab(contextMenu.tabId); closeContextMenu(); }}>
            Close
          </ContextMenuItem>
          <ContextMenuItem onClick={() => handleDuplicateTab(contextMenu.tabId)}>
            Duplicate
          </ContextMenuItem>
          <ContextMenuDivider />
          <ContextMenuItem onClick={() => handleCloseOthers(contextMenu.tabId)}>
            Close Others
          </ContextMenuItem>
          <ContextMenuItem onClick={handleCloseAll}>
            Close All
          </ContextMenuItem>
        </div>
      )}
    </>
  );
}

function ContextMenuItem({ 
  children, 
  onClick 
}: { 
  children: React.ReactNode; 
  onClick: () => void;
}) {
  return (
    <div
      onClick={onClick}
      style={{
        padding: 'var(--space-2) var(--space-3)',
        cursor: 'pointer',
        fontSize: 'var(--font-size-sm)',
        color: 'var(--text-primary)',
        borderRadius: '2px',
      }}
      onMouseEnter={(e) => {
        e.currentTarget.style.backgroundColor = 'var(--border-default)';
      }}
      onMouseLeave={(e) => {
        e.currentTarget.style.backgroundColor = 'transparent';
      }}
    >
      {children}
    </div>
  );
}

function ContextMenuDivider() {
  return (
    <div
      style={{
        height: '1px',
        backgroundColor: 'var(--border-default)',
        margin: 'var(--space-1) 0',
      }}
    />
  );
}

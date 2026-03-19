# Crypton — UI/UX Style Guide

This document is the canonical reference for all visual design decisions in the Crypton Monitoring Dashboard. Every component, panel, and layout element must conform to these specifications. The dashboard is the control plane for an autonomous cryptocurrency portfolio management system — it must be information-dense, immediately legible, and technically precise.

---

## Design Philosophy

> *"All signal; no noise."* Every pixel must justify its existence.

The Crypton dashboard draws from F35 cockpit displays and Bloomberg Terminal aesthetics. It is a tool built for developers and operators — exposing technical details is a feature, not a bug. The interface prioritizes:

- **Information density** — Maximum data per viewport with zero scrolling
- **Immediate legibility** — Critical state visible at a glance, color-coded semantics
- **Precision** — Monospace data, tabular number alignment, exact values over approximations
- **Restraint** — No decoration, no gradients, no shadows, no rounded corners. Clean crisp lines only.

---

## Color System

All colors are defined as CSS custom properties on `:root` in `src/Crypton.Web.Dashboard/src/styles/globals.css`.

### Backgrounds

| Token               | Hex       | Usage                              |
| ------------------- | --------- | ---------------------------------- |
| `--bg-viewport`     | `#0a0a0f` | Main viewport / deepest background |
| `--bg-panel`        | `#0f0f1a` | Panel body background              |
| `--bg-panel-header` | `#12121a` | Panel header, status bar, tab bar  |

The background hierarchy creates subtle depth: viewport → panel → header. Never use pure black (`#000`).

### Borders

| Token              | Hex       | Usage                                         |
| ------------------ | --------- | --------------------------------------------- |
| `--border-default` | `#1a1a2e` | Panel borders, dividers, separators           |
| `--border-active`  | `#2a2a4e` | Focused/active panel borders, scrollbar hover |

Border width: **1px** for standard panels, **2px** for active/focused state. Border radius: **0px** (crisp corners). Maximum allowed radius is **2px** for small inset elements like progress bars, scrollbar thumbs, and code blocks.

### Text

| Token              | Hex       | Usage                                       |
| ------------------ | --------- | ------------------------------------------- |
| `--text-primary`   | `#e0e0e0` | Primary content — prices, values, headings  |
| `--text-secondary` | `#808090` | Labels, metadata, supporting text           |
| `--text-tertiary`  | `#606070` | Disabled states, placeholders, "Loading..." |

Never use pure white (`#fff`) for text. The off-white primary creates comfortable contrast against the dark background without causing eye strain.

### Semantic Colors

These carry meaning. Do not use them decoratively.

| Token             | Hex                      | Meaning                                                            |
| ----------------- | ------------------------ | ------------------------------------------------------------------ |
| `--color-profit`  | `#00ffc8`                | Gains, upward movement, positive outcome, defensive posture        |
| `--color-loss`    | `#ff4466`                | Losses, downward movement, negative outcome, `exit_all`, live mode |
| `--color-warning` | `#ffaa00`                | Warnings, attention required, degraded state, aggressive posture   |
| `--color-info`    | `#4488ff`                | Information, highlights, links, paper mode, moderate posture       |
| `--color-active`  | `#00ff88`                | Active/running state — pulsing indicators, connected status        |
| `--color-idle`    | `#666680`                | Idle/waiting/inactive state, flat posture                          |
| `--color-glow`    | `rgba(0, 255, 200, 0.2)` | Panel attention glow — border + box-shadow accent                  |

### Glow System

Panels can pulse with a colored border glow to draw attention. Glow colors map to severity:

| Level     | Color                  |
| --------- | ---------------------- |
| `info`    | `var(--color-info)`    |
| `warning` | `var(--color-warning)` |
| `error`   | `var(--color-loss)`    |
| `success` | `var(--color-profit)`  |

Glow is applied via `border-color` and `box-shadow` with a `150ms` transition. The border animation for attention uses a **1s loop** pulse.

### Derived Alpha Colors

When a semantic color is used for backgrounds (e.g., service status chips, badges), apply alpha:

- **Border tint:** `{color}22` (≈ 13% opacity)
- **Background tint:** `{color}11` (≈ 7% opacity)
- **Flash highlight:** `{color} at 0.3 opacity` (used in price flash animations)

### Color Mapping Reference

| Domain                  | Context      | Color                               |
| ----------------------- | ------------ | ----------------------------------- |
| **Price movement**      | Up / gain    | `--color-profit`                    |
| **Price movement**      | Down / loss  | `--color-loss`                      |
| **Direction indicator** | ▲ triangle   | `--color-profit`                    |
| **Direction indicator** | ▼ triangle   | `--color-loss`                      |
| **Connection status**   | Connected    | `--color-active`                    |
| **Connection status**   | Connecting   | `--color-warning`                   |
| **Connection status**   | Disconnected | `--color-loss`                      |
| **Service health**      | Online       | `--color-active`                    |
| **Service health**      | Degraded     | `--color-warning`                   |
| **Service health**      | Offline      | `--color-loss`                      |
| **Strategy posture**    | Aggressive   | `--color-warning`                   |
| **Strategy posture**    | Moderate     | `--color-info`                      |
| **Strategy posture**    | Defensive    | `--color-profit`                    |
| **Strategy posture**    | Flat         | `--text-secondary`                  |
| **Strategy posture**    | Exit all     | `--color-loss`                      |
| **Trading mode**        | Paper        | `--color-info`                      |
| **Trading mode**        | Live         | `--color-loss`                      |
| **Agent state**         | Running      | `--color-active`                    |
| **Agent state**         | Idle         | `--color-idle`                      |
| **Progress bar**        | Fill         | `--color-info`                      |
| **Progress bar**        | Track        | `--border-default`                  |
| **Search highlight**    | Matched text | `--color-info` at `fontWeight: 700` |

---

## Typography

### Font Families

| Token         | Stack                                                    | Usage                                                                      |
| ------------- | -------------------------------------------------------- | -------------------------------------------------------------------------- |
| `--font-mono` | `'JetBrains Mono', 'Fira Code', monospace`               | All numeric data, prices, percentages, code, technical values, asset names |
| `--font-sans` | `'Inter', -apple-system, BlinkMacSystemFont, sans-serif` | Labels, descriptions, headings, body text, UI chrome                       |

Fonts are loaded from Google Fonts with weights **400**, **500**, and **600** for both families:

```html
<link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&family=JetBrains+Mono:wght@400;500;600&display=swap" rel="stylesheet">
```

### Font Sizes

| Token            | Size   | Usage                                        |
| ---------------- | ------ | -------------------------------------------- |
| `--font-size-xs` | `11px` | Labels, status bar text, tertiary metadata   |
| `--font-size-sm` | `12px` | Default data size, panel content, tab titles |
| `--font-size-md` | `13px` | Emphasized data                              |
| `--font-size-lg` | `14px` | Primary values (current price, state name)   |
| `--font-size-xl` | `16px` | Large display values (portfolio total)       |

Code blocks use `11px` (`--font-size-xs`).

### Font Weights

| Weight | Usage                                                                              |
| ------ | ---------------------------------------------------------------------------------- |
| `400`  | Data values, body text                                                             |
| `500`  | Labels, secondary headings                                                         |
| `600`  | Panel headers, primary values (prices, state names), search match highlights (700) |

### Numeric Display

All numeric data must use:
- `font-family: var(--font-mono)`
- `font-variant-numeric: tabular-nums` (via `.tabular-nums` utility class)

This ensures columns of numbers align vertically.

### Text Rendering

```css
-webkit-font-smoothing: antialiased;
-moz-osx-font-smoothing: grayscale;
```

Applied globally on `body`.

### Line Height

Compact: **1.2** for data-dense displays. Use the browser default only in longer text blocks.

### Tab Titles

- `12px`, truncated with ellipsis
- Maximum width constrained by tab bar space

---

## Spacing System

All spacing is based on a **4px baseline grid**. Every margin, padding, and gap value must be a multiple of 4.

| Token       | Value  |
| ----------- | ------ |
| `--space-1` | `4px`  |
| `--space-2` | `8px`  |
| `--space-3` | `12px` |
| `--space-4` | `16px` |
| `--space-5` | `20px` |
| `--space-6` | `24px` |
| `--space-8` | `32px` |

### Layout Dimensions

| Token                   | Value  | Usage                                       |
| ----------------------- | ------ | ------------------------------------------- |
| `--panel-header-height` | `28px` | Panel header bars                           |
| `--panel-gap`           | `4px`  | Gutter between panels in the grid           |
| `--panel-padding`       | `8px`  | Internal padding within panel content areas |

Status bar height: **24px**.

---

## Layout Architecture

### Viewport

The application fills the full viewport (`100vh × 100vw`) with `overflow: hidden`. No scrolling at the page level — ever.

```
┌──────────────────────────────────────────────────┐
│  Tab Bar (28px)                              [+]  │
├──────────────────────────────────────────────────┤
│                                                    │
│              Panel Grid (fill)                     │
│                                                    │
│   ┌─────────┐  ┌─────────┐  ┌─────────┐          │
│   │ Panel   │  │ Panel   │  │ Panel   │          │
│   │         │  │         │  │         │          │
│   └─────────┘  └─────────┘  └─────────┘          │
│   ┌─────────┐  ┌──────────────────────┐          │
│   │ Panel   │  │ Panel               │          │
│   └─────────┘  └──────────────────────┘          │
│                                                    │
├──────────────────────────────────────────────────┤
│  Status Bar (24px)                                │
└──────────────────────────────────────────────────┘
```

### Tab System

- VS Code-style tabs at the top
- Maximum **8 tabs** per workspace
- Tabs are draggable (drag-and-drop reordering)
- Right-click context menu: close, close others, close all, duplicate
- `[+]` button adds a new empty workspace tab
- Each tab contains its own independent panel layout

### Panel Grid

- Panels sit on an **8px snap-to-grid** system (`GRID_SIZE = 8`)
- Minimum panel size: **200px wide × 100px tall**
- Panels are positioned absolutely within the grid container
- Drag by header to reposition; drag edges/corners to resize

### Panel Anatomy

Every panel follows this structure:

```
┌── Panel Header (28px) ─────────────────────┐
│  Title         [metric]    [◥] [□] [×]     │
├─────────────────────────────────────────────┤
│                                             │
│  Panel Content (padded 8px)                 │
│                                             │
└─────────────────────────────────────────────┘
```

**Header elements:**
- **Title** — left-aligned, `--font-sans`, weight 500
- **Key metric** — inline summary value (e.g., "$127k", "BTC ▲")
- **Collapse button** (`◥`/`◢`) — toggles between full and collapsed (28px summary bar)
- **Maximize button** (`□`/`❐`) — double-click header or click to fill viewport
- **Close button** (`×`) — removes panel from current tab

**Panel background:** `var(--bg-panel)` with `1px solid var(--border-default)` border.  
**Panel header background:** `var(--bg-panel-header)`.

### Panel Collapse

Collapsed panels shrink to a **28px summary bar** showing icon, title, and a key metric. Click to restore. When one panel is maximized, others auto-collapse if space is needed.

### Command Palette

Activated by **⌘K / Ctrl+K**. Appears as a centered overlay with:
- Text input with fuzzy search (all-token matching)
- Command categories: Add Panel, Navigate, Settings, Actions, Agent
- Matched text highlighted in `--color-info` with `fontWeight: 700`
- **ESC** closes the palette
- Animations: scale + fade at `150ms`

Panel add commands are organized by service domain:
- **Agent:** State, Loop, Reasoning Trace, Tool Calls, etc.
- **Execution:** Portfolio Summary, Holdings, Strategy Overview, etc.
- **Market:** Price Ticker, Price Chart, Technical Indicators, etc.
- **System:** Status, Diagnostics, Connection Health, Error Log

---

## Component Patterns

### Data Rows

The most common pattern in panels: a label-value pair displayed on a single row.

```tsx
<div style={{
  display: 'flex',
  justifyContent: 'space-between',
  fontSize: 'var(--font-size-xs)',
  color: 'var(--text-secondary)'
}}>
  <span>Label</span>
  <span style={{ fontFamily: 'var(--font-mono)' }}>Value</span>
</div>
```

Rows stack vertically with `gap: var(--space-1)` or `var(--space-2)`.

### Status Indicators

A colored dot followed by a text label:

```tsx
<div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-2)' }}>
  <div style={{
    width: '8px',
    height: '8px',
    borderRadius: '50%',
    backgroundColor: stateColor,
  }} />
  <span style={{ fontFamily: 'var(--font-mono)', fontWeight: 600 }}>
    {label}
  </span>
</div>
```

- **Running/active:** `8px` dot with `pulse 1.5s infinite` animation
- **Idle/inactive:** `8px` dot, no animation
- **Status bar dots:** `6px` for dashboard connection, `5px` for service chips

### Service Chips

Small inline badges in the status bar:

```tsx
<div style={{
  display: 'flex',
  alignItems: 'center',
  gap: '4px',
  padding: '1px 5px',
  borderRadius: '3px',
  border: `1px solid ${color}22`,
  backgroundColor: `${color}11`,
}}>
  <div style={{ width: '5px', height: '5px', borderRadius: '50%', backgroundColor: color }} />
  <span style={{ fontFamily: 'var(--font-mono)', color: 'var(--text-secondary)' }}>{label}</span>
</div>
```

Service abbreviations: **MD** (Market Data), **ES** (Execution Service), **AR** (Agent Runner).

### Progress Bars

```tsx
{/* Track */}
<div style={{ height: '4px', backgroundColor: 'var(--border-default)', borderRadius: '2px', overflow: 'hidden' }}>
  {/* Fill */}
  <div style={{ height: '100%', width: `${percent}%`, backgroundColor: 'var(--color-info)', transition: 'width 0.3s ease' }} />
</div>
```

### Section Dividers

Use a top border on a new section within a panel:

```tsx
<div style={{ marginTop: 'var(--space-2)', paddingTop: 'var(--space-2)', borderTop: '1px solid var(--border-default)' }}>
```

### Loading States

Panels without data display a centered "Loading..." in `--text-tertiary`:

```tsx
<div style={{ color: 'var(--text-tertiary)' }}>Loading...</div>
```

### Price Display

- Large price: `--font-mono`, `--font-size-lg`, weight `600`, `--text-primary`
- Change indicator: `▲`/`▼` triangle + percentage, colored by direction (profit/loss)
- Bid/ask spread: `--font-size-xs`, `--text-tertiary`, monospace values

```tsx
{isPositive ? '▲' : '▼'} {formattedChange}
```

### Code Blocks

Use `react-syntax-highlighter` with the `vscDarkPlus` theme. Container:
- `backgroundColor: var(--bg-viewport)`
- `borderRadius: 4px`
- `fontSize: 11px`
- `fontFamily: var(--font-mono)`
- `maxHeight: 300px` with overflow scroll
- Line numbers in `--text-tertiary` with `userSelect: none`

### Error Toasts

Bottom-right positioned notifications:
- Slide in from the right (`translateX(100%)` → `translateX(0)`) over `200ms ease-out`
- Dismiss on click or auto-expire

### Action Controls (Buttons & Inputs)

Compact operator-action buttons used in panel footers (e.g., System Diagnostics service controls).

```tsx
// Base styles — identical for enabled and disabled (disabled adds opacity + cursor).
const CTL_BTN: React.CSSProperties = {
    background: 'none',
    border: '1px solid var(--border-default)',
    borderRadius: '2px',
    padding: '0 6px',
    lineHeight: '18px',
    fontSize: '11px',
    fontFamily: 'var(--font-mono)',
    cursor: 'pointer',
    color: 'var(--text-secondary)',
    whiteSpace: 'nowrap',
};
// Disabled variant — never change border/color; use opacity only.
const CTL_BTN_DISABLED: React.CSSProperties = { ...CTL_BTN, opacity: 0.4, cursor: 'default' };
```

Button label conventions:
- All **lowercase**, no title-case (`start` not `Start`, `promote live` not `Promote Live`)
- Destructive / mode-change actions (e.g., `promote live`, `degrade`) sit in a separate row from safe actions

Inline text inputs paired with action buttons:

```tsx
const CTL_INPUT: React.CSSProperties = {
    background: 'var(--bg-main)',
    color: 'var(--text-primary)',
    border: '1px solid var(--border-default)',
    borderRadius: '2px',
    padding: '0 6px',
    lineHeight: '18px',
    fontSize: '11px',
    fontFamily: 'var(--font-mono)',
    width: '160px',
};
```

Layout rules:
- Use `display: flex; gap: 4px; flexWrap: wrap; alignItems: center` for button rows
- Group logically: safe actions in one row, each destructive/mode-change action (with its reason input) in its own row
- Action feedback ("X completed" / "X failed") is shown inline in the panel header bar, not adjacent to the button

---

## Animation & Motion

### Timing Tokens

| Token                 | Duration         | Usage                                               |
| --------------------- | ---------------- | --------------------------------------------------- |
| `--transition-fast`   | `150ms ease-out` | Border color, box-shadow, command palette           |
| `--transition-normal` | `200ms ease-out` | Panel appear/disappear, price flash, toast slide-in |
| `--transition-slow`   | `300ms ease-out` | Panel resize                                        |

### Keyframe Animations

| Animation     | Duration          | Usage                                                       |
| ------------- | ----------------- | ----------------------------------------------------------- |
| `flash-green` | `200ms ease-out`  | Price tick up — `rgba(0, 255, 200, 0.3)` → `transparent`    |
| `flash-red`   | `200ms ease-out`  | Price tick down — `rgba(255, 68, 102, 0.3)` → `transparent` |
| `slide-in`    | `200ms ease-out`  | Error toast entry — `translateX(100%)` → `translateX(0)`    |
| `pulse`       | `1.5–2s infinite` | Active agent/strategy running indicator dot                 |

### Motion Principles

- **Data-driven motion:** Animation conveys information (price direction, state change), never decoration
- **Brief and unobtrusive:** 150–200ms for interactions, 200–300ms for layout changes
- **Smooth number transitions:** Price updates use color flash, not abrupt replacement
- **Chart real-time draw:** Smooth line extension as new data arrives

### Reduced Motion

All animations are disabled when `prefers-reduced-motion: reduce` is detected:

```css
@media (prefers-reduced-motion: reduce) {
  * {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

The `useReducedMotion()` hook is available for component-level checks (e.g., disabling the pulse animation on indicator dots).

---

## Scrollbars

Minimal, unobtrusive scrollbars for panels with overflow:

```css
::-webkit-scrollbar        { width: 6px; height: 6px; }
::-webkit-scrollbar-track  { background: transparent; }
::-webkit-scrollbar-thumb  { background: var(--border-default); border-radius: 3px; }
::-webkit-scrollbar-thumb:hover { background: var(--border-active); }
```

---

## Focus & Selection

- **Focus ring:** `2px solid var(--color-info)` with `outline-offset: 2px` — only on `:focus-visible`
- **Text selection:** `background: var(--color-info)`, `color: var(--text-primary)`

---

## Styling Approach

The dashboard uses **CSS custom properties** for design tokens and **inline styles** (React `style` objects) for component styling. There is no Tailwind, CSS Modules, or SCSS.

### Rules

1. **All colors, fonts, spacing, and layout dimensions** must reference CSS custom properties — never hardcode hex values in components.
2. **Utility classes** (`.mono`, `.text-profit`, `.text-loss`, `.text-warning`, `.text-info`, `.text-secondary`, `.tabular-nums`) are defined in `globals.css` and may be used via `className`.
3. **Animation classes** (`.price-flash-up`, `.price-flash-down`, `.animate-slide-in`) are defined in `globals.css`.
4. **No shadows, no gradients, no rounded corners** on panels. The visual hierarchy comes from background color layering and border contrast.
5. **Spacing must be a multiple of 4px.** Use the `--space-*` tokens.

---

## Number Formatting

| Type             | Format                   | Example            |
| ---------------- | ------------------------ | ------------------ |
| USD price (≥ $1) | 2 decimal places         | `$67,432.10`       |
| USD price (< $1) | 4 decimal places         | `$0.5234`          |
| Percentage       | 2 decimal places, signed | `+2.45%`, `-1.30%` |
| Token count      | Locale-formatted integer | `12,345`           |
| Duration         | `Xh Ym` or `Xd Yh`       | `2h 34m`, `3d 5h`  |
| Latency          | Integer milliseconds     | `142ms`            |

Use `Intl.NumberFormat` for currency and locale-aware formatting.

---

## Accessibility

- All interactive elements must be keyboard-navigable
- `:focus-visible` ring on focusable elements
- `prefers-reduced-motion` honored globally and per-component
- WCAG contrast: all primary text (`#e0e0e0`) on dark backgrounds exceeds 4.5:1
- `title` attributes on abbreviated elements (service chips, truncated labels)
- `data-testid` attributes on all testable elements

---

## File Reference

| File                                                                 | Purpose                                           |
| -------------------------------------------------------------------- | ------------------------------------------------- |
| `src/Crypton.Web.Dashboard/src/styles/globals.css`                   | Design tokens, reset, utility classes, animations |
| `src/Crypton.Web.Dashboard/index.html`                               | Font loading (Google Fonts)                       |
| `src/Crypton.Web.Dashboard/src/components/panels/PanelGrid.tsx`      | Grid system, snap, drag, resize, glow             |
| `src/Crypton.Web.Dashboard/src/components/layout/TabBar.tsx`         | Tab bar with drag-reorder, context menu           |
| `src/Crypton.Web.Dashboard/src/components/layout/StatusBar.tsx`      | Service chips, connection status                  |
| `src/Crypton.Web.Dashboard/src/components/layout/CommandPalette.tsx` | ⌘K command palette with fuzzy search              |
| `src/Crypton.Web.Dashboard/src/components/CodeBlock.tsx`             | Syntax-highlighted code viewer                    |
| `src/Crypton.Web.Dashboard/src/hooks/useReducedMotion.ts`            | Reduced motion preference hook                    |
| `src/Crypton.Web.Dashboard/src/hooks/usePriceFlash.ts`               | Price change flash animation hook                 |

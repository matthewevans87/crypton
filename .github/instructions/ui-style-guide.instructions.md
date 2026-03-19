---
applyTo: "src/Crypton.Web.Dashboard/**/*.{tsx,ts,css,scss,html,svg}"
description: "Use when creating, modifying, or reviewing UI components, styles, layouts, panels, or visual elements in the Crypton dashboard. Covers colors, typography, spacing, animation, component patterns, and accessibility."
---

# Crypton Dashboard — UI Style Guide

Before writing or modifying any dashboard UI code, **read `docs/style_guide.md`** for the full specification.

If you only need a subset, read the relevant section(s) based on what you're working on:

| Task | Section to read |
|------|-----------------|
| Colors, theming, semantic meaning | **Color System** (tokens, semantic colors, glow system, color mapping reference) |
| Fonts, sizes, weights, numbers | **Typography** |
| Margins, padding, gaps, grid | **Spacing System** |
| Panels, tabs, grid, viewport | **Layout Architecture** |
| Data rows, indicators, chips, bars | **Component Patterns** |
| Action buttons, inline inputs, operator controls | **Component Patterns > Action Controls** |
| Transitions, keyframes, flash | **Animation & Motion** |
| Inline styles, CSS variables, classes | **Styling Approach** |
| Currency, percentages, durations | **Number Formatting** |
| Focus, contrast, reduced motion | **Accessibility** |

## Quick Rules (always apply)

- **All colors** via CSS custom properties (`var(--color-*)`, `var(--bg-*)`, `var(--text-*)`) — never hardcode hex in components.
- **All spacing** in multiples of 4px using `var(--space-*)` tokens.
- **Numeric data** always uses `font-family: var(--font-mono)` with `tabular-nums`.
- **No shadows, no gradients, no rounded corners** on panels. Max border-radius: 2px for small inset elements.
- **Inline styles** with CSS variable references — no Tailwind, no CSS Modules, no SCSS in components.
- **`prefers-reduced-motion`** must be respected — use the `useReducedMotion()` hook for conditional animation.
- **Panel anatomy**: 28px header (`--bg-panel-header`), 8px content padding (`--bg-panel`), 1px border (`--border-default`).

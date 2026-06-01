# Decision: PMR Content Manager Logo Redesign

**Date:** 2026-05-31T21:36:43-04:00  
**Author:** Slit (UI Dev)  
**Status:** Implemented

---

## Problem

The original "Checkered Mod" logo (a 2×2 racing-flag grid) was rejected by Elliott. He requested a design inspired by the **AMS2 Content Manager** aesthetic but branded for **PMR (Project Motor Racing)**.

## Research: AMS2/AC Content Manager Design Language

Assetto Corsa Content Manager uses:
- **Circular badge emblem** — clean, professional utility feel
- **Dashboard/gauge motifs** — speedometer, tachometer imagery
- **Bold monogram typography** — "CM" prominently featured
- **Speed indicators** — chevrons, motion lines, dynamic angles
- **Dark metallic palette** — blacks, greys, accent colors

## Decision: PMR Gauge Badge

**Design concept:** A circular motorsport badge featuring a stylized speedometer gauge, evoking a racing dashboard. The needle points toward "high speed" to suggest performance and motion.

### Visual elements:
1. **Outer ring** — gradient border (#45475a → #313244) for badge/emblem feel
2. **Inner disc** — dark gradient background (#1e1e2e → #11111b)
3. **Gauge arc** — 180° speedometer arc in AccentBrush (#89B4FA)
4. **Tick marks** — five marks around the arc (semi-transparent)
5. **Gauge needle** — pointing top-right ("high speed")
6. **Pivot hub** — center circle with dark inner dot
7. **Racing chevrons** — three right-facing chevrons at bottom (motion indicator)

### Color palette (Catppuccin Mocha):
- AccentBrush: #89B4FA (gauge, needle, chevrons)
- Surface0Brush: #313244 (outer ring)
- MantleBrush: #181825 / #1e1e2e (background)
- Semi-transparent: #7089B4FA, #9989B4FA

### Technical:
- ViewBox: 64×64 (scales cleanly to 16–48px)
- SVG source of truth: `src/EWSR_PMR_ModApp.UI/Assets/logo.svg`
- WPF resource: `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` (`LogoDrawingImage` key)
- Toolbar Image: 30×30 (bumped from 28×28)

## Why Not Copy AMS2 CM?

The design takes **inspiration** from Content Manager's visual language (circular badge, dashboard motifs, speed indicators) but is an **original composition** for PMR. No trademark or copyright issues — the speedometer gauge is a generic motorsport symbol.

## Files Changed

| File | Change |
|------|--------|
| `src/EWSR_PMR_ModApp.UI/Assets/logo.svg` | Replaced checkered grid with gauge badge design |
| `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` | Updated DrawingImage to match new SVG |
| `src/EWSR_PMR_ModApp.UI/MainWindow.xaml` | Image size bumped to 30×30 |

## Verification

- `dotnet build` — succeeds
- `dotnet test` — 164 passed, 0 failed
- SVG opens in browser — renders correctly

## Supersedes

Previous decision: "Checkered Mod" logo (decisions.md, 2026-05-31T21:21:54-04:00)

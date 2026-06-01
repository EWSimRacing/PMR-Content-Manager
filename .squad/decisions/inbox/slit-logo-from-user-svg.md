# Decision: User-supplied PMR/CM wordmark supersedes gauge-badge logo

**Date:** 2026-05-31T21:58:13-04:00
**Author:** Slit (UI Dev)
**Status:** Implemented

---

## Problem

The "PMR Gauge Badge" circular badge design (implemented 2026-05-31T21:36:43-04:00) was superseded when Elliott provided his own finished logo artwork:

- **Source file:** `C:\Users\Ellio\Downloads\PMR_CM_letters_only.svg`
- **Design:** PMR stacked over CM; letters-only; transparent background; no badge or emblem.
- **Colors:** asphalt black `#050505` · pit-wall white `#FFFFFF` · brass gold `#B99A5D`.
- **ViewBox:** 1200×800 (3:2 aspect ratio).

Elliott owns this artwork. No trademark or copyright concerns.

---

## Decision: Use Elliott's SVG as the canonical app logo

### In-app logo (WPF DrawingImage)

- **Source of truth:** `src/EWSR_PMR_ModApp.UI/Assets/logo.svg` — Elliott's SVG, copied verbatim.
- **WPF resource:** `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` — `DrawingImage` with key `LogoDrawingImage`.
- **Translation approach:** Each SVG `<g transform="translate(tx,ty) scale(1,-1)">` layer is a `DrawingGroup` with `<MatrixTransform Matrix="1,0,0,-1,tx,ty"/>`. The SVG uses a 4-layer stroke technique (gold glow → white thick outline → black fill → gold hairline) which is reproduced faithfully. Root `DrawingGroup` clips to 1200×800 to preserve 3:2 aspect ratio. `FillRule="Nonzero"` matches SVG default.
- **Toolbar Image:** `Height="28" Stretch="Uniform"` — no fixed Width; auto-sizes to ~42 px wide (3:2). This prevents squishing on the wordmark.

### Windows desktop/taskbar icon (app.ico)

- **Generated via:** `tools/IconGen` — throwaway .NET console project (net10.0-windows, UseWPF=true). Not in `EWSR_PMR_ModApp.slnx`.
- **Technique:** `RenderTargetBitmap` (Pbgra32, transparent) on an STA thread; renders the `DrawingGroup` at 6 sizes (16, 32, 48, 64, 128, 256 px); each size encoded as PNG via `PngBitmapEncoder`; assembled into a multi-resolution PNG-compressed `.ico` using manually written ICONDIR/ICONDIRENTRY headers.
- **Result:** `src/EWSR_PMR_ModApp.UI/Assets/app.ico` — 45,083 bytes, 6 entries.
- **Rationale for tooling:** No ImageMagick or Inkscape is installed. WPF's own rendering pipeline produces faithful, high-quality output from the same DrawingGroup used in the UI.

### Icon wiring

| Mechanism | File | Change |
|-----------|------|--------|
| `.exe` icon (desktop/Explorer) | `EWSR_PMR_ModApp.UI.csproj` | `<ApplicationIcon>Assets\app.ico</ApplicationIcon>` |
| WPF pack-URI runtime resource | `EWSR_PMR_ModApp.UI.csproj` | `<Resource Include="Assets\app.ico"/>` |
| Title-bar + taskbar HICON | `MainWindow.xaml` | `Icon="Assets/app.ico"` on `<Window>` |

The old "TODO: ship a .ico" comment in Logo.xaml and MainWindow.xaml has been removed.

---

## Files Changed

| File | Change |
|------|--------|
| `src/EWSR_PMR_ModApp.UI/Assets/logo.svg` | Replaced gauge-badge SVG with Elliott's wordmark (verbatim copy) |
| `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` | Rebuilt DrawingImage to render the wordmark; `LogoDrawingImage` key preserved |
| `src/EWSR_PMR_ModApp.UI/Assets/app.ico` | New: multi-resolution icon (16/32/48/64/128/256 px PNG-compressed) |
| `src/EWSR_PMR_ModApp.UI/EWSR_PMR_ModApp.UI.csproj` | Added `ApplicationIcon` + `<Resource>` for app.ico |
| `src/EWSR_PMR_ModApp.UI/MainWindow.xaml` | `Icon="Assets/app.ico"` on Window; Image size → `Height="28" Stretch="Uniform"` |
| `tools/IconGen/IconGen.csproj` | New throwaway generator (not in solution) |
| `tools/IconGen/Program.cs` | New throwaway generator source |

---

## Verification

- `Assets/app.ico` exists, size 45,083 bytes, 6 PNG-compressed entries (16–256 px) ✓
- `dotnet build` EWSR_PMR_ModApp.slnx — 0 errors, 0 warnings ✓
- `dotnet test` — 164 passed, 0 failed ✓
- Toolbar Image uses `Stretch="Uniform"` with single Height constraint ✓

## Supersedes

- Decision: "PMR Gauge Badge Logo" (decisions.md, 2026-05-31T21:36:43-04:00)
- Decision: "App logo — Checkered Mod" (decisions.md, 2026-05-31T21:21:54-04:00)

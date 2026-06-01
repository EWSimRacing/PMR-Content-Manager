# Decisions

Canonical decision ledger for EWSR_PMR_ModApp. Append-only. Scribe merges entries from decisions/inbox/.

For archived decisions, see decisions-archive/.

---

## Decision: FileClassifier must honour modInfo.Files explicit mappings

**Date:** 2026-05-31  
**Author:** Nux  
**Status:** Implemented & tested

### Problem

`FileClassifier.Classify()` applied extension/path policy without ever consulting `modInfo.Files` (the explicit source→target install-mapping dictionary in `modinfo.json`). A file such as `custom_shader.fx` — unrecognised extension, no `data/` prefix — was classified `NoPathMatch` even when the mod author explicitly listed it in `modinfo.Files`. This silently dropped files that mod authors expected to install.

### Decision

Add a dedicated check in `FileClassifier.Classify()` **after** the MetaFile guard and **before** any extension-based policy:

```csharp
// 5b. modinfo.json explicit Files mapping → Install (wins before extension policy)
if (modInfo?.Files is { Count: > 0 } && modInfo.Files.ContainsKey(zipPath))
{
    reason = null;
    return SkipCategory.Install;
}
```

**Rationale for placement:**
- Runs *after* `SkipFiles` — a user exclusion still wins even over an explicit Files entry (author intent + safety).
- Runs *after* `DisplayFiles` — allows a DisplayFiles entry to shadow a Files entry if intentionally set (unlikely but consistent).
- Runs *after* `UnsafeFile` / `PackagingArtifact` / `MetaFile` — hard security/correctness checks are non-negotiable.
- Runs *before* all extension checks — modinfo is authoritative over heuristics.

### Files Changed

| File | Change |
|------|--------|
| `src/EWSR_PMR_ModApp.Core/ZipHandling/FileClassifier.cs` | Added check 5b (7 lines) |
| `tests/EWSR_PMR_ModApp.Core.Tests/ZipHandling/FileClassifierTests.cs` | Removed `Skip` attribute from `ModinfoFiles_ExplicitEntry_IsInstall_EvenIfExtensionWouldBeNoPathMatch` |

### Test Result

112 passed, 0 failed, 0 skipped.

---

## Decision: Elevated Drag-Drop — Root Cause and Fix Direction

**Date:** 2026-05-31T19:57:03-04:00  
**Author:** Slit (UI Dev)  
**Status:** Proposed — pending Elliott's runtime log data

---

### Problem

Drag-and-drop of a .zip from Windows Explorer onto the app's drop zone shows the ⊘ (no-drop) cursor and never completes the drop. This persists even when the app is running as Administrator. Three rounds of fixes (XAML wiring, ChangeWindowMessageFilterEx calls, AllowDrop placement) have all been committed but the issue remains live.

### Root Cause Finding

The prior fixes addressed real but secondary problems (missing handlers on DropZoneBorder, missing AllowDrop on the drop target). Those were legitimate bugs and have been fixed correctly.

The remaining failure, specifically in the **elevated (Restart as Administrator)** scenario, is almost certainly caused by a deeper UIPI limitation:

**`ChangeWindowMessageFilterEx` only unblocks the legacy `WM_DROPFILES` Win32 path.**  
WPF drag-and-drop is **OLE-based**: it uses `RegisterDragDrop` / `IDropTarget` / `IDataObject` through COM cross-process marshaling. When the app is elevated (High integrity) and the drag source (Explorer) is not elevated (Medium integrity), **UIPI blocks the COM channel entirely**. This is a separate mechanism from the WM_DROPFILES message filter and cannot be fixed by `ChangeWindowMessageFilterEx` alone.

Evidence: Both prior attempts applied the message filter correctly (correct HWND, correct timing in `OnSourceInitialized`, all three message IDs) — and the drag still failed. The new diagnostic logging (round 3) will confirm definitively:

- **If `DragEnter`/`DragOver` NEVER appear in the log while elevated**: OLE channel is blocked. Message filter is irrelevant for WPF drag-drop.
- **If handlers DO fire but `FileDrop` is absent or `IsZipDrop=False`**: data serialization issue (unlikely).
- **If handlers fire and `IsZipDrop=True`**: logic issue in the handler (unlikely given code review).

### Decision: Non-Elevated UI + Elevated Helper Process

#### Chosen direction

**The UI process should run non-elevated (asInvoker, as it does today)** for the normal install flow. Explorer→OLE drag-drop works perfectly at medium integrity. The app should NOT require elevation to accept a file drop.

The elevation requirement comes solely from writing files into `C:\Program Files\Project Motor Racing\data`. This should be delegated to a **minimal elevated helper process** rather than elevating the whole UI.

#### Architecture (to be implemented in a follow-up task)

```
EWSR_PMR_ModApp.UI.exe (asInvoker, Medium IL)
  └─ accepts OLE drag-drop from Explorer ✓
  └─ resolves mappings, shows dialogs ✓
  └─ when writes to Program Files are needed:
       └─ spawns EWSR_PMR_ModApp.Installer.exe (requireAdministrator manifest)
            └─ receives file list + target mappings via named pipe or temp manifest
            └─ performs the actual file copies / backup / manifest writes
            └─ returns result + any errors
            └─ exits
```

**EWSR_PMR_ModApp.Installer.exe** would be a minimal console/windowless process with a `requireAdministrator` manifest that:
1. Reads a JSON "install job" from a named pipe or a temp file
2. Performs the file writes
3. Writes a JSON result back
4. Exits

The UI stays visible and responsive throughout; the helper is a short-lived process.

#### Why not keep trying to unblock elevated OLE drag-drop?

- No public, documented, reliable API exists for this on Windows
- `ChangeWindowMessageFilterEx` is documented and has been applied correctly; it does not help WPF OLE drag-drop
- Dropping to Win32 `DragAcceptFiles` would require replacing WPF's drag system and breaking the existing, working non-elevated flow
- The non-elevated-UI + helper pattern is the same architecture used by Windows installers (e.g., MSI, Inno Setup) and is the correct solution

#### What works today (non-elevated)

When the app is NOT elevated, OLE drag-drop works: `DragEnter`/`DragOver`/`Drop` fire, `FileDrop` is present, `IsZipDrop` returns true. The diagnostic log will confirm this.

The UX impact of the helper approach: the user sees a UAC prompt once when they drop a zip (the helper process elevates). This is better than requiring the entire app to stay elevated.

### Short-term mitigation (already in place)

- Diagnostic logging writes to `%TEMP%\ewsr_dragdrop.log` — Elliott can capture exact data
- `IsHitTestVisible="False"` on decorative TextBlocks in the drop zone — belt-and-suspenders hit-test hardening
- `ChangeWindowMessageFilterEx` calls retained (they are harmless and correct for completeness)
- Clear `TODO` comment in `MainWindow.xaml.cs` `OnSourceInitialized` pointing to the helper-process architecture

### Next steps

1. Elliott runs the app (non-elevated AND elevated) and reports the log contents
2. If log confirms "handlers never fire when elevated" → proceed with helper-process implementation
3. If log reveals something unexpected → reassess
4. Follow-up task: implement `EWSR_PMR_ModApp.Installer.exe` with named-pipe IPC

# Decision: Home navigation + app logo (nav pattern + branding)

**Date:** 2026-05-31T21:21:54-04:00
**Author:** Slit (UI Dev)
**Status:** Implemented

---

## Home navigation pattern

### Problem
When the user opened Settings there was no obvious return path — the only way back was to click ⚙ Settings again (toggling), which is non-obvious.

### Decision
- Added `HomeCommand` (RelayCommand, `IsSettingsVisible = false`) to `MainViewModel`.
- Added a `🏠  Home` button in the toolbar right StackPanel, **before** Re-check and Settings.
- `Visibility` bound to `IsSettingsVisible` with `BoolToVisConverter` → button is **invisible on the home screen** and **visible only when in Settings**.
- `IsEnabled` bound to `IsNotBusy` — consistent with every other toolbar button.

### Rationale
Toggling ToggleSettingsCommand was kept for the Settings button (it already works; removing it would break keyboard/command users). The Home button adds an explicit, labeled affordance that disappears when not needed, so the toolbar is never cluttered with a redundant button.

---

## App logo (SUPERSEDED — see "PMR Gauge Badge Logo" below)

**Status:** Superseded 2026-05-31T21:36:43-04:00

The "Checkered Mod" design was rejected by Elliott and replaced with the PMR Gauge Badge concept.

---

## Decision: PMR Gauge Badge Logo

**Date:** 2026-05-31T21:36:43-04:00  
**Author:** Slit (UI Dev)  
**Status:** Implemented

---

### Problem

The original "Checkered Mod" logo (a 2×2 racing-flag grid) was rejected by Elliott. He requested a design inspired by the **AMS2 Content Manager** aesthetic but branded for **PMR (Project Motor Racing)**.

### Research: AMS2/AC Content Manager Design Language

Assetto Corsa Content Manager uses:
- **Circular badge emblem** — clean, professional utility feel
- **Dashboard/gauge motifs** — speedometer, tachometer imagery
- **Bold monogram typography** — "CM" prominently featured
- **Speed indicators** — chevrons, motion lines, dynamic angles
- **Dark metallic palette** — blacks, greys, accent colors

### Decision: PMR Gauge Badge

**Design concept:** A circular motorsport badge featuring a stylized speedometer gauge, evoking a racing dashboard. The needle points toward "high speed" to suggest performance and motion.

#### Visual elements:
1. **Outer ring** — gradient border (#45475a → #313244) for badge/emblem feel
2. **Inner disc** — dark gradient background (#1e1e2e → #11111b)
3. **Gauge arc** — 180° speedometer arc in AccentBrush (#89B4FA)
4. **Tick marks** — five marks around the arc (semi-transparent)
5. **Gauge needle** — pointing top-right ("high speed")
6. **Pivot hub** — center circle with dark inner dot
7. **Racing chevrons** — three right-facing chevrons at bottom (motion indicator)

#### Color palette (Catppuccin Mocha):
- AccentBrush: #89B4FA (gauge, needle, chevrons)
- Surface0Brush: #313244 (outer ring)
- MantleBrush: #181825 / #1e1e2e (background)
- Semi-transparent: #7089B4FA, #9989B4FA

#### Technical:
- ViewBox: 64×64 (scales cleanly to 16–48px)
- SVG source of truth: `src/EWSR_PMR_ModApp.UI/Assets/logo.svg`
- WPF resource: `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` (`LogoDrawingImage` key)
- Toolbar Image: 30×30 (bumped from 28×28)

### Why Not Copy AMS2 CM?

The design takes **inspiration** from Content Manager's visual language (circular badge, dashboard motifs, speed indicators) but is an **original composition** for PMR. No trademark or copyright issues — the speedometer gauge is a generic motorsport symbol.

### Files Changed

| File | Change |
|------|--------|
| `src/EWSR_PMR_ModApp.UI/Assets/logo.svg` | Replaced checkered grid with gauge badge design |
| `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` | Updated DrawingImage to match new SVG |
| `src/EWSR_PMR_ModApp.UI/MainWindow.xaml` | Image size bumped to 30×30 |

### Verification

- `dotnet build` — succeeds
- `dotnet test` — 164 passed, 0 failed
- SVG opens in browser — renders correctly

### Supersedes

Previous decision: "Checkered Mod" logo (decisions.md, 2026-05-31T21:21:54-04:00)

---

## Decision: Adopt user PMR/CM mark + black/white/gold theme

**Date:** 2026-06-01
**Author:** Slit
**Status:** Implemented & verified

### Problem

Elliott supplied a new logo (PMR with a gold "V" wedge over "CM", on black) and asked to use it as the app/desktop/taskbar icon and the in-app toolbar mark, then to retheme the whole app to match: black background, white borders, gold letters/accents.

### Decision — Logo / icon

- Source is a raster PNG (not SVG). Generated assets with Pillow:
  - `Assets/app.ico` — square, centered crop, 6 PNG-payload entries (16/32/48/64/128/256).
  - `Assets/logo.png` — tight crop for the toolbar, shown in a rounded black badge.
- Removed the old vector wordmark: `Assets/Logo.xaml` + `Assets/logo.svg`, and the `App.xaml` MergedDictionary that referenced `Logo.xaml` (resource key `LogoDrawingImage`).

### Decision — Theme (replaces Catppuccin Mocha)

- Backgrounds → black: `BaseBrush #0A0A0A`, `MantleBrush #050505`, surfaces `#161616`/`#262626`.
- Borders → white: new `BorderBrush #E8E4D8`, applied to buttons, inputs, panels, toolbar, status bar.
- Accent → brass gold `#C2A35A` (sampled from the logo, avg `#B49862`). Used for headings, primary buttons, progress, selection, drag-over highlight.
- Text → cream `TextBrush #EFEADD`; secondary `SubtextBrush #A89F8C`.
- OS title bar painted black via DWM (Win11): immersive dark mode + black caption + white border + gold caption text. Fails harmlessly on older Windows.

### Key learnings

- **Pillow's default `.ico` is not WPF-decodable** — `Window.Icon` threw `XamlParseException → FileFormatException ("image is unrecognized")` at startup. Fix: assemble the ICO manually as PNG-payload entries (ICONDIR + ICONDIRENTRY + concatenated PNGs) — the format WPF accepts (PNG-compressed ICO, Vista+).
- The supplied logo is designed for black with dark-grey letters; keying out the black for transparency would erase the letters. Keep the black and present it as a rounded badge on the near-black toolbar.

### Verification

- `dotnet build` — 0 warnings, 0 errors
- `dotnet test` — 164 passed, 0 failed, 0 skipped
- Launched exe; no startup XAML errors; screenshot confirmed black bg, white borders, gold title/headers/button, cream text, black title bar, PMR/CM badge.

### Supersedes

Previous: "PMR Gauge Badge" / "Checkered Mod" logo, and the Catppuccin Mocha palette.

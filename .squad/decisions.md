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

## App logo

### Problem
The app had no visual identity — just a 🎮 emoji in the toolbar title.

### Decision

**Logo concept — "Checkered Mod":**
- 2×2 racing-flag grid on a dark rounded-rect background (32×32 viewbox).
- AccentBrush (#89B4FA, Catppuccin Mocha blue): top-left and bottom-right squares.
- Surface0Brush (#313244): top-right and bottom-left squares.
- Background: MantleBrush (#181825).
- Flat, no gradients. Reads at 16–40 px.

**Implementation:**
- `Assets/logo.svg` — editable SVG source (build action `None`, design-time only).
- `Assets/Logo.xaml` — WPF `ResourceDictionary` with `DrawingImage x:Key="LogoDrawingImage"` (compiled as `Page`, merged into App.xaml).
- Toolbar: `<Image Source="{StaticResource LogoDrawingImage}" Width="28" Height="28"/>` replaces the 🎮 emoji.

**Window.Icon — NOT set:**
`Window.Icon` requires a `BitmapFrame`/HICON for correct taskbar rendering. `DrawingImage` inherits from `ImageSource` but WPF's native window chrome code path for the taskbar icon rasterises via HICON, making DrawingImage unreliable there. The toolbar `Image` uses DrawingImage correctly (WPF renders it via the visual tree). A `<!-- TODO: ship a .ico for taskbar icon (packaging) -->` comment documents this. Action item: generate a multi-size logo.ico from logo.svg and set `Window.Icon` — route to Furiosa/Nux for packaging.

### Files Changed

| File | Change |
|------|--------|
| `src/EWSR_PMR_ModApp.UI/Assets/Logo.xaml` | New — DrawingImage resource |
| `src/EWSR_PMR_ModApp.UI/Assets/logo.svg` | New — SVG source of truth |
| `src/EWSR_PMR_ModApp.UI/App.xaml` | Wrap resources in ResourceDictionary; add MergedDictionaries → Logo.xaml |
| `src/EWSR_PMR_ModApp.UI/MainWindow.xaml` | Replace 🎮 with Image; add 🏠 Home button; add Window.Icon TODO |
| `src/EWSR_PMR_ModApp.UI/ViewModels/MainViewModel.cs` | Add HomeCommand property + instantiation |

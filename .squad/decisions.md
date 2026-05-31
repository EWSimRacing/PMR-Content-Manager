# Decisions

Canonical decision ledger for EWSR_PMR_ModApp. Append-only. Scribe merges entries from `decisions/inbox/`.

---

### 2026-05-31T15:41:33-04:00: Stack Choice — .NET 10 + WPF

**By:** Furiosa

**What:** The project will use C# / .NET 10 with WPF for the UI and a separate Core class library for all engine logic.

**Why:**
- Windows-only target → WPF gives native drag-and-drop, file dialogs, and system tray support out of the box.
- .NET's `System.IO.Compression` and async file I/O are excellent for the zip/sync workload.
- Single-file publish produces a clean installer story without bundling a browser (vs Electron).
- C# is strongly typed and well-tooled for a solo/small dev — refactoring is safe, NuGet ecosystem is rich.
- Inspired by AMS2 Content Manager which is also .NET/WPF — proven pattern for this domain.

---

### 2026-05-31T15:59:44-04:00: User directive

**By:** Elliott Williams (via Copilot)

**What:** All game files that mods will change live under `C:\Program Files\Project Motor Racing\data`. The install target root is the game's `data` folder. Open design question raised: how to determine which files in an added mod `.zip` map to which files under `data`.

**Why:** User request — captured for team memory. Constrains GameDetection (locate the `data` root) and the zip→game-path mapping logic in the SyncEngine.

---

### 2026-05-31T15:59:44-04:00: File Mapping Strategy — Hybrid Path-Overlay + Filename-Index Fallback

**By:** Furiosa

**What:** Mod zip files are mapped to the game's `data` root using a hybrid strategy: (1) path-overlay when the zip preserves folder structure (strip `data/` prefix if present, else overlay directly if top-level dirs match known children of `data`); (2) filename-index fallback for flat zips (match by name against an index of all files under `data`, surface ambiguities to user). An optional `modinfo.json` inside the zip is treated as authoritative when present. The install manifest records per-file: relativeTargetPath, sourcePathInZip, mappingMethod, originalFileHash, installedFileHash, isNewFile — making installs deterministic and reversible. GameDetection must validate write access to the `data` root under Program Files and prompt for elevation if needed.

**Why:**
- Path-overlay is the 90% case for racing sim mods — authors typically mirror the game's folder tree. It's fast, deterministic, and requires no indexing.
- Filename-index covers the remaining 10% (flat zips from lazy packagers) without requiring user intervention in the happy path.
- Surfacing ambiguous matches to the user avoids silent mis-installation — correctness over convenience.
- The optional `modinfo.json` gives advanced mod authors a zero-ambiguity path without imposing burden on casual modders.
- Recording `mappingMethod` in the manifest means we can audit and debug any install after the fact.
- Program Files write-access check prevents the #1 silent failure mode on modern Windows.

---

### 2026-05-31T16:05:20-04:00: Core Service Interfaces and Storage Locations

**By:** Nux

**What:**
The following public interfaces are defined in `src/EWSR_PMR_ModApp.Core/` and constitute the stable contract between Core and the UI:

| Interface | Location | Role |
|---|---|---|
| `IFileSystem` / `RealFileSystem` | `Abstractions/` | Disk I/O abstraction for testability |
| `IGameLocator` / `GameLocator` | `GameDetection/` | Resolves PMR data root; validates path; checks write access |
| `IManifestStore` / `ManifestStore` | `Manifest/` | JSON manifest CRUD + conflict detection |
| `IZipService` / `ZipService` | `ZipHandling/` | Zip validation, staging, modinfo.json parse |
| `IMappingResolver` / `MappingResolver` | `SyncEngine/Mapping/` | Pure file-mapping logic (no disk writes) |
| `ISyncEngine` / `SyncEngine` | `SyncEngine/` | Install / Uninstall / CheckReverted / Reapply orchestration |
| `IBackupService` / `BackupService` | `Backup/` | Pre-install backup, restore-on-uninstall, restore-all, prune |

**Storage locations** (all under `%APPDATA%\EWSR_PMR_ModApp\`):
- `manifest.json` — master manifest; schema version 1; migrateable via `SchemaVersion` field
- `backups\{modId}\` — original game files, namespaced per mod, for clean uninstall
- `mods\{modId}\` — cached mod payload for post-game-update reapply without the original zip
- `staging\{sessionGuid}\` — transient extraction area; always cleaned up after install (success or failure)

**Key design choices baked into the interfaces:**
- `ISyncEngine.InstallAsync` takes a `Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>>` callback so the UI owns the ambiguity-resolution dialog without Core having any UI dependency.
- `ISyncEngine.InstallAsync` takes `IProgress<SyncProgress>` (phase string + 0–100 percent) for live UI feedback.
- `IGameLocator.LocateAsync` returns `GameLocatorResult(Found, DataRoot, Source, FailureReason?)` — if `Found=false`, caller must prompt for manual path selection.
- `IGameLocator.CanWriteDataRoot` lets the UI gate the install button behind an elevation check.
- `TimeProvider` is injected into `SyncEngine` (not `DateTime.UtcNow`) so install timestamps are deterministic in tests.
- `ManifestStore` accepts a path override constructor for testing without touching `%APPDATA%`.

**Why:**
- Zero WPF dependencies in Core makes the library fully unit-testable and reusable.
- DI-friendly interfaces (constructor injection throughout) let the host app swap implementations (e.g. mock filesystem in tests, real filesystem in production).
- Centralising all storage paths in `AppPaths.cs` means renaming a folder is a one-line change.
- Separating the pure `MappingResolver` from the orchestrating `SyncEngine` means the 90% of mapping logic can be tested without setting up a full install pipeline.

---

### 2026-05-31T16:40:00-04:00: Collision detection in MappingResolver + IFileHasher injection in SyncEngine

**By:** Nux

**What:**
1. Added `CollisionMapping` type and a `Collisions` bucket to `MappingPlan`. `MappingResolver` now runs an `ApplyCollisionDetection` post-pass after every mapping strategy: any two or more zip entries that resolve to the same `RelativeTargetPath` (case-insensitive) are moved out of `Mapped` into `Collisions` — neither is auto-installed. `SyncEngine.InstallAsync` surfaces each collision as a warning string in `InstallResult.Warnings`.
2. Introduced `IFileHasher` interface (`string ComputeHash(string filePath)`) with a default `RealFileHasher` implementation wrapping `HashHelper.ComputeFileHash`. `SyncEngine` now accepts `IFileHasher?` as an optional constructor parameter (defaults to `RealFileHasher`). All four internal hash calls use `_hasher.ComputeHash`.

**Why:**
- **Collision fix:** Two mod files silently overwriting the same game file is a data-loss bug. Neither entry should win without explicit user input; surfacing them in `Collisions` lets the UI prompt the user for resolution. Wez's skipped regression test (`TwoZipEntries_BothResolvingToSameTarget_CollisionReported_NeitherPlaced`) now passes and was un-skipped.
- **IFileHasher fix:** `HashHelper.ComputeFileHash` calls real disk, blocking unit tests of the revert-detection and re-apply paths. Making the hasher injectable keeps Core testable without temp files, as requested by Wez in `ReApplyTests.cs`.

---

### 2026-05-31T16:55:00-04:00: Test Approach — Use Real Core Types + FakeFileSystem

**By:** Wez

**What:**
Tests use real Core classes (`MappingResolver`, `GameLocator`, `ManifestStore`, `SyncEngine`) coupled with test doubles:
- `FakeFileSystem` (in-memory `IFileSystem` implementation) for I/O
- `FakeFileHasher` (injectable `IFileHasher` test double) for hash computation
- `NoOpZipService` and `NoOpBackupService` for non-install test slices

**Why:**
Testing real Core code directly gives the highest-confidence regression net. The FakeFileSystem avoids I/O without sacrificing fidelity. This approach is compatible with Nux's `IFileHasher` injection, allowing `SyncEngine.CheckForRevertedModsAsync` to be unit-tested without staging stubs or real disk.

---

### 2026-05-31T16:55:00-04:00: Test Project Consolidation

**By:** Wez

**What:**
Single canonical test project: `tests/EWSR_PMR_ModApp.Core.Tests` (the conventional location, already in `EWSR_PMR_ModApp.slnx`). Wez's 55 comprehensive tests were consolidated into this location alongside Nux's smoke tests. The old `src/EWSR_PMR_ModApp.Tests/` directory (containing staging stubs and contracts) was deleted.

**Result:**
- One test project in the solution
- `dotnet test` from repo root: 56 tests, 0 failed, 0 skipped
- No `[Fact(Skip=...)]` attributes in the suite
- All tests run against real Core classes with in-memory fakes for I/O

**Why:**
Consolidation into the solution eliminates the accidental omission where `dotnet test` was only running Nux's 6 smoke tests and ignoring Wez's full suite. Single source of truth simplifies CI/CD and team onboarding.

---

### 2026-05-31T18:09:19-04:00: WPF UI Architecture — Full Shell Implementation

**By:** Slit

**What:**
The WPF UI shell for `src/EWSR_PMR_ModApp.UI` is now fully implemented. Key decisions:

1. **MVVM pattern** — ViewModels (`MainViewModel`, `SettingsViewModel`, `ModItemViewModel`) hold all logic; code-behind (`MainWindow.xaml.cs`) is limited to drag-and-drop event bridging.

2. **DI container** — `Microsoft.Extensions.DependencyInjection` (v9.0.5) registered in `App.xaml.cs`. All Core concrete types registered as singletons against their interfaces. `TimeProvider.System` registered for `SyncEngine`. A factory lambda breaks the `SettingsViewModel → MainViewModel` circular dependency.

3. **Elevation approach** — `app.manifest` uses `asInvoker` (app starts without admin). If `IGameLocator.CanWriteDataRoot` returns false, a persistent orange banner is shown with a "Restart as Administrator" button that relaunches the process with `runas` verb. UAC cancellation (Win32 1223) is handled silently.

4. **Ambiguous mapping dialog** — `ISyncEngine.InstallAsync`'s `confirmAmbiguous` callback is implemented as `MainViewModel.ResolveAmbiguousMappingsAsync`, which marshals via `Dispatcher.InvokeAsync` to show `AmbiguousMappingDialog` on the UI thread.

5. **Collision/warning surfacing** — `InstallResult.Warnings` are shown in `WarningsDialog` after each install if non-empty, prominently noting collisions were NOT installed.

6. **Settings persistence** — user-configured game path is written to `%APPDATA%\EWSR_PMR_ModApp\ui-settings.json` by `UISettingsStore` (UI-layer only; does not touch Core's `AppPaths`).

7. **Color theme** — Catppuccin Mocha palette defined in `App.xaml` Application.Resources, applied via `StaticResource` brushes throughout.

**Why:**
- MVVM keeps logic testable and code-behind thin per the quality bar.
- `asInvoker` + relaunch gives the best user experience: users can browse mods without admin, and are only prompted to elevate when they actually attempt a write operation.
- DI-first approach means Core services can be swapped for test doubles without changing ViewModel code.
- All Core interfaces matched their constructors exactly — no Core API gaps encountered.

---

### 2026-05-31: Fix ProgressBar TwoWay Binding Crash

**By:** Slit

**What:** Added `Mode=OneWay` to `ProgressBar.Value` binding in `MainWindow.xaml` (line 357). Changed `Value="{Binding ProgressValue}"` → `Value="{Binding ProgressValue, Mode=OneWay}"`.

**Why:** `RangeBase.Value` (the base of `ProgressBar`) binds TwoWay by default. `MainViewModel.ProgressValue` has a `private set`, which WPF cannot write to from outside the class. At startup WPF attempted the TwoWay write-back and threw `XamlParseException → InvalidOperationException`, crashing the app before any window appeared. The progress value is display-only (VM → UI only), so `Mode=OneWay` is the correct, clean fix. No other XAML bindings were affected.

---

### 2026-05-31T18:41:06-04:00: Fix WPF Drag-and-Drop — UIPI Message Filter + AllowDrop on Window

**By:** Slit (commit ba4c336)

**What:**
Two changes to `src/EWSR_PMR_ModApp.UI/`:

1. **`MainWindow.xaml.cs`** — Added P/Invoke for `ChangeWindowMessageFilterEx` (user32.dll) and overrode `OnSourceInitialized` to call it with `MSGFLT_ALLOW` for `WM_DROPFILES (0x0233)`, `WM_COPYDATA (0x004A)`, and `WM_COPYGLOBALDATA (0x0049)` on the window's HWND via `WindowInteropHelper`. Added `using System.Runtime.InteropServices` and `using System.Windows.Interop`.

2. **`MainWindow.xaml`** — Added `AllowDrop="True"` to the `Window` root element.

**Why:**
- **UIPI** (primary fix): When running elevated, Windows UIPI silently blocks drag-drop messages from non-elevated processes (e.g., Explorer). `ChangeWindowMessageFilterEx` with `MSGFLT_ALLOW` opens the three drag-drop message channels for the app's HWND, restoring Explorer→app drag-drop when elevated.
- The Window-level handlers (`DragOver`/`DragEnter`/`Drop`) were already correctly written with `Effects=Copy`, `Handled=true`, zip filter, and delegation to `InstallZipsAsync`.

---

### 2026-05-31T19:19:13-04:00: Wire Drag-Drop Handlers to DropZoneBorder Directly

**By:** Slit

**What:**
Added `DragEnter`, `DragOver`, `DragLeave`, and `Drop` attributes directly to `DropZoneBorder` in `MainWindow.xaml`, wiring them to the same handler methods used by the Window. These handlers were previously only on the Window element, while `DropZoneBorder` had `AllowDrop="True"` but no handlers attached.

**Why:**
WPF's OLE drag-drop targets the **innermost `AllowDrop="True"` element** found via hit-test. When dragging over `DropZoneBorder`, WPF routed `DragOver` to the border first. Without handlers attached to `DropZoneBorder`, nothing set `e.Effects = DragDropEffects.Copy`, so WPF reported no valid drop effect to the OLE layer. The result was a ⊘ cursor and silent rejection of the `Drop` event, regardless of the Window handlers that would have received the bubbled event.

This surfaced (or was made worse) by commit ba4c336, which added `AllowDrop="True"` to `DropZoneBorder` without attaching the handlers, making it the OLE target with no confirmation logic.

**Rule:** Drag event handlers must be wired to the same element that has `AllowDrop="True"` (the innermost AllowDrop hit-target), not only to distant ancestors. Relying on bubbling to a Window ancestor is unreliable for OLE drag-drop effect confirmation.

The UIPI `ChangeWindowMessageFilterEx` fix remains necessary for the elevated scenario.

---

# Decision: Warning messages must name every skipped/colliding file

**Date:** 2026-05-31T18:55:17-04:00
**By:** Nux
**Requested by:** Elliott Williams

## What

`SyncEngine.InstallAsync` now emits one `InstallResult.Warnings` entry **per file** for both
unmatched and collision cases, instead of a single aggregated count string.

**Before:**
```
1 file(s) had no match in the data directory and were skipped.
Path collision: 2 zip files resolve to 'vehicles/car_x/livery.dds' — none installed. User resolution required.
```

**After:**
```
Skipped (no match in data): mod_pack/brand_new_skin.dds
Path collision: 'vehicles/car_x/livery.dds' — 2 source(s): skin_a/livery.dds, skin_b/livery.dds — none installed.
```

## Why

The old format gave a bare count with no filenames.  The user could not tell *which* file was
skipped or *which* zip entries collided, making the warning non-actionable.  Elliott's explicit
request: "can we at least show which files it is warning us about?"

The `WarningsDialog` already renders each `Warnings` string as its own scrollable row (one `⚠`
icon per item), so one-row-per-file is the natural fit — no UI changes required.

## Scope

- `src/EWSR_PMR_ModApp.Core/SyncEngine/SyncEngine.cs` only.
- No install behaviour changed: unmatched still skipped, collisions still not installed.
- 4 new tests added (`InstallWarningsTests.cs`).
- New `StubZipService` test double added.

---

# Decision: Zip Skip Policy — Complete Taxonomy and Handling

**Date:** 2026-05-31T19:17:41-04:00
**By:** Furiosa
**Requested by:** Elliott Williams

## Context

Elliott raised a design concern: when a mod zip is dropped into the manager, some files will be skipped, but users (including Elliott as a mod author) need to understand:
1. Which files are skipped and why
2. How to package zips so nothing important gets skipped accidentally
3. How skipped files are surfaced in the UI

## Decision

### 1. Skip Category Taxonomy

All files in a mod zip are classified into one of these categories:

| Category | Code | Behavior |
|----------|------|----------|
| `Install` | Normal install | Copied to game `data/` directory |
| `DisplayOnly` | Shown in UI | Extracted for viewing, not installed |
| `NoPathMatch` | Skipped | No mapping to game path found |
| `MetaFile` | Skipped | Mod manager metadata (e.g., `modinfo.json`) |
| `HashMatch` | Skipped | Already installed with identical content |
| `UserExcluded` | Skipped | User explicitly blocked this file |
| `AmbiguousPending` | Held | Requires user confirmation |
| `Collision` | Blocked | Multiple sources → same target |
| `UnsafeFile` | Blocked | Executables — never auto-installed |

### 2. Skip Policy by Extension

**Always Install (if path maps):** `.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`

**DisplayOnly:** `.md`, `.txt`, `.pdf` (documentation)

**Conditional Images:** `.png`, `.jpg` — install if in valid game texture path, display-only if at root/preview folder

**Always Skip:** `.exe`, `.dll`, `.bat`, `.ps1`, `.log`, `.bak`, `.tmp`, nested archives

**Meta:** `modinfo.json` at zip root

### 3. modinfo.json v1 Spec

Full schema defined in ARCHITECTURE.md with:
- Required: `schemaVersion`, `name`, `version`
- Optional: `author`, `description`, `website`, `minGameVersion`, `tags`
- File control: `files`, `displayFiles`, `skipFiles`, `dependencies`

When `modinfo.json` is present and valid, its `files` mapping is authoritative — no heuristics run for listed files.

### 4. UI Surfacing

- **During install:** "Installed X, skipped Y" with expandable detail dropdown
- **Mod detail view:** Full file list with status column (installed / display-only / skipped)
- **Log panel:** One line per skipped file with category and reason
- **InstallResult API:** Returns `IReadOnlyList<SkippedFile>` with path, category, reason

### 5. Documentation

Created `docs/MOD_PACKAGING_GUIDE.md` — practical packaging guide for mod authors (Elliott's reference when building EWSR zips).

## Why

- **Transparency:** Users need to know what happened to every file in their zip
- **Actionability:** Warnings must name specific files (per existing decision)
- **Author guidance:** Elliott builds mods via EWSR_PMR_Tools — he needs clear rules
- **Safety:** Never auto-install executables; always block unsafe files
- **Flexibility:** Display-only preserves README/preview access without polluting game directory

## Impact

- `docs/ARCHITECTURE.md` — updated with Skip Logic section and full modinfo.json spec
- `docs/MOD_PACKAGING_GUIDE.md` — new file for mod authors
- Nux: implement `SkipCategory` enum, `SkippedFile` record, extension filtering in `ZipService`
- Slit: update install result display to show skip breakdown

## Review Needed

Elliott should review:
1. Skip category taxonomy — any missing cases?
2. Extension policy — any PMR file types not covered?
3. modinfo.json spec — any fields needed for EWSR workflow?
4. Packaging guide — clear enough for EWSR_PMR_Tools integration?


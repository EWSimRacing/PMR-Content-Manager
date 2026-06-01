# History: Slit — UI Dev

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- My domain: drop-zone UI, installed mods list, install status, settings screen.
- User: Elliott Williams.

## Learnings

### 2026-05-31: ProgressBar TwoWay Binding Crash — Bug Fix

- **Bug:** `ProgressBar.Value="{Binding ProgressValue}"` caused `XamlParseException` at startup. `RangeBase.Value` (and `ProgressBar.Value`) binds **TwoWay by default**. WPF tried to write back through the binding and hit `ProgressValue`'s `private set`, which it cannot access — hence the `InvalidOperationException`.
- **Fix:** Added `Mode=OneWay` → `Value="{Binding ProgressValue, Mode=OneWay}"`. The progress bar is display-only; the value only ever flows VM → UI.
- **Audit note:** Scanned all XAML (MainWindow.xaml, AmbiguousMappingDialog.xaml, WarningsDialog.xaml). Only `ProgressBar.Value` was affected. `TextBox.Text` for `ConfiguredPath` and `ComboBox.SelectedItem` for `SelectedOption` are intentionally TwoWay and both target public setters — those are fine.
- **PropertyChanged:** `ProgressValue` already raises `PropertyChanged` via `SetField` in `ViewModelBase` — the bar still animates during installs.
- **Lesson — compile ≠ runs for WPF:** XAML binding errors are runtime-only; the app will build cleanly and still crash on startup. Always **launch-test** after UI changes, not just build-test.
- **Watch out for TwoWay-default bindings against `private set` / get-only VM props:** `ProgressBar.Value`, `Slider.Value`, `ComboBox.SelectedItem`, `ComboBox.SelectedValue`, `CheckBox.IsChecked`, `ToggleButton.IsChecked`, `TextBox.Text` all bind TwoWay by default. If the VM property has `private set` or no setter, add `Mode=OneWay`.

### 2026-05-31: Stack Decision & Handoff (via Furiosa)
- **Stack:** C# / .NET 10 + WPF. Solution: `EWSR_PMR_ModApp.slnx` at repo root.
- **Your module:** `src/EWSR_PMR_ModApp.UI/` (WPF shell, depends on Core).
- **Next:** Build drag-and-drop zone, mod list view, settings panel, status/log display. Integrate with Core APIs as they stabilize.
- **See:** `docs/ARCHITECTURE.md` for full module spec and Core API contracts.
- **Build:** `dotnet build` from repo root; run with `dotnet run --project src/EWSR_PMR_ModApp.UI`.

### 2026-05-31: File Mapping Strategy — UI Handoff (via Scribe)
- **Install target:** All mod-affected files under `C:\Program Files\Project Motor Racing\data` (user constraint).
- **UI responsibilities:**
  1. **Ambiguity resolution:** When filename-index fallback produces multiple matches, surface them to user with options to pick correct file(s).
  2. **Elevation prompt:** If SyncEngine detects missing write access to `data` root, UI must prompt for elevation (Windows UAC dialog).
  3. **Manifest display:** Show per-file mapping status: relativeTargetPath, mappingMethod (overlay/filename-index/modinfo), installed file hash (for verification).
- **Nux side:** SyncEngine will call Core APIs to surface ambiguities; UI catches and displays them.
- **See:** `docs/ARCHITECTURE.md` "File Mapping Strategy" section for full details.

### 2026-05-31: Full WPF UI Implemented

#### UI Structure
All files live under `src/EWSR_PMR_ModApp.UI/`:

| File/Folder | Purpose |
|---|---|
| `App.xaml` | Application-level styles (Catppuccin Mocha palette), converters, app startup event |
| `App.xaml.cs` | DI container setup; resolves MainViewModel + SettingsViewModel; fires `InitializeAsync()` |
| `MainWindow.xaml` | Shell: toolbar, mod list (left), drop zone/settings (right), elevation banner, status bar |
| `MainWindow.xaml.cs` | Thin code-behind: drag-and-drop events → `MainViewModel.InstallZipsAsync()` |
| `app.manifest` | UAC manifest: `asInvoker` — app starts without admin, relaunch-as-admin handled via button |
| `Infrastructure/ViewModelBase.cs` | `INotifyPropertyChanged` base + `SetField<T>` helper |
| `Infrastructure/RelayCommand.cs` | Synchronous `ICommand` impl |
| `Infrastructure/AsyncRelayCommand.cs` | Async `ICommand` impl; disables while executing |
| `Infrastructure/BoolToVisibilityConverter.cs` | `bool→Visibility` (true=Visible) and inverse |
| `Infrastructure/ZeroToVisibilityConverter.cs` | `int==0 → Visible` for empty-state panels |
| `Infrastructure/NonNullToVisibilityConverter.cs` | `string?` non-null → Visible (for DataRoot label) |
| `Infrastructure/UISettingsStore.cs` | Persists user-configured game path to `%APPDATA%\EWSR_PMR_ModApp\ui-settings.json` |
| `ViewModels/ModItemViewModel.cs` | One installed mod: name, date, file count, `UninstallCommand` |
| `ViewModels/SettingsViewModel.cs` | Game path config: browse folder dialog, `IGameLocator.LocateAsync`, persist via UISettingsStore |
| `ViewModels/MainViewModel.cs` | Primary orchestrator: install flow, mod list refresh, reapply, elevation |
| `Dialogs/AmbiguousMappingItemViewModel.cs` | Per-item VM for the ambiguity dialog: zip path, reason, candidate ComboBox options |
| `Dialogs/AmbiguousMappingDialog.xaml/.cs` | Modal: shows each `AmbiguousMapping`, lets user pick target or skip; returns `IReadOnlyList<ResolvedMapping>` |
| `Dialogs/WarningsDialog.xaml/.cs` | Modal: lists install warnings/collisions; prominently notes none were auto-installed |

#### How DI is wired to Core
- `App.xaml.cs` creates a `ServiceCollection`, registers all Core concrete types against their interfaces, registers `TimeProvider.System`, and registers UI types.
- `SyncEngine` constructor takes `IZipService, IMappingResolver, IManifestStore, IBackupService, IFileSystem, TimeProvider` — all resolved by the DI container.
- `SettingsViewModel` has a circular dependency on `MainViewModel`, resolved via a factory lambda that defers resolution.
- `MainViewModel.SettingsViewModel` is assigned after both VMs are resolved, breaking the cycle without DI complexity.

#### Elevation handling
- `app.manifest` uses `asInvoker` — app always starts as the current user.
- On startup, `IGameLocator.CanWriteDataRoot(dataRoot)` is called; if false, `MainViewModel.NeedsElevation = true`.
- This triggers the elevation banner in `MainWindow.xaml` with a "Restart as Administrator" button.
- `RestartElevatedCommand` relaunches the process via `Process.Start` with `Verb = "runas"` then calls `Application.Current.Shutdown()`.
- UAC cancellation (Win32 error 1223) is caught silently — the app stays open.

#### Ambiguous mapping callback
- `MainViewModel.ResolveAmbiguousMappingsAsync` is passed as the `confirmAmbiguous` callback to `ISyncEngine.InstallAsync`.
- It marshals to the UI thread via `Application.Current.Dispatcher.InvokeAsync` to show `AmbiguousMappingDialog`.
- The dialog returns `IReadOnlyList<ResolvedMapping>` after the user picks targets or skips each file.

#### What Wez needs to test the UI
- **Build:** `dotnet build` — must succeed (confirmed clean).
- **Tests:** `dotnet test` — 56/56 tests pass; no Core files modified.
- **Manual launch:** `dotnet run --project src/EWSR_PMR_ModApp.UI` from repo root (Windows only; requires .NET 10 + WPF).
- **Game detection:** if PMR is installed at `C:\Program Files\Project Motor Racing\data`, it auto-detects on startup; otherwise the Settings panel opens automatically.
- **Elevation:** if the game is under Program Files and the app isn't elevated, the orange banner appears with the "Restart as Administrator" button.
- **No Core API gaps found** — all Core interfaces and concrete types had sufficient constructors for DI registration.


### 2026-05-31: Drag-and-Drop Fix — UIPI + AllowDrop

#### Root Causes Found
1. **UIPI (primary — almost certainly the live blocker):** When the app is relaunched as Administrator, it runs at a higher integrity level than Explorer. Windows UIPI silently blocks `WM_DROPFILES` / `WM_COPYDATA` / `WM_COPYGLOBALDATA` from reaching the elevated window — no cursor change, nothing on drop. The standard fix is to call `ChangeWindowMessageFilterEx` (user32.dll) with `MSGFLT_ALLOW` on each of those three message IDs immediately after the HWND is created.
2. **`AllowDrop` on the drop target element (belt-and-suspenders):** `AllowDrop="True"` was only on the `Window`. The element actually under the cursor needs it, not just a parent. Added `AllowDrop="True"` directly to `DropZoneBorder` (which already has a non-null `Background` via its Style, so it IS hit-testable).

#### What was already correct
- `DragOver` set `e.Effects = DragDropEffects.Copy` + `e.Handled = true`
- `DragEnter` set `e.Effects = DragDropEffects.Copy` + `e.Handled = true`
- `Drop` read `DataFormats.FileDrop`, filtered `.zip` (case-insensitive), called `InstallZipsAsync`
- Browse button calls the same `InstallZipsAsync` — single shared code path

#### Changes made
- `MainWindow.xaml.cs`: Added P/Invoke for `ChangeWindowMessageFilterEx`. Overrode `OnSourceInitialized` to call it for `WM_DROPFILES (0x0233)`, `WM_COPYDATA (0x004A)`, `WM_COPYGLOBALDATA (0x0049)` on the window HWND via `WindowInteropHelper`. Safe to always run — harmless on non-elevated, essential when elevated.
- `MainWindow.xaml`: Added `AllowDrop="True"` to `DropZoneBorder`.

#### Lessons
- **Elevated WPF + Explorer drag-drop = UIPI block.** Any app that uses "Restart as Administrator" and accepts file drops from Explorer MUST call `ChangeWindowMessageFilterEx` for the three drag messages. This is a silent failure — no error, no cursor, just nothing.
- **`ChangeWindowMessageFilterEx` requires the HWND** — call it in `OnSourceInitialized` (HWND exists), NOT in the constructor (too early, HWND is null).
- **`AllowDrop` on the exact drop-target element, not just a parent.** Set it on the visual border/panel the user drops onto.
- **`Background` must be non-null on the drop element** — a null/transparent background element is not hit-testable.

### 2026-05-31T19:19:13-04:00: Drag-Drop Still Broken — Real Root Cause Found (handlers not wired to drop target)

**Root cause:** `DropZoneBorder` had `AllowDrop="True"` (added in ba4c336) but had **no drag event handlers attached to it in XAML**. The handlers (`Window_DragEnter`, `Window_DragOver`, `Window_DragLeave`, `Window_Drop`) were only wired to the `Window` element.

**Why this breaks:** WPF's OLE drag-drop system targets the **innermost AllowDrop element** found via hit-test — in this case `DropZoneBorder`. During `DragOver`, no handler fires on DropZoneBorder to set `e.Effects = DragDropEffects.Copy`. WPF never reports a valid effect back to the OLE layer, so the ⊘ cursor is shown and `Drop` never fires — even though the Window-level handlers would theoretically receive the bubbled events.

Adding `AllowDrop` to DropZoneBorder (ba4c336) without also adding the handlers actually made things WORSE: it made DropZoneBorder the "responsible" OLE target but left it with no handler to confirm the drop.

**Fix:** Added `DragEnter="Window_DragEnter"`, `DragOver="Window_DragOver"`, `DragLeave="Window_DragLeave"`, `Drop="Window_Drop"` directly to `DropZoneBorder` in XAML. Same handler methods, same logic — no code-behind changes. Window-level handlers kept as fallback for drops on margin/background areas.

**Key lesson:** Handlers must be wired to the SAME element that has `AllowDrop="True"` (or at least to the innermost AllowDrop element in the visual path). Wiring them to a distant ancestor like Window and relying on bubbling is unreliable because WPF's OLE layer interprets effects from the innermost AllowDrop target's handling, not from bubbled ancestors.

**UIPI fix (ba4c336) is still correct:** The `ChangeWindowMessageFilterEx` call in `OnSourceInitialized` is still needed for the elevated/Restart-as-Administrator scenario. Do not remove it.

### 2026-05-31T19:57:03-04:00: Drag-Drop Round 3 — OLE vs WM_DROPFILES Insight + Diagnostics

#### The real root cause (confirmed by reasoning, to be confirmed by log data)

`ChangeWindowMessageFilterEx` only unblocks the **legacy Win32 WM_DROPFILES path**. WPF drag-and-drop is entirely **OLE-based**: it uses `RegisterDragDrop` / `IDropTarget` / `IDataObject` via COM cross-process marshaling. When the app is elevated (High integrity) and the drag source (Explorer) is Medium integrity, UIPI blocks the COM channel separately — `ChangeWindowMessageFilterEx` does not help. This is why two prior fix rounds both failed despite the code being technically correct.

#### Diagnostics added (Part 1)

Added file-based logging to `MainWindow.xaml.cs` writing to `%TEMP%\ewsr_dragdrop.log`:
- `OnSourceInitialized`: logs `IsElevated`, HWND, and the bool return + `GetLastWin32Error` for each `ChangeWindowMessageFilterEx` call
- `DragEnter`: logs sender type, `AllowedEffects`, `GetDataPresent(FileDrop)`, all formats from `GetFormats()`, `IsZipDrop` result
- `DragOver`: throttled (first 3, then every 10th) — logs same data
- `DragLeave`: logs sender type
- `Drop`: logs all data + zip count + paths
- All wrapped in `try/catch` — logging never throws into UI

**What the log tells us:**
- If `DragEnter`/`DragOver` never appear while elevated → OLE channel blocked by UIPI. Message filter is irrelevant.
- If handlers fire but `FileDrop=False` → data serialization issue (unexpected)
- If handlers fire, `IsZipDrop=True`, no `Drop` entry → something breaks the drop completion
- If `ChangeWindowMessageFilterEx` returns `ok=False` → even the WM_DROPFILES path is blocked (error code explains why)

**IMPORTANT: Diagnostics are active and should be removed/gated before final release** (log writes to %TEMP% on every drag event).

#### Hardening added (Part 2a)

Added `IsHitTestVisible="False"` to the four decorative TextBlocks inside the drop zone StackPanel (icon, "Drop a mod .zip here", "or", "Multiple zips supported"). The Button is untouched (still clickable). This ensures decorative elements cannot intercept drag hit-testing even though WPF TextBlocks with no Background are already transparent to hit-testing in practice.

#### Fix direction (Part 2b)

**Recommended architecture: non-elevated UI + elevated helper process.**
- The UI stays `asInvoker` (Medium integrity) — OLE drag-drop from Explorer works perfectly at this level
- A separate minimal `EWSR_PMR_ModApp.Installer.exe` with a `requireAdministrator` manifest handles the actual file writes to Program Files
- IPC via named pipe or temp JSON manifest file
- This is the correct pattern used by professional Windows installers
- The elevated helper is a short-lived process invoked only when file writes are needed; the UI remains responsive

A TODO comment in `OnSourceInitialized` points to this architecture for the follow-up task.

**Key lesson:** Never try to make an elevated WPF app accept OLE drag-drop from non-elevated Explorer. The UIPI COM channel block cannot be bypassed from user mode. The correct solution is to not elevate the UI.

### 2026-05-31T20:10:18-04:00: Drag-Drop RESOLVED — Confirmed Root Cause (IsHitTestVisible)

#### Confirmed root cause
The bug was **NOT UIPI**. The OLE/UIPI theory (and the `ChangeWindowMessageFilterEx` detour) was a red herring.

The actual cause: the decorative `TextBlock` elements centered inside the drop zone — especially "Drop a mod .zip here" — were **hit-testable** by default (WPF elements are hit-testable even with no Background, as long as they are visible). When the user hovered over the center of the drop zone (the natural drop point), the cursor was over one of these TextBlocks. Because those TextBlocks had no `AllowDrop` and no drag handlers, WPF's OLE layer found no valid drop target and showed the ⊘ cursor. The `Border` underneath had all the right wiring, but the TextBlocks above it captured the hit-test first.

The runtime log (`%TEMP%\ewsr_dragdrop.log`) proved this conclusively: a Drop event fired successfully while the app was running **elevated** (`IsElevated=True`), launching a real install. The message filter calls all returned `ok=True`. OLE drag-drop from Explorer works fine even at High integrity — UIPI was never the actual blocker.

#### Fix
`IsHitTestVisible="False"` on the four decorative `TextBlock`s inside the drop zone `StackPanel`. This makes them transparent to hit-testing so drags pass through to the `Border` behind them.

#### Diagnostics cleanup
All diagnostic logging gated behind `#if DEBUG` in `MainWindow.xaml.cs`:
- `_logPath`, `_dragOverLogCount`, `Log()`, `DumpDragData()` — inside `#if DEBUG` block
- All `Log()` calls in `DragEnter`, `DragOver`, `DragLeave`, `Drop` — gated
- `OnSourceInitialized` logging (elevation check, filter call results) — gated; Release path is just the three clean `ChangeWindowMessageFilterEx` calls
- `System.IO` and `System.Security.Principal` usings — gated under `#if DEBUG`
- Log file `%TEMP%\ewsr_dragdrop.log` deleted

`ChangeWindowMessageFilterEx` calls retained unconditionally — harmless and correct for the elevated path.

#### Lessons
- **Decorative children (TextBlocks/StackPanels) centered in a drop zone must have `IsHitTestVisible="False"`** or they intercept the drag and show ⊘, even when the parent Border has `AllowDrop` and all four handlers. This is the #1 WPF drop zone gotcha that is easy to miss.
- A WPF `TextBlock` with no `Background` is still hit-testable. Transparent ≠ non-hit-testable in WPF.
- The UIPI/`ChangeWindowMessageFilterEx` concern was real in theory but was not the live bug. The earlier analysis of OLE-vs-WM_DROPFILES was technically correct but irrelevant to the actual failure mode.
- Diagnostic logging to `%TEMP%` on every drag event is invaluable for remote debugging. Gate it behind `#if DEBUG` so it never ships.


### 2026-05-31T20:40:00-04:00: Phase 2 — Elevation Broker Wiring (S1–S5)

#### Summary

Wired the UI through the non-elevated-UI + elevated-helper architecture that Nux built in Phase 1 (Core). All install, uninstall, and reapply operations now go through the `Prepare → WritePlanRequest → IElevatedWriter → manifest` orchestration.

#### Flow implemented

**Install (S3):**
1. `ISyncEngine.PrepareInstallAsync(...)` — validates zip, stages, resolves mappings, shows ambiguity dialog. Returns `InstallPlan`.
2. Build `WritePlanRequest { Operation=Install, DataRoot, ModId, FilesToCopy, FilesToBackup }` from the plan.
3. Resolve `IElevatedWriter` via factory `_writerFactory(DataRoot)` (see S2).
4. `writer.ExecuteAsync(request, ct)` — does backup + file copy (in-process or via elevated Helper.exe).
5. If `WriteResult.Success`: call `CachePayload(plan)` (copies staged files → AppData payload dir) then `BuildModEntry(plan)` (computes hashes from staged files + backup dir) then `_manifestStore.AddOrUpdateModAsync`.
6. `CleanupInstallPlan(plan)` in `finally` — removes staging dir regardless of outcome.

**Uninstall (S5):**
1. `PrepareUninstallAsync(modId, dataRoot)` → `UninstallPlan`.
2. Build `WritePlanRequest { Operation=Uninstall, FilesToDelete=plan.NewFilesToDelete }`.
3. `writer.ExecuteAsync` — restores backups from AppData backup dir → DataRoot, deletes new files from DataRoot.
4. If success: `Directory.Delete(AppPaths.BackupDirForMod(modId))` then `_manifestStore.RemoveModAsync`.

**Reapply (S5):**
1. `PrepareReapplyAsync(dataRoot)` → `ReapplyPlan` (already checks for reverted mods internally).
2. If `plan.ModsToReapply.Count == 0`, bail early with "nothing to reapply" message.
3. Flatten all mods' `FilesToCopy` into one `WritePlanRequest { Operation=Reapply }` — one UAC prompt total.
4. `writer.ExecuteAsync` — copies payload cache files → DataRoot.
5. No manifest update needed for reapply (manifest already records correct hashes).

#### Writer selection (S2)

`Func<string, IElevatedWriter>` registered as a singleton in DI. Evaluated at operation time (not startup) so DataRoot changes (Settings panel) are reflected immediately:

```csharp
services.AddSingleton<Func<string, IElevatedWriter>>(sp => {
    var locator = sp.GetRequiredService<IGameLocator>();
    return dataRoot => locator.CanWriteDataRoot(dataRoot)
        ? (IElevatedWriter) new InProcessWriter()
        : new HelperProcessWriter();
});
```

`InProcessWriter` is used when the game directory is writable (dev machine, non-Program-Files install). `HelperProcessWriter` is used when elevation is needed (Program Files) — it spawns `EWSR_PMR_ModApp.Helper.exe` with `runas`, writes a temp JSON request file, awaits exit, reads the result file.

#### Manifest write (S3)

`ExecuteInstallAsync` (SyncEngine in-process path) is no longer called from the UI. Instead, the UI orchestration always calls the writer directly and then updates the manifest itself. This means:

- **Both in-process and helper paths** end with an identical manifest update from the UI.
- No double-write. `ExecuteInstallAsync` is only used by the thin-wrapper `InstallAsync` which is no longer called by the UI.
- `InstalledFileHash` is computed from the staged source file (same bytes as installed). `OriginalFileHash` is computed from the backup file if it exists (null = new file).

#### UAC cancel UX (S4)

`HelperProcessWriter` returns `WriteResult { Success=false, ErrorMessage="Elevation cancelled by user." }` on Win32 error 1223. `ShowWriteFailure()` detects this string and shows a `MessageBoxImage.Warning` (not error) with the message "Install/Uninstall/Reapply cancelled — administrator permission was declined." `IsBusy` is always released in `finally` — no stuck spinner.

#### S1 — Elevation flow removed

- `NeedsElevation` property, `RestartElevatedCommand`, `RestartElevated()`, and related status text removed from `MainViewModel`.
- Elevation banner (Grid.Row="2", ~30 lines of XAML) removed from `MainWindow.xaml`. Grid row count reduced from 4 to 3; status bar moved from Grid.Row="3" to Grid.Row="2".
- `app.manifest` remains `asInvoker` — unchanged. The UI never elevates itself.
- `ChangeWindowMessageFilterEx` calls in `MainWindow.xaml.cs` retained (harmless, correct for completeness).

#### Build / test result

- `dotnet build` → 0 errors, 0 warnings.
- `dotnet test` → 112/112 passed (Core tests unchanged; no UI tests exist).



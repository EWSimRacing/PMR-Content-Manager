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


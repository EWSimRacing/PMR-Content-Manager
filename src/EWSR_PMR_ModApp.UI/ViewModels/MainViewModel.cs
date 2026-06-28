using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.GameDetection;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.UI.Dialogs;
using EWSR_PMR_ModApp.UI.Infrastructure;
using Microsoft.Win32;

namespace EWSR_PMR_ModApp.UI.ViewModels;

/// <summary>
/// Primary ViewModel — coordinates install, mod list, progress, and writer selection.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IGameLocator    _gameLocator;
    private readonly IManifestStore  _manifestStore;
    private readonly ISyncEngine     _syncEngine;
    private readonly UISettingsStore _settingsStore;
    private readonly TimeProvider    _clock;
    private readonly IHookRunner     _hookRunner;
    /// <summary>Resolves the correct writer at operation time based on DataRoot writability.</summary>
    private readonly Func<string, IElevatedWriter> _writerFactory;

    private string?  _dataRoot;
    private bool     _isBusy;
    private bool     _isDragOver;
    private bool     _isSettingsVisible;
    private bool     _isGameNotFoundVisible;
    private string   _statusText = "Ready";
    private int      _progressValue;

    // SettingsViewModel is injected lazily to avoid circular DI
    private SettingsViewModel? _settingsViewModel;

    /// <summary>Version string from the executing assembly, e.g. "1.0.0".</summary>
    public static string AppVersion { get; } =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public MainViewModel(
        IGameLocator    gameLocator,
        IManifestStore  manifestStore,
        ISyncEngine     syncEngine,
        UISettingsStore settingsStore,
        TimeProvider    clock,
        IHookRunner     hookRunner,
        Func<string, IElevatedWriter> writerFactory)
    {
        _gameLocator   = gameLocator;
        _manifestStore = manifestStore;
        _syncEngine    = syncEngine;
        _settingsStore = settingsStore;
        _clock         = clock;
        _hookRunner    = hookRunner;
        _writerFactory = writerFactory;

        Mods = new ObservableCollection<ModItemViewModel>();

        BrowseCommand             = new AsyncRelayCommand(BrowseAsync,   () => !IsBusy);
        ReapplyCommand            = new AsyncRelayCommand(ReapplyAsync,   () => !IsBusy);
        ToggleSettingsCommand     = new RelayCommand(() => IsSettingsVisible = !IsSettingsVisible);
        HomeCommand               = new RelayCommand(() => IsSettingsVisible = false);
        OpenSettingsForGameCommand = new RelayCommand(() => IsSettingsVisible = true);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Properties
    // ─────────────────────────────────────────────────────────────────────────

    public ObservableCollection<ModItemViewModel> Mods { get; }

    public string? DataRoot
    {
        get => _dataRoot;
        private set => SetField(ref _dataRoot, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
                OnPropertyChanged(nameof(IsNotBusy));
        }
    }

    public bool IsNotBusy => !_isBusy;

    public bool IsDragOver
    {
        get => _isDragOver;
        set => SetField(ref _isDragOver, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set => SetField(ref _isSettingsVisible, value);
    }

    /// <summary>
    /// True when game auto-detection failed at startup. Drives the "game not found" banner.
    /// Cleared as soon as a valid DataRoot is applied.
    /// </summary>
    public bool IsGameNotFoundVisible
    {
        get => _isGameNotFoundVisible;
        private set => SetField(ref _isGameNotFoundVisible, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public int ProgressValue
    {
        get => _progressValue;
        private set => SetField(ref _progressValue, value);
    }

    /// <summary>Injected after construction to avoid circular DI dependency.</summary>
    public SettingsViewModel? SettingsViewModel
    {
        get => _settingsViewModel;
        set => SetField(ref _settingsViewModel, value);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Commands
    // ─────────────────────────────────────────────────────────────────────────

    public AsyncRelayCommand BrowseCommand             { get; }
    public AsyncRelayCommand ReapplyCommand            { get; }
    public RelayCommand      ToggleSettingsCommand     { get; }
    public RelayCommand      HomeCommand               { get; }
    public RelayCommand      OpenSettingsForGameCommand { get; }

    // ─────────────────────────────────────────────────────────────────────────
    // Initialization
    // ─────────────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        SetStatus("Locating game…", 0);

        var settings = _settingsStore.Load();
        var locatorResult = await _gameLocator.LocateAsync(settings.UserConfiguredGamePath);

        if (!locatorResult.Found)
        {
            IsGameNotFoundVisible = true;
            SetStatus("Game not found — configure path in Settings.", 0);
            IsSettingsVisible = true;
            return;
        }

        IsGameNotFoundVisible = false;
        DataRoot = locatorResult.DataRoot;
        SetStatus("Ready — drop a .zip to install.", 0);

        await RefreshModListAsync();
    }

    /// <summary>Called from SettingsViewModel when the user applies a new data root path.</summary>
    public async Task ApplyDataRootAsync(string newDataRoot)
    {
        IsGameNotFoundVisible = false;
        DataRoot = newDataRoot;
        SetStatus("Path updated — ready.", 0);

        await RefreshModListAsync();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Install
    // ─────────────────────────────────────────────────────────────────────────

    private async Task BrowseAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title            = "Select mod zip file(s)",
            Filter           = "Zip archives|*.zip",
            Multiselect      = true,
        };

        if (dialog.ShowDialog() != true) return;
        await InstallZipsAsync(dialog.FileNames);
    }

    public async Task InstallZipsAsync(IEnumerable<string> zipPaths)
    {
        if (DataRoot is null)
        {
            MessageBox.Show(
                "Game data path is not configured. Go to Settings to set it.",
                "No Game Path",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            foreach (string zipPath in zipPaths)
            {
                string modName = Path.GetFileNameWithoutExtension(zipPath);
                SetStatus($"Installing {modName}…", 0);

                var progress = MakeProgress();
                InstallPlan? plan = null;
                try
                {
                    // Step 1: pure prepare — validates zip, stages, resolves mappings.
                    plan = await _syncEngine.PrepareInstallAsync(
                        zipPath,
                        DataRoot,
                        modName,
                        confirmAmbiguous: ResolveAmbiguousMappingsAsync,
                        progress: progress);

                    // Step 2: execute file writes via the appropriate writer.
                    var request = new WritePlanRequest
                    {
                        Operation     = WritePlanOperation.Install,
                        DataRoot      = plan.DataRoot,
                        GameRoot      = plan.GameRoot,
                        ModId         = plan.ModId,
                        FilesToCopy   = plan.FilesToCopy,
                        FilesToBackup = plan.FilesToBackup
                    };

                    var writer      = _writerFactory(DataRoot);
                    var writeResult = await writer.ExecuteAsync(request);

                    if (!writeResult.Success)
                    {
                        ShowWriteFailure(writeResult, "Install", modName);
                        continue;
                    }

                    // Step 3: cache payload to AppData so reapply-after-update works.
                    SetStatus($"Caching {modName} payload…", 85);
                    CachePayload(plan);
                    CacheHookScripts(plan);

                    // Step 4: update manifest (AppData — no elevation needed).
                    SetStatus("Updating manifest…", 90);
                    var modEntry  = BuildModEntry(plan);
                    var conflicts = await _manifestStore.DetectConflictsAsync(modEntry);
                    await _manifestStore.AddOrUpdateModAsync(modEntry);

                    // Non-conflict warnings (skipped/unmatched files) stay in the
                    // plain warnings list; file conflicts get their own calm notice.
                    var warnings = plan.Warnings.ToList();
                    if (warnings.Count > 0)
                    {
                        var warningsDlg = new WarningsDialog(modName, warnings)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        warningsDlg.ShowDialog();
                    }

                    // Surface inter-mod file conflicts as a friendly, informational
                    // banner instead of an alarming warning. Last-write-wins, so the
                    // freshly installed mod is active — nothing actually broke.
                    if (conflicts.Count > 0)
                    {
                        var groups = conflicts
                            .GroupBy(c => c.Item1)   // existing mod id
                            .Select(g => new ConflictDialog.ConflictGroup(
                                NewModName:   modName,
                                OtherModName: ResolveModName(g.Key),
                                Files:        g.Select(c => c.Item3).ToList()))
                            .ToList();

                        var conflictDlg = new ConflictDialog(modName, groups)
                        {
                            Owner = Application.Current.MainWindow
                        };
                        conflictDlg.ShowDialog();
                    }

                    SetStatus($"Installed {writeResult.FilesCopied} file(s) from {modName}.", 100);

                    // Step 5: Run post-install hook if declared.
                    if (plan.PostInstallHook is { } hook)
                    {
                        string cachedScript = Path.Combine(AppPaths.ScriptsDirForMod(plan.ModId), hook.ScriptName);
                        var hookDlg = new HookConfirmDialog(
                            modName, hook.ScriptName, hook.Description,
                            hook.RequiresElevation, isPostInstall: true)
                        {
                            Owner = Application.Current.MainWindow
                        };

                        if (hookDlg.ShowDialog() == true)
                        {
                            SetStatus("Running post-install script…", 100);
                            var hookResult = await _hookRunner.RunAsync(
                                cachedScript, DataRoot!, plan.ModId, hook.RequiresElevation);

                            if (!hookResult.Success)
                            {
                                MessageBox.Show(
                                    $"The post-install script reported an error:\n\n{hookResult.ErrorMessage}" +
                                    "\n\nThe mod files are still installed correctly.",
                                    "Script Warning",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    SetStatus("Install cancelled.", 0);
                    continue;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"An unexpected error occurred installing {modName}:\n\n{ex.Message}",
                        "Install Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    SetStatus($"Error installing {modName}.", 0);
                    continue;
                }
                finally
                {
                    if (plan is not null)
                        _syncEngine.CleanupInstallPlan(plan);
                }
            }
        }
        finally
        {
            IsBusy = false;
            await RefreshModListAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Uninstall (called by ModItemViewModel)
    // ─────────────────────────────────────────────────────────────────────────

    public async Task UninstallModAsync(string modId, string modName)
    {
        var confirm = MessageBox.Show(
            $"Uninstall \"{modName}\"?\n\nOriginal game files will be restored.",
            "Confirm Uninstall",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        IsBusy = true;
        try
        {
            SetStatus($"Uninstalling {modName}…", 0);

            UninstallPlan plan;
            try
            {
                plan = await _syncEngine.PrepareUninstallAsync(modId, DataRoot ?? string.Empty);
            }
            catch (KeyNotFoundException ex)
            {
                MessageBox.Show($"Cannot uninstall: {ex.Message}", "Uninstall Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                SetStatus("Uninstall failed.", 0);
                return;
            }

            var request = new WritePlanRequest
            {
                Operation     = WritePlanOperation.Uninstall,
                DataRoot      = plan.DataRoot,
                GameRoot      = plan.GameRoot,
                ModId         = plan.ModId,
                FilesToDelete = plan.NewFilesToDelete
            };

            var writer      = _writerFactory(plan.DataRoot);
            var writeResult = await writer.ExecuteAsync(request);

            if (!writeResult.Success)
            {
                ShowWriteFailure(writeResult, "Uninstall", modName);
                return;
            }

            // Run post-uninstall hook if the mod declared one (e.g. navlines cleanup).
            if (plan.PostUninstallScriptPath is { } scriptPath)
            {
                var hookDlg = new HookConfirmDialog(
                    modName,
                    Path.GetFileName(scriptPath),
                    plan.PostUninstallDescription,
                    plan.PostUninstallRequiresElevation,
                    isPostInstall: false)
                {
                    Owner = Application.Current.MainWindow
                };

                if (hookDlg.ShowDialog() == true)
                {
                    SetStatus("Running post-uninstall script…", 90);
                    var hookResult = await _hookRunner.RunAsync(
                        scriptPath, plan.DataRoot, plan.ModId, plan.PostUninstallRequiresElevation);

                    if (!hookResult.Success)
                    {
                        MessageBox.Show(
                            $"The post-uninstall script reported an error:\n\n{hookResult.ErrorMessage}" +
                            "\n\nGame files have already been restored.",
                            "Script Warning",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }
            }

            // Clean up backup directory (AppData — no elevation needed).
            TryDeleteDirectory(AppPaths.BackupDirForMod(plan.ModId));

            // Clean up cached hook scripts.
            TryDeleteDirectory(AppPaths.ScriptsDirForMod(plan.ModId));

            // Remove from manifest.
            await _manifestStore.RemoveModAsync(plan.ModId);

            SetStatus($"Uninstalled {modName} — {writeResult.FilesBackedUp} file(s) restored.", 100);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error uninstalling {modName}:\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            SetStatus("Uninstall error.", 0);
        }
        finally
        {
            IsBusy = false;
            await RefreshModListAsync();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reapply
    // ─────────────────────────────────────────────────────────────────────────

    private async Task ReapplyAsync()
    {
        if (DataRoot is null)
        {
            MessageBox.Show("Game data path not configured.", "No Game Path",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            SetStatus("Checking for reverted mods…", 0);
            var plan = await _syncEngine.PrepareReapplyAsync(DataRoot);

            if (plan.ModsToReapply.Count == 0)
            {
                SetStatus("All mods intact — nothing to reapply.", 0);
                MessageBox.Show("All mods are intact. No files have been reverted.",
                    "All Mods OK", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetStatus($"Reapplying {plan.ModsToReapply.Count} mod(s)…", 0);

            // Batch all mods into a single write request — one UAC prompt total.
            var allFiles = plan.ModsToReapply
                .SelectMany(m => m.FilesToCopy)
                .ToList();

            var request = new WritePlanRequest
            {
                Operation   = WritePlanOperation.Reapply,
                DataRoot    = plan.DataRoot,
                GameRoot    = plan.GameRoot,
                ModId       = "reapply",
                FilesToCopy = allFiles
            };

            var writer      = _writerFactory(plan.DataRoot);
            var writeResult = await writer.ExecuteAsync(request);

            if (!writeResult.Success)
            {
                ShowWriteFailure(writeResult, "Reapply", null);
                return;
            }

            var errors = writeResult.Errors
                .Select(e => $"'{e.RelativePath}': {e.Message}")
                .ToList();

            if (errors.Count > 0)
            {
                var warningsDlg = new WarningsDialog("Reapply", errors)
                {
                    Owner = Application.Current.MainWindow
                };
                warningsDlg.ShowDialog();
            }

            SetStatus($"Reapplied {writeResult.FilesCopied} file(s) across {plan.ModsToReapply.Count} mod(s).", 100);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error during reapply:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            SetStatus("Reapply error.", 0);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ambiguous mapping dialog (InstallAsync callback)
    // ─────────────────────────────────────────────────────────────────────────

    private Task<IReadOnlyList<ResolvedMapping>> ResolveAmbiguousMappingsAsync(
        IReadOnlyList<AmbiguousMapping> ambiguous)
    {
        // Must show the dialog on the UI thread.
        return Application.Current.Dispatcher.InvokeAsync<IReadOnlyList<ResolvedMapping>>(() =>
        {
            var dialog = new AmbiguousMappingDialog(ambiguous)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
            return dialog.GetResolutions();
        }).Task;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Install post-write helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Caches staged mod files to AppData payload directory so reapply-after-update works.</summary>
    private static void CachePayload(InstallPlan plan)
    {
        string payloadDir = AppPaths.PayloadDirForMod(plan.ModId);
        Directory.CreateDirectory(payloadDir);
        foreach (var mapping in plan.MappedFiles)
        {
            string dest = Path.Combine(
                payloadDir,
                mapping.ZipEntry.FullNameInZip.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(mapping.ZipEntry.StagedFilePath, dest, overwrite: true);
        }
    }

    /// <summary>Caches hook scripts to AppData so they survive staging cleanup.</summary>
    private static void CacheHookScripts(InstallPlan plan)
    {
        if (plan.PostInstallHook is null && plan.PostUninstallHook is null)
            return;

        string scriptDir = AppPaths.ScriptsDirForMod(plan.ModId);
        Directory.CreateDirectory(scriptDir);

        if (plan.PostInstallHook is { } piHook)
            File.Copy(piHook.StagedPath, Path.Combine(scriptDir, piHook.ScriptName), overwrite: true);

        if (plan.PostUninstallHook is { } puHook)
            File.Copy(puHook.StagedPath, Path.Combine(scriptDir, puHook.ScriptName), overwrite: true);
    }

    /// <summary>
    /// Builds a <see cref="ModEntry"/> from the install plan.
    /// Hashes are read from staged sources (same bytes as the installed files).
    /// Original hashes are recovered from the backup directory when available.
    /// </summary>
    private ModEntry BuildModEntry(InstallPlan plan)
    {
        string backupDir       = AppPaths.BackupDirForMod(plan.ModId);
        var    installedFiles  = new List<InstalledFileEntry>();

        foreach (var mapping in plan.MappedFiles)
        {
            string backupPath = Path.Combine(
                backupDir,
                mapping.TargetRoot == EWSR_PMR_ModApp.Core.SyncEngine.Mapping.TargetRoot.Game ? "__game__" : "__data__",
                mapping.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

            bool    isNew         = !File.Exists(backupPath);
            string? originalHash  = isNew ? null : HashHelper.ComputeFileHash(backupPath);
            string  installedHash = HashHelper.ComputeFileHash(mapping.ZipEntry.StagedFilePath);

            installedFiles.Add(new InstalledFileEntry
            {
                RelativeTargetPath = mapping.RelativeTargetPath,
                SourcePathInZip    = mapping.ZipEntry.FullNameInZip,
                MappingMethod      = mapping.MappingMethod,
                TargetRoot         = mapping.TargetRoot,
                OriginalFileHash   = originalHash,
                InstalledFileHash  = installedHash,
                IsNewFile          = isNew
            });
        }

        return new ModEntry
        {
            ModId            = plan.ModId,
            ModName          = plan.ModName,
            SourceZipHash    = plan.ZipHash,
            InstallTimestamp = _clock.GetUtcNow(),
            Files            = installedFiles,
            Hooks = (plan.PostInstallHook is not null || plan.PostUninstallHook is not null)
                ? new ModHookMetadata
                  {
                      PostInstallScriptName          = plan.PostInstallHook?.ScriptName,
                      PostInstallDescription         = plan.PostInstallHook?.Description,
                      PostInstallRequiresElevation   = plan.PostInstallHook?.RequiresElevation ?? false,
                      PostUninstallScriptName        = plan.PostUninstallHook?.ScriptName,
                      PostUninstallDescription       = plan.PostUninstallHook?.Description,
                      PostUninstallRequiresElevation = plan.PostUninstallHook?.RequiresElevation ?? false
                  }
                : null
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps a mod id to its human-readable display name using the installed-mod
    /// list. Falls back to the id when the name cannot be resolved.
    /// </summary>
    private string ResolveModName(string modId) =>
        Mods.FirstOrDefault(m => m.ModId == modId)?.ModName ?? modId;

    private async Task RefreshModListAsync()
    {
        try
        {
            var manifest = await _manifestStore.LoadAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                Mods.Clear();
                foreach (var mod in manifest.Mods.Values
                             .OrderByDescending(m => m.InstallTimestamp))
                {
                    Mods.Add(new ModItemViewModel(mod, this));
                }
            });
        }
        catch (Exception ex)
        {
            SetStatus($"Failed to load manifest: {ex.Message}", 0);
        }
    }

    private IProgress<SyncProgress> MakeProgress() =>
        new Progress<SyncProgress>(p =>
        {
            // Progress<T> invokes on the capturing SynchronizationContext (UI thread).
            StatusText    = p.CurrentFile is not null
                ? $"{p.Phase} — {Path.GetFileName(p.CurrentFile)}"
                : p.Phase;
            ProgressValue = p.PercentComplete;
        });

    private void SetStatus(string text, int percent)
    {
        StatusText    = text;
        ProgressValue = percent;
    }

    /// <summary>
    /// Shows a user-friendly error for a failed <see cref="WriteResult"/>.
    /// Distinguishes UAC cancellation from other failures.
    /// </summary>
    private void ShowWriteFailure(WriteResult result, string operation, string? modName)
    {
        bool cancelled = result.ErrorMessage == "Elevation cancelled by user.";
        string title   = cancelled ? $"{operation} Cancelled" : $"{operation} Failed";
        string msg     = cancelled
            ? $"{operation} cancelled — administrator permission was declined."
            : modName is not null
                ? $"Failed to {operation.ToLowerInvariant()} {modName}:\n\n{result.ErrorMessage}"
                : $"{operation} failed:\n\n{result.ErrorMessage}";

        MessageBox.Show(msg, title, MessageBoxButton.OK,
            cancelled ? MessageBoxImage.Warning : MessageBoxImage.Error);

        SetStatus(cancelled
            ? $"{operation} cancelled."
            : $"{operation} failed: {result.ErrorMessage}", 0);
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch { /* Best-effort cleanup — do not throw. */ }
    }
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using EWSR_PMR_ModApp.Core.GameDetection;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.UI.Dialogs;
using EWSR_PMR_ModApp.UI.Infrastructure;
using Microsoft.Win32;

namespace EWSR_PMR_ModApp.UI.ViewModels;

/// <summary>
/// Primary ViewModel — coordinates install, mod list, progress, and elevation state.
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    private readonly IGameLocator    _gameLocator;
    private readonly IManifestStore  _manifestStore;
    private readonly ISyncEngine     _syncEngine;
    private readonly UISettingsStore _settingsStore;

    private string?  _dataRoot;
    private bool     _isBusy;
    private bool     _isDragOver;
    private bool     _needsElevation;
    private bool     _isSettingsVisible;
    private string   _statusText = "Ready";
    private int      _progressValue;

    // SettingsViewModel is injected lazily to avoid circular DI
    private SettingsViewModel? _settingsViewModel;

    public MainViewModel(
        IGameLocator    gameLocator,
        IManifestStore  manifestStore,
        ISyncEngine     syncEngine,
        UISettingsStore settingsStore)
    {
        _gameLocator   = gameLocator;
        _manifestStore = manifestStore;
        _syncEngine    = syncEngine;
        _settingsStore = settingsStore;

        Mods = new ObservableCollection<ModItemViewModel>();

        BrowseCommand          = new AsyncRelayCommand(BrowseAsync,          () => !IsBusy);
        ReapplyCommand         = new AsyncRelayCommand(ReapplyAsync,          () => !IsBusy);
        RestartElevatedCommand = new RelayCommand(RestartElevated);
        ToggleSettingsCommand  = new RelayCommand(() => IsSettingsVisible = !IsSettingsVisible);
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

    public bool NeedsElevation
    {
        get => _needsElevation;
        private set => SetField(ref _needsElevation, value);
    }

    public bool IsSettingsVisible
    {
        get => _isSettingsVisible;
        set => SetField(ref _isSettingsVisible, value);
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

    public AsyncRelayCommand BrowseCommand          { get; }
    public AsyncRelayCommand ReapplyCommand         { get; }
    public RelayCommand      RestartElevatedCommand { get; }
    public RelayCommand      ToggleSettingsCommand  { get; }

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
            SetStatus("Game not found — configure path in Settings.", 0);
            NeedsElevation    = false;
            IsSettingsVisible = true;
            return;
        }

        DataRoot       = locatorResult.DataRoot;
        NeedsElevation = !_gameLocator.CanWriteDataRoot(DataRoot!);

        SetStatus(NeedsElevation
            ? "Admin rights required to install mods."
            : "Ready — drop a .zip to install.", 0);

        await RefreshModListAsync();
    }

    /// <summary>Called from SettingsViewModel when the user applies a new data root path.</summary>
    public async Task ApplyDataRootAsync(string newDataRoot)
    {
        DataRoot       = newDataRoot;
        NeedsElevation = !_gameLocator.CanWriteDataRoot(newDataRoot);

        SetStatus(NeedsElevation
            ? "Admin rights required."
            : "Path updated — ready.", 0);

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

                InstallResult result;
                try
                {
                    result = await _syncEngine.InstallAsync(
                        zipPath,
                        DataRoot,
                        modName,
                        confirmAmbiguous: ResolveAmbiguousMappingsAsync,
                        progress: progress);
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

                if (!result.Success)
                {
                    MessageBox.Show(
                        $"Failed to install {modName}:\n\n{result.ErrorMessage}",
                        "Install Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    SetStatus($"Install failed: {result.ErrorMessage}", 0);
                    continue;
                }

                // Surface warnings/collisions prominently.
                if (result.Warnings.Count > 0)
                {
                    var warningsDlg = new WarningsDialog(modName, result.Warnings)
                    {
                        Owner = Application.Current.MainWindow
                    };
                    warningsDlg.ShowDialog();
                }

                SetStatus($"Installed {result.FilesInstalled} file(s) from {modName}.", 100);
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
            var progress = MakeProgress();

            var result = await _syncEngine.UninstallAsync(
                modId, DataRoot ?? string.Empty, progress);

            if (!result.Success)
            {
                MessageBox.Show(
                    $"Uninstall failed:\n\n{result.ErrorMessage}",
                    "Uninstall Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                SetStatus($"Uninstall failed: {result.ErrorMessage}", 0);
            }
            else
            {
                SetStatus($"Uninstalled {modName} — {result.FilesRestored} file(s) restored.", 100);
            }
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
            var statuses = await _syncEngine.CheckForRevertedModsAsync(DataRoot);

            int revertedCount = statuses.Count(s =>
                s.State == Core.SyncEngine.ModRevertState.Reverted);

            if (revertedCount == 0)
            {
                SetStatus("All mods intact — nothing to reapply.", 0);
                MessageBox.Show("All mods are intact. No files have been reverted.",
                    "All Mods OK", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SetStatus($"Reapplying {revertedCount} reverted mod(s)…", 0);
            var progress = MakeProgress();
            var result   = await _syncEngine.ReapplyRevertedModsAsync(DataRoot, progress);

            if (result.Errors.Count > 0)
            {
                var warningsDlg = new WarningsDialog("Reapply", result.Errors)
                {
                    Owner = Application.Current.MainWindow
                };
                warningsDlg.ShowDialog();
            }

            SetStatus($"Reapplied {result.FilesReapplied} file(s) across {result.ModsReapplied} mod(s).", 100);
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
    // Elevation
    // ─────────────────────────────────────────────────────────────────────────

    private void RestartElevated()
    {
        var executablePath = Environment.ProcessPath
            ?? System.Reflection.Assembly.GetExecutingAssembly().Location;

        var psi = new ProcessStartInfo(executablePath)
        {
            Verb            = "runas",
            UseShellExecute = true
        };

        try
        {
            Process.Start(psi);
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // Error 1223 = The operation was cancelled by the user (UAC dialog dismissed).
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
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

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
}

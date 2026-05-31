using System.Windows;
using Microsoft.Win32;
using EWSR_PMR_ModApp.Core.GameDetection;
using EWSR_PMR_ModApp.UI.Infrastructure;

namespace EWSR_PMR_ModApp.UI.ViewModels;

/// <summary>
/// ViewModel for the Settings panel. Owns game-path configuration.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly IGameLocator    _gameLocator;
    private readonly UISettingsStore _settingsStore;
    private readonly MainViewModel   _mainVm;

    private string? _configuredPath;

    public SettingsViewModel(
        IGameLocator    gameLocator,
        UISettingsStore settingsStore,
        MainViewModel   mainVm)
    {
        _gameLocator   = gameLocator;
        _settingsStore = settingsStore;
        _mainVm        = mainVm;

        // Restore persisted path.
        _configuredPath = _settingsStore.Load().UserConfiguredGamePath;

        BrowsePathCommand = new RelayCommand(BrowsePath);
        ApplyPathCommand  = new AsyncRelayCommand(ApplyPathAsync);
    }

    /// <summary>The path the user has typed or selected.</summary>
    public string? ConfiguredPath
    {
        get => _configuredPath;
        set => SetField(ref _configuredPath, value);
    }

    public RelayCommand      BrowsePathCommand { get; }
    public AsyncRelayCommand ApplyPathCommand  { get; }

    private void BrowsePath()
    {
        var dialog = new OpenFolderDialog
        {
            Title            = "Select Project Motor Racing 'data' folder",
            Multiselect      = false,
        };

        if (dialog.ShowDialog() == true)
            ConfiguredPath = dialog.FolderName;
    }

    private async Task ApplyPathAsync()
    {
        string? path = ConfiguredPath?.Trim();

        // Re-run game locator with the new path (or auto-detect if cleared).
        var result = await _gameLocator.LocateAsync(
            string.IsNullOrWhiteSpace(path) ? null : path);

        if (!result.Found)
        {
            MessageBox.Show(
                $"Could not validate the selected path:\n\n{result.FailureReason}",
                "Invalid Path",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Persist and notify MainViewModel.
        var settings = _settingsStore.Load();
        settings.UserConfiguredGamePath = string.IsNullOrWhiteSpace(path) ? null : path;
        _settingsStore.Save(settings);

        await _mainVm.ApplyDataRootAsync(result.DataRoot!);

        MessageBox.Show(
            $"Game data path updated to:\n{result.DataRoot}",
            "Path Updated",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}

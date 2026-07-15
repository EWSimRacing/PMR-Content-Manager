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

        // If the user gave the game root (without \data), try appending \data automatically.
        if (!result.Found && !string.IsNullOrWhiteSpace(path))
        {
            var withData = System.IO.Path.Combine(path!, "data");
            var retryResult = await _gameLocator.LocateAsync(withData);
            if (retryResult.Found)
            {
                result = retryResult;
                ConfiguredPath = withData;
                path = withData;
            }
        }

        if (!result.Found)
        {
            MessageBox.Show(
                $"Could not validate the selected path:\n\n{result.FailureReason}",
                "Invalid Path",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Show warning if junction resolved to a different path than what the user typed.
        if (result.Warning is not null)
        {
            MessageBox.Show(
                result.Warning,
                "Path Mismatch",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Update the displayed path to match what the game actually uses.
            ConfiguredPath = result.DataRoot;
        }

        // Persist and notify MainViewModel.
        var settings = _settingsStore.Load();
        settings.UserConfiguredGamePath = string.IsNullOrWhiteSpace(path) ? null : path;
        _settingsStore.Save(settings);

        await _mainVm.ApplyDataRootAsync(result.DataRoot!);

        MessageBox.Show(
            $"Game data path updated to:\n{result.DataRoot}\n\n(Source: {FormatSource(result.Source)})",
            "Path Updated",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static string FormatSource(LocationSource source) => source switch
    {
        LocationSource.JunctionResolved => "PMR game junction (auto-detected)",
        LocationSource.UserConfigured   => "Manual configuration",
        LocationSource.DefaultPath      => "Default install location",
        LocationSource.SteamDetected    => "Steam library detection",
        _                               => source.ToString()
    };
}

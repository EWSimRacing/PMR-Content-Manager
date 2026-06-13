using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.GameDetection;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.ZipHandling;
using EWSR_PMR_ModApp.UI.Infrastructure;
using EWSR_PMR_ModApp.UI.ViewModels;

namespace EWSR_PMR_ModApp.UI;

/// <summary>
/// Application entry point. Sets up the DI container and launches the main window.
/// </summary>
public partial class App : Application
{
    private IServiceProvider? _services;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        // Resolve MainViewModel; inject the SettingsViewModel reference.
        var mainVm     = _services.GetRequiredService<MainViewModel>();
        var settingsVm = _services.GetRequiredService<SettingsViewModel>();
        mainVm.SettingsViewModel = settingsVm;

        var mainWindow = new MainWindow(mainVm);
        mainWindow.Show();

        // First-run consent: show once before the app will install anything.
        var settingsStore = _services.GetRequiredService<UISettingsStore>();
        var uiSettings = settingsStore.Load();
        if (!uiSettings.HasShownConsentDialog)
        {
            var result = MessageBox.Show(
                "PMR CM modifies Project Motor Racing game files.\n\n" +
                "Your original files are automatically backed up before any changes " +
                "and can be fully restored at any time using the Uninstall button.\n\n" +
                "Continue?",
                "PMR CM — Before You Start",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.OK)
            {
                Shutdown(0);
                return;
            }

            uiSettings.HasShownConsentDialog = true;
            settingsStore.Save(uiSettings);
        }

        // Async init: locate game, load manifest.
        _ = mainVm.InitializeAsync();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Core infrastructure ──────────────────────────────────────────────
        services.AddSingleton<IFileSystem, RealFileSystem>();
        services.AddSingleton(TimeProvider.System);

        // ── Core services ────────────────────────────────────────────────────
        services.AddSingleton<IGameLocator,     GameLocator>();
        services.AddSingleton<IManifestStore,   ManifestStore>();
        services.AddSingleton<IZipService,      ZipService>();
        services.AddSingleton<IMappingResolver, MappingResolver>();
        services.AddSingleton<IBackupService,   BackupService>();
        services.AddSingleton<ISyncEngine,      SyncEngine>();

        // ── Elevated writer factory ──────────────────────────────────────────
        // Resolved at operation time so DataRoot changes (e.g. via Settings) are reflected.
        services.AddSingleton<Func<string, IElevatedWriter>>(sp =>
        {
            var locator = sp.GetRequiredService<IGameLocator>();
            return dataRoot => locator.CanWriteDataRoot(dataRoot)
                ? (IElevatedWriter) new InProcessWriter()
                : new HelperProcessWriter();
        });

        // ── UI layer ─────────────────────────────────────────────────────────
        services.AddSingleton<UISettingsStore>();
        services.AddSingleton<MainViewModel>();
        // SettingsViewModel depends on MainViewModel — factory avoids circular ctor.
        services.AddSingleton<SettingsViewModel>(sp => new SettingsViewModel(
            sp.GetRequiredService<IGameLocator>(),
            sp.GetRequiredService<UISettingsStore>(),
            sp.GetRequiredService<MainViewModel>()));
    }
}


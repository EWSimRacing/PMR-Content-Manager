using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.UI.Infrastructure;

namespace EWSR_PMR_ModApp.UI.ViewModels;

/// <summary>Represents one installed mod in the mod list.</summary>
public sealed class ModItemViewModel : ViewModelBase
{
    private readonly ModEntry _entry;
    private readonly MainViewModel _parent;

    public ModItemViewModel(ModEntry entry, MainViewModel parent)
    {
        _entry  = entry;
        _parent = parent;

        UninstallCommand = new AsyncRelayCommand(
            UninstallAsync,
            () => !_parent.IsBusy);
    }

    public string ModId   => _entry.ModId;
    public string ModName => _entry.ModName;
    public int    FileCount => _entry.Files.Count;

    public string InstallDate =>
        _entry.InstallTimestamp.ToLocalTime().ToString("yyyy-MM-dd");

    public AsyncRelayCommand UninstallCommand { get; }

    private Task UninstallAsync() => _parent.UninstallModAsync(ModId, ModName);
}

namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Optional lifecycle hook declarations from <c>modinfo.json</c>.
/// Hook scripts are cached in the CM application data directory and
/// are never copied into the game data tree.
/// </summary>
public sealed class ModHooks
{
    /// <summary>Script to run after all mod files have been installed to the game directory.</summary>
    public HookScript? PostInstall { get; set; }

    /// <summary>Script to run after CM restores backed-up originals during uninstall.</summary>
    public HookScript? PostUninstall { get; set; }
}

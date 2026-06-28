namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>
/// Lifecycle hook metadata persisted in the manifest for a mod.
/// Allows CM to locate and run hook scripts during post-install re-runs and uninstall.
/// </summary>
public sealed class ModHookMetadata
{
    /// <summary>Filename of the post-install script cached under ScriptsDirForMod, or null.</summary>
    public string? PostInstallScriptName { get; init; }

    /// <summary>Human-readable description of the post-install hook, or null.</summary>
    public string? PostInstallDescription { get; init; }

    /// <summary>Whether the post-install hook requires UAC elevation.</summary>
    public bool PostInstallRequiresElevation { get; init; }

    /// <summary>Filename of the post-uninstall script cached under ScriptsDirForMod, or null.</summary>
    public string? PostUninstallScriptName { get; init; }

    /// <summary>Human-readable description of the post-uninstall hook, or null.</summary>
    public string? PostUninstallDescription { get; init; }

    /// <summary>Whether the post-uninstall hook requires UAC elevation.</summary>
    public bool PostUninstallRequiresElevation { get; init; }
}

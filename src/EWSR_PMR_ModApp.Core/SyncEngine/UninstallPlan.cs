namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Output of <see cref="ISyncEngine.PrepareUninstallAsync"/>.
/// Carries everything needed to execute the uninstall write operations.
/// </summary>
public sealed class UninstallPlan
{
    /// <summary>Mod identifier — used to locate the backup directory.</summary>
    public required string ModId { get; init; }

    /// <summary>Human-readable display name (for progress reporting).</summary>
    public required string ModName { get; init; }

    /// <summary>Absolute path to the game data root.</summary>
    public required string DataRoot { get; init; }

    /// <summary>Absolute path to the game root.</summary>
    public required string GameRoot { get; init; }

    /// <summary>
    /// Relative paths under <see cref="DataRoot"/> for files that were brand-new
    /// (no backup exists) and must be deleted during uninstall.
    /// </summary>
    public required IReadOnlyList<Elevation.FileTargetSpec> NewFilesToDelete { get; init; }

    /// <summary>
    /// Number of files that have backups and will be restored.
    /// Used for result reporting only — actual restore enumerates the backup directory.
    /// </summary>
    public required int BackedUpFileCount { get; init; }

    /// <summary>
    /// Absolute path to the cached post-uninstall hook script, or <see langword="null"/>
    /// when the mod has no post-uninstall hook or the cached file no longer exists.
    /// </summary>
    public string? PostUninstallScriptPath { get; init; }

    /// <summary>Human-readable description of the post-uninstall hook, for the confirm dialog.</summary>
    public string? PostUninstallDescription { get; init; }

    /// <summary>Whether the post-uninstall hook requires UAC elevation.</summary>
    public bool PostUninstallRequiresElevation { get; init; }
}

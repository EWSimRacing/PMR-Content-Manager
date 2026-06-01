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

    /// <summary>
    /// Relative paths under <see cref="DataRoot"/> for files that were brand-new
    /// (no backup exists) and must be deleted during uninstall.
    /// </summary>
    public required IReadOnlyList<string> NewFilesToDelete { get; init; }

    /// <summary>
    /// Number of files that have backups and will be restored.
    /// Used for result reporting only — actual restore enumerates the backup directory.
    /// </summary>
    public required int BackedUpFileCount { get; init; }
}

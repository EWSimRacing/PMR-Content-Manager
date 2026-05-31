using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Orchestrates the complete mod install, uninstall, and post-update reapply lifecycle.
/// This is the primary service the UI binds to for all mod management operations.
/// </summary>
public interface ISyncEngine
{
    /// <summary>
    /// Full install flow:
    /// validate zip → stage → map → confirm ambiguous → backup originals → copy files →
    /// cache payload → update manifest.
    /// </summary>
    /// <param name="zipPath">Absolute path to the mod zip file.</param>
    /// <param name="dataRoot">Absolute path to the validated game data root.</param>
    /// <param name="modName">Display name shown in the mod list.</param>
    /// <param name="confirmAmbiguous">
    /// UI callback invoked when ambiguous file mappings are found.
    /// The UI presents options to the user and returns their resolved choices.
    /// </param>
    Task<InstallResult> InstallAsync(
        string zipPath,
        string dataRoot,
        string modName,
        Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Uninstalls a mod: restores backed-up originals, removes new files, cleans manifest.
    /// </summary>
    Task<UninstallResult> UninstallAsync(
        string modId,
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Compares all installed mod hashes against on-disk files.
    /// Returns a status per mod indicating whether a game update has reverted any files.
    /// </summary>
    Task<IReadOnlyList<ModUpdateStatus>> CheckForRevertedModsAsync(
        string dataRoot,
        CancellationToken ct = default);

    /// <summary>
    /// Re-copies mod files whose hashes differ from the stored installed hashes,
    /// using the cached payload under <c>%APPDATA%\EWSR_PMR_ModApp\mods\{modId}\</c>.
    /// </summary>
    Task<ReapplyResult> ReapplyRevertedModsAsync(
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);
}

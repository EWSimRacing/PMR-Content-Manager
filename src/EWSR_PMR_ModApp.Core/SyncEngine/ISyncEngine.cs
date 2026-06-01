using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Orchestrates the complete mod install, uninstall, and post-update reapply lifecycle.
/// This is the primary service the UI binds to for all mod management operations.
/// </summary>
public interface ISyncEngine
{
    // ─────────────────────────────────────────────────────────────────────────
    // Install — thin wrapper + split prepare/execute
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Full install flow (thin wrapper):
    /// validate zip → stage → map → confirm ambiguous → backup originals → copy files →
    /// cache payload → update manifest.
    /// </summary>
    Task<InstallResult> InstallAsync(
        string zipPath,
        string dataRoot,
        string modName,
        Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Step 1 of the split install: validate, stage, resolve mappings, resolve ambiguities.
    /// Does NOT touch the game directory.  Returns an <see cref="InstallPlan"/> that the caller
    /// passes to <see cref="ExecuteInstallAsync"/> (in-process) or converts into a
    /// <c>WritePlanRequest</c> for an <c>IElevatedWriter</c>.
    /// </summary>
    /// <remarks>
    /// The caller MUST call <see cref="CleanupInstallPlan"/> when the plan is no longer needed,
    /// even on failure, to remove the staging directory.
    /// </remarks>
    Task<InstallPlan> PrepareInstallAsync(
        string zipPath,
        string dataRoot,
        string modName,
        Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Step 2 of the split install (in-process path): backup originals → copy files →
    /// cache payload → update manifest.
    /// </summary>
    Task<InstallResult> ExecuteInstallAsync(
        InstallPlan plan,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Cleans up the staging directory created by <see cref="PrepareInstallAsync"/>.</summary>
    void CleanupInstallPlan(InstallPlan plan);

    // ─────────────────────────────────────────────────────────────────────────
    // Uninstall
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Uninstalls a mod (thin wrapper): restores backed-up originals, removes new files, cleans manifest.</summary>
    Task<UninstallResult> UninstallAsync(
        string modId,
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Step 1: load manifest, validate mod exists, build <see cref="UninstallPlan"/>.</summary>
    Task<UninstallPlan> PrepareUninstallAsync(
        string modId,
        string dataRoot,
        CancellationToken ct = default);

    /// <summary>Step 2: restore backups, delete new files, prune backup dir, remove from manifest.</summary>
    Task<UninstallResult> ExecuteUninstallAsync(
        UninstallPlan plan,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    // ─────────────────────────────────────────────────────────────────────────
    // Reapply
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Compares all installed mod hashes against on-disk files.
    /// Returns a status per mod indicating whether a game update has reverted any files.
    /// </summary>
    Task<IReadOnlyList<ModUpdateStatus>> CheckForRevertedModsAsync(
        string dataRoot,
        CancellationToken ct = default);

    /// <summary>Re-copies all reverted mod files (thin wrapper).</summary>
    Task<ReapplyResult> ReapplyRevertedModsAsync(
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>Step 1: detect reverted files, build <see cref="ReapplyPlan"/> from payload cache.</summary>
    Task<ReapplyPlan> PrepareReapplyAsync(
        string dataRoot,
        CancellationToken ct = default);

    /// <summary>Step 2: copy payload files back into the data root.</summary>
    Task<ReapplyResult> ExecuteReapplyAsync(
        ReapplyPlan plan,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default);
}

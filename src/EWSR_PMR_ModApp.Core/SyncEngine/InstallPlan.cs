using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Output of <see cref="ISyncEngine.PrepareInstallAsync"/>.
/// Contains everything needed to execute or delegate the write operations for an install,
/// plus the data required to update the manifest after a successful write.
/// </summary>
public sealed class InstallPlan
{
    /// <summary>Stable UUID for this mod installation, generated during prepare.</summary>
    public required string ModId { get; init; }

    /// <summary>Human-readable display name for the mod.</summary>
    public required string ModName { get; init; }

    /// <summary>Absolute path to the validated game data root.</summary>
    public required string DataRoot { get; init; }

    /// <summary>Absolute path to the game root.</summary>
    public required string GameRoot { get; init; }

    /// <summary>SHA-256 hash of the source zip file (for manifest).</summary>
    public required string ZipHash { get; init; }

    /// <summary>
    /// Absolute path to the staging directory created by the zip service.
    /// The caller MUST clean this up (e.g. via <see cref="ISyncEngine.CleanupInstallPlan"/>)
    /// once the plan is no longer needed, whether the install succeeded or failed.
    /// </summary>
    public required string StagingDirectory { get; init; }

    /// <summary>
    /// Relative paths under <see cref="DataRoot"/> that will be overwritten and must be
    /// backed up before any files are copied.
    /// </summary>
    public required IReadOnlyList<FileTargetSpec> FilesToBackup { get; init; }

    /// <summary>Source → target copy specs (staged file → relative target in DataRoot).</summary>
    public required IReadOnlyList<FileCopySpec> FilesToCopy { get; init; }

    /// <summary>
    /// Full mapping results — carried so <see cref="ISyncEngine.ExecuteInstallAsync"/> can
    /// compute per-file hashes and build <c>InstalledFileEntry</c> records for the manifest.
    /// </summary>
    public required IReadOnlyList<FileMappingResult> MappedFiles { get; init; }

    /// <summary>Warnings accumulated during prepare (collisions, unmatched files, etc.).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>
    /// Post-install hook script staged and ready to cache, or <see langword="null"/> when the
    /// mod declares no <c>postInstall</c> hook.
    /// </summary>
    public StagedHook? PostInstallHook { get; init; }

    /// <summary>
    /// Post-uninstall hook script staged and ready to cache, or <see langword="null"/> when the
    /// mod declares no <c>postUninstall</c> hook.
    /// </summary>
    public StagedHook? PostUninstallHook { get; init; }
}

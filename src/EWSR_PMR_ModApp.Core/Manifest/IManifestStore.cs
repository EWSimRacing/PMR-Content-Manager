namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>
/// Persistent store for the application mod manifest.
/// All mutating operations are safe to call concurrently.
/// </summary>
public interface IManifestStore
{
    /// <summary>Loads the manifest from disk. Returns an empty manifest if none exists yet.</summary>
    Task<AppManifest> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the manifest to disk.</summary>
    Task SaveAsync(AppManifest manifest, CancellationToken ct = default);

    /// <summary>Adds or replaces a mod entry, then persists.</summary>
    Task AddOrUpdateModAsync(ModEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes a mod entry by id, then persists. No-op if the mod is not present.
    /// </summary>
    Task RemoveModAsync(string modId, CancellationToken ct = default);

    /// <summary>Returns <c>true</c> if any installed mod owns the given relative target path.</summary>
    Task<bool> IsFileOwnedByModAsync(string relativeTargetPath, CancellationToken ct = default);

    /// <summary>
    /// Detects file-level conflicts: returns tuples of (existingModId, candidateModId, relativePath)
    /// where both mods target the same file.
    /// </summary>
    Task<IReadOnlyList<(string ExistingModId, string CandidateModId, string RelativePath)>>
        DetectConflictsAsync(ModEntry candidate, CancellationToken ct = default);
}

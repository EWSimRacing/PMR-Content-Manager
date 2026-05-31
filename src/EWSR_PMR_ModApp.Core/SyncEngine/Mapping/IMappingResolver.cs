using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>
/// Produces a <see cref="MappingPlan"/> that maps zip entries to target paths under the
/// game data root.  This interface is intentionally pure — no disk writes, no side effects.
/// </summary>
public interface IMappingResolver
{
    /// <summary>
    /// Resolves zip entries to game-data target paths using the hybrid strategy:
    /// modinfo.json (authoritative) → path-overlay (primary) → filename-index fallback.
    /// </summary>
    /// <param name="zipEntries">File entries from the staged zip (no directory entries).</param>
    /// <param name="dataRoot">Absolute path to the game data root (used only for existence checks).</param>
    /// <param name="dataFileIndex">
    /// Pre-built index of all relative file paths currently under the data root.
    /// The resolver uses this for filename-index fallback; the caller builds it from disk.
    /// </param>
    /// <param name="modInfo">
    /// Optional parsed modinfo.json from the zip root; when non-null overrides all heuristics.
    /// </param>
    MappingPlan Resolve(
        IReadOnlyList<ZipEntryInfo> zipEntries,
        string dataRoot,
        IReadOnlyList<string> dataFileIndex,
        ModInfo? modInfo = null);
}

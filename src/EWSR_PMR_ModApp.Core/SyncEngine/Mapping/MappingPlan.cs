using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>
/// Complete output of the mapping resolver, partitioned into four buckets.
/// </summary>
public sealed class MappingPlan
{
    /// <summary>Entries unambiguously mapped to a unique target path — ready to install.</summary>
    public required IReadOnlyList<FileMappingResult> Mapped { get; init; }

    /// <summary>Entries with multiple candidate paths that require user confirmation.</summary>
    public required IReadOnlyList<AmbiguousMapping> Ambiguous { get; init; }

    /// <summary>
    /// Entries that matched no existing file in the data directory.
    /// These are brand-new files if path-overlay placed them; otherwise they need user review.
    /// </summary>
    public required IReadOnlyList<ZipEntryInfo> Unmatched { get; init; }

    /// <summary>
    /// Groups of two or more zip entries that all resolved to the same relative target path.
    /// None of the entries in any collision group are present in <see cref="Mapped"/>.
    /// User resolution is required before any of them can be installed.
    /// </summary>
    public required IReadOnlyList<CollisionMapping> Collisions { get; init; }
}

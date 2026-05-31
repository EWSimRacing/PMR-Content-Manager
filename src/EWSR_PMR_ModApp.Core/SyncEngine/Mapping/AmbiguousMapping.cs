using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>
/// A zip entry that could not be unambiguously mapped to a game data file.
/// The UI must present this to the user for manual confirmation before install proceeds.
/// </summary>
public sealed class AmbiguousMapping
{
    /// <summary>The zip entry that could not be resolved automatically.</summary>
    public required ZipEntryInfo ZipEntry { get; init; }

    /// <summary>
    /// Candidate target paths under the data root, ordered by match confidence (highest first).
    /// </summary>
    public required IReadOnlyList<string> CandidatePaths { get; init; }

    /// <summary>Human-readable explanation of why the mapping is ambiguous.</summary>
    public required string Reason { get; init; }
}

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>
/// Two or more zip entries that resolved to the same relative target path.
/// None of the colliding entries may be auto-installed — user resolution required.
/// </summary>
public sealed class CollisionMapping
{
    /// <summary>The relative target path (under the data root) that multiple entries share.</summary>
    public required string RelativeTargetPath { get; init; }

    /// <summary>
    /// All source entries that resolved to <see cref="RelativeTargetPath"/>.
    /// Guaranteed to contain at least two elements.
    /// </summary>
    public required IReadOnlyList<FileMappingResult> Entries { get; init; }
}

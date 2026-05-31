using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>A single zip entry that has been unambiguously mapped to a game data path.</summary>
public sealed class FileMappingResult
{
    /// <summary>The source entry from the staged zip.</summary>
    public required ZipEntryInfo ZipEntry { get; init; }

    /// <summary>
    /// Resolved relative path under the game data root
    /// (e.g. <c>vehicles/car_a/livery.dds</c>), using forward slashes.
    /// </summary>
    public required string RelativeTargetPath { get; init; }

    /// <summary>Strategy used to produce this mapping.</summary>
    public required MappingMethod MappingMethod { get; init; }
}

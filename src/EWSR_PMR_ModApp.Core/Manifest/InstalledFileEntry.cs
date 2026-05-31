namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>Records a single file written to the game data directory during a mod install.</summary>
public sealed class InstalledFileEntry
{
    /// <summary>Relative path under the game data root (e.g. <c>vehicles/car_a/livery.dds</c>).</summary>
    public required string RelativeTargetPath { get; init; }

    /// <summary>Path of the source inside the mod zip (e.g. <c>data/vehicles/car_a/livery.dds</c>).</summary>
    public required string SourcePathInZip { get; init; }

    /// <summary>How the target path was determined.</summary>
    public required MappingMethod MappingMethod { get; init; }

    /// <summary>
    /// SHA-256 of the original game file before the mod was applied.
    /// <c>null</c> if this is a brand-new file that did not previously exist on disk.
    /// </summary>
    public string? OriginalFileHash { get; init; }

    /// <summary>SHA-256 of the mod file written to disk. Used to detect game-update reversions.</summary>
    public required string InstalledFileHash { get; init; }

    /// <summary>True when no original file existed at this path before the mod was applied.</summary>
    public bool IsNewFile { get; init; }
}

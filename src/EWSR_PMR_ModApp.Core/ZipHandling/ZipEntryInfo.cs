namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Describes a single file entry enumerated from a mod zip archive.
/// </summary>
public sealed class ZipEntryInfo
{
    /// <summary>
    /// Full path of this entry inside the zip, using forward slashes
    /// (e.g. <c>data/vehicles/car_a/livery.dds</c>).
    /// </summary>
    public required string FullNameInZip { get; init; }

    /// <summary>Just the filename portion (e.g. <c>livery.dds</c>).</summary>
    public string FileName => Path.GetFileName(FullNameInZip);

    /// <summary>Absolute path to this entry's extracted file in the staging directory.</summary>
    public required string StagedFilePath { get; init; }

    /// <summary>Uncompressed size in bytes.</summary>
    public long UncompressedSize { get; init; }

    /// <summary>Classification assigned by <see cref="FileClassifier"/>.</summary>
    public SkipCategory Category { get; set; } = SkipCategory.Install;

    /// <summary>Human-readable reason when <see cref="Category"/> != <see cref="SkipCategory.Install"/>.</summary>
    public string? SkipReason { get; set; }
}

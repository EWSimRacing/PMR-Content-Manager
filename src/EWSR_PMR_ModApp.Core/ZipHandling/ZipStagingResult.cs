namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>Result returned by <see cref="IZipService.StageAsync"/>.</summary>
public sealed class ZipStagingResult
{
    /// <summary>Absolute path of the staging directory where the zip was extracted.</summary>
    public required string StagingDirectory { get; init; }

    /// <summary>All file entries extracted from the zip (directories are excluded).</summary>
    public required IReadOnlyList<ZipEntryInfo> Entries { get; init; }

    /// <summary>Parsed <c>modinfo.json</c> from the zip root, or <c>null</c> if none was present.</summary>
    public ModInfo? ModInfo { get; init; }

    /// <summary>SHA-256 hash of the original zip file.</summary>
    public required string ZipHash { get; init; }
}

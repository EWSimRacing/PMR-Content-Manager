namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Validates and stages zip archives for inspection before installation.
/// Files are never written directly to the game directory by this service.
/// </summary>
public interface IZipService
{
    /// <summary>
    /// Verifies the archive is readable and all entries pass their CRC checks.
    /// Returns <c>false</c> for corrupt or partial archives.
    /// </summary>
    Task<bool> ValidateIntegrityAsync(string zipPath, CancellationToken ct = default);

    /// <summary>
    /// Extracts the zip to a new session directory under the app-data staging root.
    /// The caller is responsible for calling <see cref="CleanupStaging"/> when done.
    /// </summary>
    Task<ZipStagingResult> StageAsync(
        string zipPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default);

    /// <summary>Removes a staging directory created by <see cref="StageAsync"/>.</summary>
    void CleanupStaging(string stagingDirectory);
}

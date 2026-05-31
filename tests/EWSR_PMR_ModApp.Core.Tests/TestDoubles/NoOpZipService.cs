using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.Tests.TestDoubles;

/// <summary>
/// Minimal no-op <see cref="IZipService"/> for tests that do not exercise the install path.
/// Calling <see cref="StageAsync"/> throws; all other operations succeed silently.
/// </summary>
public sealed class NoOpZipService : IZipService
{
    public Task<bool> ValidateIntegrityAsync(string zipPath, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<ZipStagingResult> StageAsync(
        string zipPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default) =>
        throw new NotSupportedException("NoOpZipService does not support StageAsync.");

    public void CleanupStaging(string stagingDirectory) { }
}

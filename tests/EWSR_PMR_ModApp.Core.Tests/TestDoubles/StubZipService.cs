using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.Tests.TestDoubles;

/// <summary>
/// A configurable <see cref="IZipService"/> stub for install-path tests.
/// Returns a pre-set <see cref="ZipStagingResult"/> from <see cref="StageAsync"/>;
/// validation always succeeds; cleanup is a no-op.
/// </summary>
public sealed class StubZipService : IZipService
{
    private readonly ZipStagingResult _result;

    public StubZipService(ZipStagingResult result) => _result = result;

    public Task<bool> ValidateIntegrityAsync(string zipPath, CancellationToken ct = default) =>
        Task.FromResult(true);

    public Task<ZipStagingResult> StageAsync(
        string zipPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default) =>
        Task.FromResult(_result);

    public void CleanupStaging(string stagingDirectory) { }
}

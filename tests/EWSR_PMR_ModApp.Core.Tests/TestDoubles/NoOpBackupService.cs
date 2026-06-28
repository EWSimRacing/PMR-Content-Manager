using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Tests.TestDoubles;

/// <summary>
/// Minimal no-op <see cref="IBackupService"/> for tests that do not exercise the backup path.
/// All operations succeed silently.
/// </summary>
public sealed class NoOpBackupService : IBackupService
{
    public Task BackupFilesAsync(
        string modId,
        string dataRoot,
        string gameRoot,
        IEnumerable<FileTargetSpec> targets,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RestoreAsync(
        string modId,
        string dataRoot,
        string gameRoot,
        CancellationToken ct = default) => Task.CompletedTask;

    public Task RestoreAllAsync(string dataRoot, string gameRoot, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task PruneAsync(string modId, CancellationToken ct = default) =>
        Task.CompletedTask;

    public string? GetBackupPath(string modId, string relativeTargetPath, TargetRoot targetRoot = TargetRoot.Data) => null;
}

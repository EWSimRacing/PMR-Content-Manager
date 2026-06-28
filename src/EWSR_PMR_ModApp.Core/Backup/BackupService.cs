using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Backup;

/// <summary>
/// Manages original-game-file backups stored under
/// <c>%APPDATA%\EWSR_PMR_ModApp\backups\{modId}\</c>.
/// </summary>
public sealed class BackupService : IBackupService
{
    private readonly IFileSystem _fs;

    public BackupService(IFileSystem fileSystem) => _fs = fileSystem;

    public async Task BackupFilesAsync(
        string modId,
        string dataRoot,
        string gameRoot,
        IEnumerable<FileTargetSpec> targets,
        CancellationToken ct = default)
    {
        string backupDir = AppPaths.BackupDirForMod(modId);
        _fs.CreateDirectory(backupDir);

        await Task.Run(() =>
        {
            foreach (var target in targets)
            {
                ct.ThrowIfCancellationRequested();
                string relative = target.RelativeTargetPath;
                string source = Path.Combine(
                    GetBaseRoot(dataRoot, gameRoot, target.TargetRoot),
                    relative.Replace('/', Path.DirectorySeparatorChar));

                if (!_fs.FileExists(source)) continue; // New file — nothing to back up.

                string backup = Path.Combine(
                    backupDir,
                    BackupPrefix(target.TargetRoot),
                    relative.Replace('/', Path.DirectorySeparatorChar));
                _fs.CopyFile(source, backup, overwrite: true);
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(
        string modId,
        string dataRoot,
        string gameRoot,
        CancellationToken ct = default)
    {
        string backupDir = AppPaths.BackupDirForMod(modId);
        if (!_fs.DirectoryExists(backupDir)) return;

        // NOTE: Writes to C:\Program Files require admin elevation at runtime.
        await Task.Run(() =>
        {
            foreach (string backupFile in _fs.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string relativeFromBackup = Path.GetRelativePath(backupDir, backupFile);
                var (root, relative) = SplitBackupRelative(relativeFromBackup);
                string dest = Path.Combine(GetBaseRoot(dataRoot, gameRoot, root), relative);
                _fs.CopyFile(backupFile, dest, overwrite: true);
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task RestoreAllAsync(string dataRoot, string gameRoot, CancellationToken ct = default)
    {
        string backupsRoot = AppPaths.BackupsRoot;
        if (!_fs.DirectoryExists(backupsRoot)) return;

        foreach (string modBackupDir in _fs.EnumerateDirectories(backupsRoot))
        {
            ct.ThrowIfCancellationRequested();
            string modId = Path.GetFileName(modBackupDir);
            await RestoreAsync(modId, dataRoot, gameRoot, ct).ConfigureAwait(false);
        }
    }

    public Task PruneAsync(string modId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            string backupDir = AppPaths.BackupDirForMod(modId);
            if (_fs.DirectoryExists(backupDir))
                _fs.DeleteDirectory(backupDir, recursive: true);
        }, ct);

    public string? GetBackupPath(string modId, string relativeTargetPath, TargetRoot targetRoot = TargetRoot.Data)
    {
        string path = Path.Combine(
            AppPaths.BackupDirForMod(modId),
            BackupPrefix(targetRoot),
            relativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
        return _fs.FileExists(path) ? path : null;
    }

    private static string GetBaseRoot(string dataRoot, string gameRoot, TargetRoot targetRoot) =>
        targetRoot == TargetRoot.Game ? gameRoot : dataRoot;

    private static string BackupPrefix(TargetRoot targetRoot) =>
        targetRoot == TargetRoot.Game ? "__game__" : "__data__";

    private static (TargetRoot Root, string RelativePath) SplitBackupRelative(string relativeFromBackup)
    {
        string normalized = relativeFromBackup.Replace('\\', '/');
        int slash = normalized.IndexOf('/');
        if (slash <= 0)
            return (TargetRoot.Data, relativeFromBackup);

        string prefix = normalized[..slash];
        string relative = normalized[(slash + 1)..].Replace('/', Path.DirectorySeparatorChar);
        if (string.Equals(prefix, "__game__", StringComparison.OrdinalIgnoreCase))
            return (TargetRoot.Game, relative);
        if (string.Equals(prefix, "__data__", StringComparison.OrdinalIgnoreCase))
            return (TargetRoot.Data, relative);

        return (TargetRoot.Data, relativeFromBackup);
    }
}

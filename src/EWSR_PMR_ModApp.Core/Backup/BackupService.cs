using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;

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
        IEnumerable<string> relativeTargetPaths,
        CancellationToken ct = default)
    {
        string backupDir = AppPaths.BackupDirForMod(modId);
        _fs.CreateDirectory(backupDir);

        await Task.Run(() =>
        {
            foreach (string relative in relativeTargetPaths)
            {
                ct.ThrowIfCancellationRequested();
                string source = Path.Combine(
                    dataRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar));

                if (!_fs.FileExists(source)) continue; // New file — nothing to back up.

                string backup = Path.Combine(
                    backupDir,
                    relative.Replace('/', Path.DirectorySeparatorChar));
                _fs.CopyFile(source, backup, overwrite: true);
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task RestoreAsync(
        string modId,
        string dataRoot,
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
                string relative = Path.GetRelativePath(backupDir, backupFile);
                string dest     = Path.Combine(dataRoot, relative);
                _fs.CopyFile(backupFile, dest, overwrite: true);
            }
        }, ct).ConfigureAwait(false);
    }

    public async Task RestoreAllAsync(string dataRoot, CancellationToken ct = default)
    {
        string backupsRoot = AppPaths.BackupsRoot;
        if (!_fs.DirectoryExists(backupsRoot)) return;

        foreach (string modBackupDir in _fs.EnumerateDirectories(backupsRoot))
        {
            ct.ThrowIfCancellationRequested();
            string modId = Path.GetFileName(modBackupDir);
            await RestoreAsync(modId, dataRoot, ct).ConfigureAwait(false);
        }
    }

    public Task PruneAsync(string modId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            string backupDir = AppPaths.BackupDirForMod(modId);
            if (_fs.DirectoryExists(backupDir))
                _fs.DeleteDirectory(backupDir, recursive: true);
        }, ct);

    public string? GetBackupPath(string modId, string relativeTargetPath)
    {
        string path = Path.Combine(
            AppPaths.BackupDirForMod(modId),
            relativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
        return _fs.FileExists(path) ? path : null;
    }
}

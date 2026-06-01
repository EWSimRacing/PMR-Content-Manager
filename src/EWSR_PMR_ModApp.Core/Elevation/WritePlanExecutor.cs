using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Stateless, synchronous executor shared between <see cref="InProcessWriter"/> and the elevated
/// <c>EWSR_PMR_ModApp.Helper</c> process.  Uses real <see cref="System.IO"/> — no
/// <c>IFileSystem</c> abstraction — because both callers operate against real files.
/// </summary>
/// <remarks>
/// Backup format matches <c>BackupService</c>:
/// <c>%APPDATA%\EWSR_PMR_ModApp\backups\{modId}\{relativeTargetPath}</c>.
/// </remarks>
public static class WritePlanExecutor
{
    /// <summary>
    /// Executes a validated <see cref="WritePlanRequest"/> and returns a <see cref="WriteResult"/>.
    /// All path validation must have been done by the caller BEFORE this method is invoked.
    /// </summary>
    /// <param name="request">The fully-validated write plan.</param>
    /// <param name="logLine">Optional callback receiving one log line per operation (timestamp already prepended by caller).</param>
    public static WriteResult Execute(WritePlanRequest request, Action<string>? logLine = null)
    {
        int filesCopied   = 0;
        int filesDeleted  = 0;
        int filesBackedUp = 0;
        var errors        = new List<FileOperationError>();

        try
        {
            switch (request.Operation)
            {
                case WritePlanOperation.Install:
                    filesBackedUp = BackupFiles(request.ModId, request.DataRoot, request.FilesToBackup, errors, logLine);
                    if (errors.Count == 0)
                        filesCopied = CopyFiles(request.DataRoot, request.FilesToCopy, errors, logLine);
                    break;

                case WritePlanOperation.Uninstall:
                    filesBackedUp = RestoreBackups(request.ModId, request.DataRoot, errors, logLine);
                    if (errors.Count == 0)
                        filesDeleted = DeleteFiles(request.DataRoot, request.FilesToDelete, errors, logLine);
                    break;

                case WritePlanOperation.Reapply:
                    filesCopied = CopyFiles(request.DataRoot, request.FilesToCopy, errors, logLine);
                    break;
            }
        }
        catch (Exception ex)
        {
            errors.Add(new FileOperationError { RelativePath = "*", Message = ex.ToString() });
        }

        return new WriteResult
        {
            Success       = errors.Count == 0,
            FilesCopied   = filesCopied,
            FilesDeleted  = filesDeleted,
            FilesBackedUp = filesBackedUp,
            Errors        = errors
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Install helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static int BackupFiles(
        string modId,
        string dataRoot,
        IReadOnlyList<string>? relativePaths,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        if (relativePaths is null or { Count: 0 }) return 0;

        string backupDir = AppPaths.BackupDirForMod(modId);
        Directory.CreateDirectory(backupDir);
        int count = 0;

        foreach (string relative in relativePaths)
        {
            string source = Path.Combine(dataRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                log?.Invoke($"  backup skip (new file): {relative}");
                continue; // New file — nothing to back up.
            }

            string dest = Path.Combine(backupDir, relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(source, dest, overwrite: true);
                count++;
                log?.Invoke($"  backed up: {relative}");
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError { RelativePath = relative, Message = ex.Message });
            }
        }

        return count;
    }

    private static int CopyFiles(
        string dataRoot,
        IReadOnlyList<FileCopySpec>? specs,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        if (specs is null or { Count: 0 }) return 0;
        int count = 0;

        foreach (var spec in specs)
        {
            string dest = Path.Combine(dataRoot, spec.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(spec.SourcePath, dest, overwrite: true);
                count++;
                log?.Invoke($"  copied: {spec.RelativeTargetPath}");
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError { RelativePath = spec.RelativeTargetPath, Message = ex.Message });
            }
        }

        return count;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Uninstall helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores all files from the backup directory for <paramref name="modId"/> back into
    /// <paramref name="dataRoot"/>.  Mirrors <c>BackupService.RestoreAsync</c> path layout.
    /// </summary>
    private static int RestoreBackups(
        string modId,
        string dataRoot,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        string backupDir = AppPaths.BackupDirForMod(modId);
        if (!Directory.Exists(backupDir)) return 0;
        int count = 0;

        foreach (string backupFile in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(backupDir, backupFile);
            string dest     = Path.Combine(dataRoot, relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(backupFile, dest, overwrite: true);
                count++;
                log?.Invoke($"  restored: {relative}");
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError { RelativePath = relative, Message = ex.Message });
            }
        }

        return count;
    }

    private static int DeleteFiles(
        string dataRoot,
        IReadOnlyList<string>? relativePaths,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        if (relativePaths is null or { Count: 0 }) return 0;
        int count = 0;

        foreach (string relative in relativePaths)
        {
            string path = Path.Combine(dataRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                log?.Invoke($"  delete skip (already gone): {relative}");
                continue;
            }

            try
            {
                File.Delete(path);
                count++;
                log?.Invoke($"  deleted: {relative}");
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError { RelativePath = relative, Message = ex.Message });
            }
        }

        return count;
    }
}

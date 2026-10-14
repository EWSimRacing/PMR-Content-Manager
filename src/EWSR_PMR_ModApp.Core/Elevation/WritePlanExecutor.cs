using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Stateless, synchronous executor shared between <see cref="InProcessWriter"/> and the elevated
/// <c>EWSR_PMR_ModApp.Helper</c> process. Both callers operate against real files;
/// pre-operation recovery uses the same transaction as the direct Core path.
/// </summary>
/// <remarks>
/// Backup format matches <c>BackupService</c>:
/// <c>%APPDATA%\EWSR_PMR_ModApp\backups\{modId}\__data__\{relativeTargetPath}</c>
/// or <c>...\__game__\{relativeTargetPath}</c>.
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
        FileWriteTransaction? transaction = null;
        bool rollbackAttempted = false;
        bool rollbackSucceeded = false;

        try
        {
            // Verify every native patch baseline before any backup or write, including in the elevated helper.
            foreach (var spec in request.FilesToCopy ?? [])
                ValidateBaseline(request.DataRoot, request.GameRoot, spec);
            if (request.Operation is WritePlanOperation.Install or WritePlanOperation.Reapply)
                transaction = new FileWriteTransaction(new RealFileSystem(), request.DataRoot, request.GameRoot, request.FilesToCopy ?? []);
            switch (request.Operation)
            {
                case WritePlanOperation.Install:
                    filesBackedUp = BackupFiles(request.ModId, request.DataRoot, request.GameRoot, request.FilesToBackup, errors, logLine);
                    if (errors.Count == 0)
                        filesCopied = CopyFiles(request.DataRoot, request.GameRoot, request.FilesToCopy, errors, logLine, transaction!);
                    break;

                case WritePlanOperation.Uninstall:
                    filesBackedUp = RestoreBackups(request.ModId, request.DataRoot, request.GameRoot, errors, logLine);
                    if (errors.Count == 0)
                        filesDeleted = DeleteFiles(request.DataRoot, request.GameRoot, request.FilesToDelete, errors, logLine);
                    break;

                case WritePlanOperation.Reapply:
                    filesCopied = CopyFiles(request.DataRoot, request.GameRoot, request.FilesToCopy, errors, logLine, transaction!);
                    break;
            }
        }
        catch (Exception ex)
        {
            errors.Add(new FileOperationError { RelativePath = "*", Message = ex.ToString() });
        }

        if (transaction is not null)
        {
            if (errors.Count == 0)
            {
                try { transaction.Commit(); }
                catch (Exception ex) { errors.Add(new FileOperationError { RelativePath = "*", Message = ex.ToString() }); }
            }
            if (errors.Count > 0)
            {
                rollbackAttempted = true;
                var rollbackErrors = transaction.Rollback(new IOException(string.Join("\n", errors.Select(e => $"{e.RelativePath}: {e.Message}"))));
                rollbackSucceeded = rollbackErrors.Count == 0;
                errors.AddRange(rollbackErrors);
                if (rollbackSucceeded) filesCopied = 0;
            }
        }

        return new WriteResult
        {
            Success       = errors.Count == 0,
            ErrorMessage  = errors.Count == 0 ? null : string.Join("\n", errors.Select(e => $"{e.RelativePath}: {e.Message}")),
            FilesCopied   = filesCopied,
            FilesDeleted  = filesDeleted,
            FilesBackedUp = filesBackedUp,
            Errors        = errors,
            RollbackAttempted = rollbackAttempted,
            RollbackSucceeded = rollbackSucceeded,
            RecoveryDirectory = rollbackAttempted && !rollbackSucceeded ? transaction?.RecoveryDirectory : null
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Install helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static int BackupFiles(
        string modId,
        string dataRoot,
        string gameRoot,
        IReadOnlyList<FileTargetSpec>? targets,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        if (targets is null or { Count: 0 }) return 0;

        string backupDir = AppPaths.BackupDirForMod(modId);
        Directory.CreateDirectory(backupDir);
        int count = 0;

        foreach (var target in targets)
        {
            string relative = target.RelativeTargetPath;
            string source = Path.Combine(GetBaseRoot(dataRoot, gameRoot, target.TargetRoot), relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(source))
            {
                log?.Invoke($"  backup skip (new file): {relative}");
                continue; // New file — nothing to back up.
            }

            string dest = Path.Combine(backupDir, BackupPrefix(target.TargetRoot), relative.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                if (!File.Exists(dest)) File.Copy(source, dest, overwrite: false);
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
        string gameRoot,
        IReadOnlyList<FileCopySpec>? specs,
        List<FileOperationError> errors,
        Action<string>? log,
        FileWriteTransaction transaction)
    {
        if (specs is null or { Count: 0 }) return 0;
        int count = 0;

        foreach (var spec in specs)
        {
            string dest = Path.Combine(GetBaseRoot(dataRoot, gameRoot, spec.TargetRoot), spec.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                ValidateBaseline(dataRoot, gameRoot, spec);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                transaction.BeforeWrite(dest);
                ManagedFileOperations.Copy(spec.SourcePath, dest, overwrite: true);
                count++;
                log?.Invoke($"  copied: {spec.RelativeTargetPath}");
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError { RelativePath = spec.RelativeTargetPath, Message = ex.Message });
                break;
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
        string gameRoot,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        string backupDir = AppPaths.BackupDirForMod(modId);
        if (!Directory.Exists(backupDir)) return 0;
        int count = 0;

        foreach (string backupFile in Directory.EnumerateFiles(backupDir, "*", SearchOption.AllDirectories))
        {
            string relativeFromBackup = Path.GetRelativePath(backupDir, backupFile);
            var (root, relative) = SplitBackupRelative(relativeFromBackup);
            string dest = Path.Combine(GetBaseRoot(dataRoot, gameRoot, root), relative);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                ManagedFileOperations.Copy(backupFile, dest, overwrite: true, restoreSourceAttributes: true);
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
        string gameRoot,
        IReadOnlyList<FileTargetSpec>? targets,
        List<FileOperationError> errors,
        Action<string>? log)
    {
        if (targets is null or { Count: 0 }) return 0;
        int count = 0;

        foreach (var target in targets)
        {
            string relative = target.RelativeTargetPath;
            string path = Path.Combine(GetBaseRoot(dataRoot, gameRoot, target.TargetRoot), relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                log?.Invoke($"  delete skip (already gone): {relative}");
                continue;
            }

            try
            {
                ManagedFileOperations.Delete(path);
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

    private static string GetBaseRoot(string dataRoot, string gameRoot, TargetRoot targetRoot) =>
        targetRoot == TargetRoot.Game ? gameRoot : dataRoot;

    private static void ValidateBaseline(string dataRoot, string gameRoot, FileCopySpec spec)
    {
        if (spec.ExpectedSourceHash is not null && (!File.Exists(spec.SourcePath)
            || !string.Equals(HashHelper.ComputeFileHash(spec.SourcePath), spec.ExpectedSourceHash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Prepared payload changed for '{spec.RelativeTargetPath}'. Refusing native patch.");
        if (spec.ExpectedTargetHash is null) return;
        string target = Path.Combine(GetBaseRoot(dataRoot, gameRoot, spec.TargetRoot),
            spec.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(target) || !string.Equals(HashHelper.ComputeFileHash(target), spec.ExpectedTargetHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Baseline changed for '{spec.RelativeTargetPath}'. Refusing stale native patch.");
    }

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

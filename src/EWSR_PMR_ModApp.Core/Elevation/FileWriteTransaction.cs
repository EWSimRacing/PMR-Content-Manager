using System.Text.Json;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Captures pre-operation bytes separately from uninstall backups. Failed recovery retains
/// snapshots and a journal for manual recovery; it must never be treated as a completed install.
/// </summary>
internal sealed class FileWriteTransaction
{
    private readonly IFileSystem _fs;
    private readonly List<Snapshot> _snapshots = [];
    public string RecoveryDirectory { get; } =
        Path.Combine(AppPaths.AppDataRoot, "write-recovery", Guid.NewGuid().ToString("N"));

    public FileWriteTransaction(IFileSystem fs, string dataRoot, string gameRoot, IReadOnlyList<FileCopySpec> specs)
    {
        _fs = fs;
        try
        {
            _fs.CreateDirectory(RecoveryDirectory);
            foreach (var spec in specs)
            {
                string target = Path.Combine(spec.TargetRoot == TargetRoot.Game ? gameRoot : dataRoot,
                    spec.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
                if (_snapshots.Any(s => string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase)))
                    continue;
                string? backup = null;
                if (_fs.FileExists(target))
                {
                    backup = Path.Combine(RecoveryDirectory, $"{_snapshots.Count}.bin");
                    _fs.CopyFile(target, backup, overwrite: false);
                }
                _snapshots.Add(new Snapshot(target, backup, backup is null ? null : _fs.GetFileAttributes(target)));
            }
            SaveJournal("prepared");
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    public void BeforeWrite(string target)
    {
        _snapshots.Single(s => string.Equals(s.Target, target, StringComparison.OrdinalIgnoreCase)).Attempted = true;
        // Persist intent before copying: even a throwing copy can truncate its destination.
        SaveJournal("writing");
    }

    public IReadOnlyList<FileOperationError> Rollback(Exception? originalError = null)
    {
        var errors = new List<FileOperationError>();
        foreach (var snapshot in _snapshots.Where(s => s.Attempted).Reverse())
        {
            try
            {
                if (snapshot.Backup is null)
                {
                    if (_fs.FileExists(snapshot.Target)) _fs.DeleteFile(snapshot.Target);
                }
                else
                {
                    if (!Unchanged(snapshot))
                        _fs.CopyFile(snapshot.Backup, snapshot.Target, overwrite: true, restoreSourceAttributes: true);
                    if (snapshot.Attributes is { } attributes && _fs.GetFileAttributes(snapshot.Target) != attributes)
                        _fs.SetFileAttributes(snapshot.Target, attributes);
                }
            }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError
                {
                    RelativePath = snapshot.Target,
                    Message = $"Rollback failed: {ex.Message}. Recovery snapshots/journal: {RecoveryDirectory}. Do not retry until recovered."
                });
            }
        }
        if (errors.Count == 0)
        {
            try { SaveJournal("rolled-back", originalError?.ToString()); }
            catch { /* Recovery is complete even if journal cleanup is blocked. */ }
            Cleanup();
        }
        else
        {
            try { SaveJournal("rollback-failed", originalError?.ToString(), errors); }
            catch (Exception ex)
            {
                errors.Add(new FileOperationError
                {
                    RelativePath = RecoveryDirectory,
                    Message = $"Could not update recovery journal: {ex.Message}. Existing write-intent journal and snapshots retained."
                });
            }
        }
        return errors;
    }

    public void Commit()
    {
        // A cleanup failure is not a write failure. Mark retained journals as committed.
        try { SaveJournal("committed"); }
        catch { /* Target writes are already committed. */ }
        Cleanup();
    }

    private bool Unchanged(Snapshot snapshot)
    {
        if (!_fs.FileExists(snapshot.Target)) return false;
        using var target = _fs.OpenRead(snapshot.Target);
        using var backup = _fs.OpenRead(snapshot.Backup!);
        return HashHelper.ComputeStreamHash(target) == HashHelper.ComputeStreamHash(backup);
    }

    private void SaveJournal(string state, string? originalError = null, IReadOnlyList<FileOperationError>? errors = null) =>
        _fs.WriteAllText(Path.Combine(RecoveryDirectory, "journal.json"),
            JsonSerializer.Serialize(new { State = state, OriginalError = originalError, Errors = errors, Files = _snapshots }));

    private void Cleanup()
    {
        try { _fs.DeleteDirectory(RecoveryDirectory, recursive: true); }
        catch { /* Retained snapshots do not change target state. */ }
    }

    private sealed class Snapshot(string target, string? backup, FileAttributes? attributes)
    {
        public string Target { get; } = target;
        public string? Backup { get; } = backup;
        public FileAttributes? Attributes { get; } = attributes;
        public bool Attempted { get; set; }
    }
}

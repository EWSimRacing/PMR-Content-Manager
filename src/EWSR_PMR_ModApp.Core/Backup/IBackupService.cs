namespace EWSR_PMR_ModApp.Core.Backup;

/// <summary>
/// Manages pre-install backups of original game files, enabling clean uninstall and restore-all.
/// Backups are stored at <c>%APPDATA%\EWSR_PMR_ModApp\backups\{modId}\{relativeTargetPath}</c>.
/// </summary>
public interface IBackupService
{
    /// <summary>
    /// Backs up all game files that the planned install will overwrite.
    /// Files that don't yet exist on disk (new-file installs) are silently skipped.
    /// </summary>
    Task BackupFilesAsync(
        string modId,
        string dataRoot,
        IEnumerable<string> relativeTargetPaths,
        CancellationToken ct = default);

    /// <summary>
    /// Restores all backed-up files for the given mod back into the game data directory.
    /// Called during uninstall.
    /// NOTE: Writing to C:\Program Files requires admin elevation at runtime.
    /// </summary>
    Task RestoreAsync(
        string modId,
        string dataRoot,
        CancellationToken ct = default);

    /// <summary>
    /// Restores backups for all installed mods. Used for a "clean slate / restore originals" operation.
    /// </summary>
    Task RestoreAllAsync(string dataRoot, CancellationToken ct = default);

    /// <summary>
    /// Deletes all backup files for the given mod. Called after uninstall is confirmed.
    /// </summary>
    Task PruneAsync(string modId, CancellationToken ct = default);

    /// <summary>
    /// Returns the absolute path of the backup file for a given mod file,
    /// or <c>null</c> if no backup exists.
    /// </summary>
    string? GetBackupPath(string modId, string relativeTargetPath);
}

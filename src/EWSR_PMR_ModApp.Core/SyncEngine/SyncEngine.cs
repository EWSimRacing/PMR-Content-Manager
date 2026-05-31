using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Orchestrates the complete mod install, uninstall, and reapply lifecycle.
/// </summary>
public sealed class SyncEngine : ISyncEngine
{
    private readonly IZipService      _zipService;
    private readonly IMappingResolver _mappingResolver;
    private readonly IManifestStore   _manifestStore;
    private readonly IBackupService   _backupService;
    private readonly IFileSystem      _fs;
    private readonly TimeProvider     _clock;
    private readonly IFileHasher      _hasher;

    public SyncEngine(
        IZipService      zipService,
        IMappingResolver mappingResolver,
        IManifestStore   manifestStore,
        IBackupService   backupService,
        IFileSystem      fileSystem,
        TimeProvider     clock,
        IFileHasher?     hasher = null)
    {
        _zipService      = zipService;
        _mappingResolver = mappingResolver;
        _manifestStore   = manifestStore;
        _backupService   = backupService;
        _fs              = fileSystem;
        _clock           = clock;
        _hasher          = hasher ?? new RealFileHasher();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Install
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<InstallResult> InstallAsync(
        string zipPath,
        string dataRoot,
        string modName,
        Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();

        // 1. Validate zip integrity.
        Report(progress, "Validating archive", 0);
        if (!await _zipService.ValidateIntegrityAsync(zipPath, ct).ConfigureAwait(false))
            return InstallResult.Failure("The zip archive is corrupt or could not be read.");

        // 2. Stage (extract to app-data staging directory — never to game dir).
        Report(progress, "Extracting archive", 10);
        var staged = await _zipService.StageAsync(
            zipPath,
            new Progress<int>(p => Report(progress, "Extracting archive", 10 + p / 10)),
            ct).ConfigureAwait(false);

        try
        {
            // 3. Build data-file index for filename-index fallback.
            Report(progress, "Indexing game files", 25);
            var dataFileIndex = BuildDataFileIndex(dataRoot);

            // 4. Resolve the mapping plan (pure — no disk writes).
            Report(progress, "Resolving file mappings", 30);
            var plan = _mappingResolver.Resolve(
                staged.Entries, dataRoot, dataFileIndex, staged.ModInfo);

            // 5. Delegate ambiguous entries to the UI callback.
            var allMapped = plan.Mapped.ToList();
            if (plan.Ambiguous.Count > 0)
            {
                var resolved = await confirmAmbiguous(plan.Ambiguous).ConfigureAwait(false);
                foreach (var r in resolved)
                {
                    if (r.Skip || r.ChosenRelativeTargetPath is null)
                    {
                        warnings.Add($"Skipped ambiguous file: {r.ZipEntry.FileName}");
                        continue;
                    }
                    allMapped.Add(new FileMappingResult
                    {
                        ZipEntry           = r.ZipEntry,
                        RelativeTargetPath = r.ChosenRelativeTargetPath,
                        MappingMethod      = MappingMethod.FilenameMatch
                    });
                }
            }

            // Surface collisions to the caller — none are auto-installed.
            if (plan.Collisions.Count > 0)
            {
                foreach (var collision in plan.Collisions)
                    warnings.Add(
                        $"Path collision: {collision.Entries.Count} zip files resolve to " +
                        $"'{collision.RelativeTargetPath}' — none installed. User resolution required.");
            }

            if (plan.Unmatched.Count > 0)
                warnings.Add($"{plan.Unmatched.Count} file(s) had no match in the data directory and were skipped.");

            if (allMapped.Count == 0)
                return InstallResult.Failure("No files could be mapped to the game data directory.");

            // 6. Generate a stable mod ID for this installation.
            string modId = Guid.NewGuid().ToString("N");

            // 7. Backup originals before touching anything in the game directory.
            Report(progress, "Backing up originals", 40);
            await _backupService.BackupFilesAsync(
                modId, dataRoot, allMapped.Select(m => m.RelativeTargetPath), ct)
                .ConfigureAwait(false);

            // 8. Copy mod files from staging to the game data root.
            //    NOTE: Writing to C:\Program Files requires admin elevation at runtime.
            Report(progress, "Installing files", 55);
            var installedFiles = new List<InstalledFileEntry>();

            foreach (var mapping in allMapped)
            {
                ct.ThrowIfCancellationRequested();

                string destPath = Path.Combine(
                    dataRoot,
                    mapping.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

                bool    isNew        = !_fs.FileExists(destPath);
                string? originalHash = isNew ? null : _hasher.ComputeHash(destPath);

                // NOTE: Writes to C:\Program Files require admin elevation at runtime.
                _fs.CopyFile(mapping.ZipEntry.StagedFilePath, destPath, overwrite: true);

                string installedHash = _hasher.ComputeHash(destPath);

                installedFiles.Add(new InstalledFileEntry
                {
                    RelativeTargetPath = mapping.RelativeTargetPath,
                    SourcePathInZip    = mapping.ZipEntry.FullNameInZip,
                    MappingMethod      = mapping.MappingMethod,
                    OriginalFileHash   = originalHash,
                    InstalledFileHash  = installedHash,
                    IsNewFile          = isNew
                });

                Report(progress, "Installing files", 55 + installedFiles.Count * 20 / allMapped.Count);
            }

            // 9. Cache mod payload so reapply-after-update works without the original zip.
            Report(progress, "Caching mod payload", 80);
            await CachePayloadAsync(modId, allMapped, ct).ConfigureAwait(false);

            // 10. Detect conflicts and record in manifest.
            Report(progress, "Updating manifest", 90);
            var modEntry = new ModEntry
            {
                ModId            = modId,
                ModName          = modName,
                SourceZipHash    = staged.ZipHash,
                InstallTimestamp = _clock.GetUtcNow(),
                Files            = installedFiles
            };

            var conflicts = await _manifestStore.DetectConflictsAsync(modEntry, ct).ConfigureAwait(false);
            foreach (var (existingId, _, path) in conflicts)
                warnings.Add($"File '{path}' is also owned by mod '{existingId}' — last-write wins.");

            await _manifestStore.AddOrUpdateModAsync(modEntry, ct).ConfigureAwait(false);

            Report(progress, "Complete", 100);
            return new InstallResult
            {
                Success        = true,
                ModId          = modId,
                Warnings       = warnings,
                FilesInstalled = installedFiles.Count
            };
        }
        finally
        {
            // Always clean up staging, even on failure.
            _zipService.CleanupStaging(staged.StagingDirectory);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Uninstall
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<UninstallResult> UninstallAsync(
        string modId,
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = await _manifestStore.LoadAsync(ct).ConfigureAwait(false);
        if (!manifest.Mods.TryGetValue(modId, out var modEntry))
            return UninstallResult.Failure($"Mod '{modId}' is not installed.");

        // Restore backed-up originals.
        // NOTE: Writes to C:\Program Files require admin elevation at runtime.
        Report(progress, "Restoring original files", 20);
        await _backupService.RestoreAsync(modId, dataRoot, ct).ConfigureAwait(false);

        // Delete any files that were brand-new (no backup exists for them).
        Report(progress, "Removing new files", 60);
        await Task.Run(() =>
        {
            foreach (var file in modEntry.Files.Where(f => f.IsNewFile))
            {
                ct.ThrowIfCancellationRequested();
                string path = Path.Combine(
                    dataRoot,
                    file.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
                if (_fs.FileExists(path))
                    _fs.DeleteFile(path);
            }
        }, ct).ConfigureAwait(false);

        Report(progress, "Cleaning up", 80);
        await _backupService.PruneAsync(modId, ct).ConfigureAwait(false);
        await _manifestStore.RemoveModAsync(modId, ct).ConfigureAwait(false);

        Report(progress, "Complete", 100);
        return new UninstallResult
        {
            Success       = true,
            FilesRestored = modEntry.Files.Count(f => !f.IsNewFile)
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Revert detection and reapply
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ModUpdateStatus>> CheckForRevertedModsAsync(
        string dataRoot,
        CancellationToken ct = default)
    {
        var manifest = await _manifestStore.LoadAsync(ct).ConfigureAwait(false);
        var statuses = new List<ModUpdateStatus>();

        foreach (var mod in manifest.Mods.Values)
        {
            ct.ThrowIfCancellationRequested();

            string payloadDir = AppPaths.PayloadDirForMod(mod.ModId);
            if (!_fs.DirectoryExists(payloadDir))
            {
                statuses.Add(new ModUpdateStatus
                {
                    ModId             = mod.ModId,
                    ModName           = mod.ModName,
                    State             = ModRevertState.PayloadMissing,
                    RevertedFileCount = 0
                });
                continue;
            }

            int reverted = 0;
            foreach (var file in mod.Files)
            {
                ct.ThrowIfCancellationRequested();
                string onDisk = Path.Combine(
                    dataRoot,
                    file.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

                if (!_fs.FileExists(onDisk))
                {
                    reverted++;
                    continue;
                }

                string hash = _hasher.ComputeHash(onDisk);
                if (!string.Equals(hash, file.InstalledFileHash, StringComparison.OrdinalIgnoreCase))
                    reverted++;
            }

            statuses.Add(new ModUpdateStatus
            {
                ModId             = mod.ModId,
                ModName           = mod.ModName,
                State             = reverted > 0 ? ModRevertState.Reverted : ModRevertState.Intact,
                RevertedFileCount = reverted
            });
        }

        return statuses;
    }

    public async Task<ReapplyResult> ReapplyRevertedModsAsync(
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var statuses = await CheckForRevertedModsAsync(dataRoot, ct).ConfigureAwait(false);
        var manifest = await _manifestStore.LoadAsync(ct).ConfigureAwait(false);

        int  modsReapplied  = 0;
        int  filesReapplied = 0;
        var  errors         = new List<string>();

        foreach (var status in statuses.Where(s => s.State == ModRevertState.Reverted))
        {
            ct.ThrowIfCancellationRequested();
            if (!manifest.Mods.TryGetValue(status.ModId, out var modEntry)) continue;

            string payloadDir = AppPaths.PayloadDirForMod(status.ModId);
            Report(progress, $"Reapplying {status.ModName}", 0);

            bool anyReapplied = false;

            foreach (var file in modEntry.Files)
            {
                ct.ThrowIfCancellationRequested();

                string onDiskPath = Path.Combine(
                    dataRoot,
                    file.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

                // Skip files that are already correct.
                if (_fs.FileExists(onDiskPath))
                {
                    string current = _hasher.ComputeHash(onDiskPath);
                    if (string.Equals(current, file.InstalledFileHash, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                string payloadFile = Path.Combine(
                    payloadDir,
                    file.SourcePathInZip.Replace('/', Path.DirectorySeparatorChar));

                if (!_fs.FileExists(payloadFile))
                {
                    errors.Add($"Payload missing for '{file.RelativeTargetPath}' (mod: {modEntry.ModName})");
                    continue;
                }

                try
                {
                    // NOTE: Writes to C:\Program Files require admin elevation at runtime.
                    _fs.CopyFile(payloadFile, onDiskPath, overwrite: true);
                    filesReapplied++;
                    anyReapplied = true;
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to reapply '{file.RelativeTargetPath}': {ex.Message}");
                }
            }

            if (anyReapplied) modsReapplied++;
        }

        return new ReapplyResult
        {
            Success        = errors.Count == 0,
            ModsReapplied  = modsReapplied,
            FilesReapplied = filesReapplied,
            Errors         = errors
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private IReadOnlyList<string> BuildDataFileIndex(string dataRoot)
    {
        if (!_fs.DirectoryExists(dataRoot)) return [];
        return _fs.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(dataRoot, f).Replace('\\', '/'))
            .ToList();
    }

    private async Task CachePayloadAsync(
        string modId,
        IReadOnlyList<FileMappingResult> mappings,
        CancellationToken ct)
    {
        string payloadDir = AppPaths.PayloadDirForMod(modId);
        _fs.CreateDirectory(payloadDir);
        await Task.Run(() =>
        {
            foreach (var mapping in mappings)
            {
                ct.ThrowIfCancellationRequested();
                string dest = Path.Combine(
                    payloadDir,
                    mapping.ZipEntry.FullNameInZip.Replace('/', Path.DirectorySeparatorChar));
                _fs.CopyFile(mapping.ZipEntry.StagedFilePath, dest, overwrite: true);
            }
        }, ct).ConfigureAwait(false);
    }

    private static void Report(IProgress<SyncProgress>? progress, string phase, int percent) =>
        progress?.Report(new SyncProgress { Phase = phase, PercentComplete = percent });
}

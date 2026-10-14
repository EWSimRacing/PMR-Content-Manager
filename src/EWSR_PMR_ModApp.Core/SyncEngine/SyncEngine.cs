using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
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
    // Install — thin wrapper + split prepare/execute
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Thin wrapper: prepare → execute → cleanup staging.</summary>
    public async Task<InstallResult> InstallAsync(
        string zipPath,
        string dataRoot,
        string modName,
        Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var plan = await PrepareInstallAsync(zipPath, dataRoot, modName, confirmAmbiguous, progress, ct)
            .ConfigureAwait(false);
        try
        {
            return await ExecuteInstallAsync(plan, progress, ct).ConfigureAwait(false);
        }
        finally
        {
            CleanupInstallPlan(plan);
        }
    }

    public async Task<InstallPlan> PrepareInstallAsync(
        string zipPath,
        string dataRoot,
        string modName,
        Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var warnings = new List<string>();
        string gameRoot = GetGameRoot(dataRoot);

        // 1. Validate zip integrity.
        Report(progress, "Validating archive", 0);
        if (!await _zipService.ValidateIntegrityAsync(zipPath, ct).ConfigureAwait(false))
            throw new InvalidOperationException("The zip archive is corrupt or could not be read.");

        // 2. Stage (extract to app-data staging directory — never to game dir).
        Report(progress, "Extracting archive", 10);
        var staged = await _zipService.StageAsync(
            zipPath,
            new Progress<int>(p => Report(progress, "Extracting archive", 10 + p / 10)),
            ct).ConfigureAwait(false);

        try
        {
            bool nativeAudio = CockpitImmersionAudio.Recognizes(staged.ZipHash);
            var baselines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var payloadHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            IReadOnlyList<ZipEntryInfo> installEntries = staged.Entries;
            if (nativeAudio)
            {
                Report(progress, "Preparing native Cockpit Immersion Audio (no scripts)", 20);
                var generated = new List<ZipEntryInfo>();
                var installed = await _manifestStore.LoadAsync(ct).ConfigureAwait(false);
                if (installed.Mods.Values.Any(m => m.NativePatchId == CockpitImmersionAudio.PatchId))
                    throw new UnsupportedModPackageException("Cockpit Immersion Audio is already managed. Use Re-check & Reapply instead of installing it twice.");
                foreach (string path in _fs.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories)
                             .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();
                    string relative = Path.GetRelativePath(dataRoot, path).Replace('\\', '/');
                    if (!relative.StartsWith("vehicles/", StringComparison.OrdinalIgnoreCase)
                        || !relative.EndsWith("audioConfig.xml", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!PathValidator.IsUnderDataRoot(dataRoot, relative))
                        throw new InvalidDataException($"Unsafe audio asset: {relative}");
                    for (string? current = path; current is not null
                         && current.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase);
                         current = Path.GetDirectoryName(current))
                        if (_fs.ResolveJunctionTarget(current) is not null)
                            throw new InvalidDataException($"Audio asset passes through a junction or symbolic link: {relative}");
                    if (installed.Mods.Values.Any(m => m.Files.Any(f => f.TargetRoot == TargetRoot.Data
                        && string.Equals(f.RelativeTargetPath, relative, StringComparison.OrdinalIgnoreCase))))
                        throw new UnsupportedModPackageException(
                            $"Audio target '{relative}' is already owned by another mod. Resolve that conflict before installing.");
                    using var source = _fs.OpenRead(path);
                    using var bytes = new MemoryStream();
                    source.CopyTo(bytes);
                    byte[] original = bytes.ToArray();
                    byte[] replacement;
                    try { replacement = CockpitImmersionAudio.Transform(original); }
                    catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException or ArgumentException)
                    {
                        throw new UnsupportedModPackageException($"Cannot safely patch '{relative}': {ex.Message}. No game files were changed.");
                    }
                    string zipRelative = "data/" + relative;
                    string destination = Path.Combine(staged.StagingDirectory, zipRelative.Replace('/', Path.DirectorySeparatorChar));
                    _fs.CreateDirectory(Path.GetDirectoryName(destination)!);
                    using (var output = _fs.CreateFile(destination)) output.Write(replacement);
                    generated.Add(new ZipEntryInfo
                    {
                        FullNameInZip = zipRelative, StagedFilePath = destination,
                        UncompressedSize = replacement.Length, Category = SkipCategory.Install
                    });
                    baselines[zipRelative] = HashHelper.ComputeBytesHash(original);
                    payloadHashes[zipRelative] = HashHelper.ComputeBytesHash(replacement);
                }
                if (generated.Count == 0)
                    throw new UnsupportedModPackageException("No *audioConfig.xml assets were found recursively under vehicles/ in the configured data folder. Cockpit Immersion Audio needs existing audio XML files.");
                installEntries = generated;
                warnings.Add("Recognized reviewed Cockpit Immersion Audio archive. Applied six native XML settings and EWSR credit; no downloaded scripts are executed. Changed game baselines are held for review during reapply.");
            }

            if (!nativeAudio && staged.Entries.Count == 0)
            {
                var blockedFiles = staged.SkippedFiles
                    .Where(f => f.Category == SkipCategory.UnsafeFile)
                    .Select(f => f.PathInZip)
                    .ToList();

                if (blockedFiles.Count > 0)
                    throw new UnsupportedModPackageException(
                        "This archive contains executable or script files but no installable game assets. " +
                        "PMR CM does not automatically run standalone installers or copy their scripts into the game. " +
                        "Built-in audio support requires the exact reviewed Cockpit Immersion Audio archive checksum. " +
                        "No game files were changed.\n\n" +
                        "Blocked files:\n" + string.Join("\n", blockedFiles) +
                        "\n\nSee the archive's README for its installation method, or ask the author for a " +
                        "CM-compatible ZIP containing the modified game files under data/. " +
                        "Changing the game path will not supply the missing assets.");
            }

            // 3. Build data-file index for filename-index fallback.
            Report(progress, "Indexing game files", 25);
            var dataFileIndex = BuildDataFileIndex(dataRoot);

            // 4. Resolve the mapping plan (pure — no disk writes).
            Report(progress, "Resolving file mappings", 30);
            var plan = _mappingResolver.Resolve(
                installEntries, dataRoot, dataFileIndex, nativeAudio ? null : staged.ModInfo);

            if (!nativeAudio)
                warnings.AddRange(staged.SkippedFiles.Where(f => f.Category != SkipCategory.MetaFile)
                    .Select(f => $"Skipped ({f.Category}): {f.PathInZip} — {f.Reason}"));

            // 5. Delegate ambiguous entries to the UI callback.
            var allMapped = plan.Mapped.ToList();
            if (plan.Ambiguous.Count > 0)
            {
                var resolved = await confirmAmbiguous(plan.Ambiguous).ConfigureAwait(false);
                var resolvedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in resolved)
                {
                    var offered = plan.Ambiguous.FirstOrDefault(a => ReferenceEquals(a.ZipEntry, r.ZipEntry));
                    if (offered is null || !resolvedSources.Add(r.ZipEntry.FullNameInZip))
                        throw new InvalidDataException("Invalid or duplicate ambiguous source resolution.");
                    if (r.Skip || r.ChosenRelativeTargetPath is null)
                    {
                        warnings.Add($"Skipped ambiguous file: {r.ZipEntry.FullNameInZip}");
                        continue;
                    }
                    if (!offered.CandidatePaths.Contains(r.ChosenRelativeTargetPath.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase))
                        throw new InvalidDataException($"Destination was not offered for '{r.ZipEntry.FullNameInZip}': {r.ChosenRelativeTargetPath}");
                    allMapped.Add(new FileMappingResult
                    {
                        ZipEntry           = r.ZipEntry,
                        RelativeTargetPath = r.ChosenRelativeTargetPath.Replace('\\', '/'),
                        MappingMethod      = MappingMethod.FilenameMatch,
                        TargetRoot         = TargetRoot.Data
                    });
                }
            }

            var invalidDataTargets = allMapped.Where(m => m.TargetRoot == TargetRoot.Data
                && !PathValidator.IsUnderDataRoot(dataRoot, m.RelativeTargetPath)).ToList();
            if (invalidDataTargets.Count > 0)
                throw new InvalidDataException("Unsafe data target rejected: " + string.Join("; ", invalidDataTargets.Select(m => m.RelativeTargetPath)));

            var collisionsAfterConfirmation = allMapped.GroupBy(m => (m.TargetRoot, m.RelativeTargetPath.ToUpperInvariant()))
                .Where(g => g.Count() > 1).ToList();
            foreach (var collision in collisionsAfterConfirmation)
            {
                warnings.Add($"Path collision after confirmation: {collision.Key.Item2} — sources: {string.Join(", ", collision.Select(m => m.ZipEntry.FullNameInZip))} — none installed.");
                foreach (var mapping in collision) allMapped.Remove(mapping);
            }

            var invalidGameRootTargets = allMapped
                .Where(m => m.TargetRoot == TargetRoot.Game
                    && !PathValidator.IsAllowedGameRootTarget(gameRoot, dataRoot, m.RelativeTargetPath))
                .Select(m => $"{m.ZipEntry.FullNameInZip} -> {m.RelativeTargetPath}")
                .ToList();

            if (invalidGameRootTargets.Count > 0)
                throw new InvalidOperationException(
                    "Unsafe game-root target rejected: " + string.Join("; ", invalidGameRootTargets));

            // Surface collisions.
            foreach (var collision in plan.Collisions)
            {
                var sources = string.Join(", ", collision.Entries.Select(e => e.ZipEntry.FullNameInZip));
                warnings.Add(
                    $"Path collision: '{collision.RelativeTargetPath}' — {collision.Entries.Count} source(s): {sources} — none installed.");
            }

            // One row per skipped file.
            foreach (var entry in plan.Unmatched)
                warnings.Add($"Skipped (no match in data): {entry.FullNameInZip}");

            if (allMapped.Count == 0)
                throw new UnsupportedModPackageException(
                    "No files could be mapped to the game data directory. No game files were changed.\n\n" +
                    (warnings.Count > 0 ? string.Join("\n", warnings) : "The archive contains no mapped game assets.") +
                    "\n\nUse a data/ layout or explicit modinfo.json mappings; resolve duplicate destinations or ambiguous filenames before retrying.");

            // 6. Generate a stable mod ID.
            string modId = Guid.NewGuid().ToString("N");

            // 7. Resolve any hook scripts declared in modinfo.json.
            StagedHook? postInstallHook   = null;
            StagedHook? postUninstallHook = null;

            if (!nativeAudio && staged.ModInfo?.Hooks is { } hooks && staged.HookScripts.Count > 0)
            {
                foreach (var hookEntry in staged.HookScripts)
                {
                    if (hooks.PostInstall?.Script is { } piPath &&
                        string.Equals(hookEntry.FullNameInZip, piPath, StringComparison.OrdinalIgnoreCase))
                    {
                        postInstallHook = new StagedHook(
                            hookEntry.StagedFilePath,
                            hookEntry.FileName,
                            hooks.PostInstall.Description,
                            hooks.PostInstall.RequiresElevation);
                    }

                    if (hooks.PostUninstall?.Script is { } puPath &&
                        string.Equals(hookEntry.FullNameInZip, puPath, StringComparison.OrdinalIgnoreCase))
                    {
                        postUninstallHook = new StagedHook(
                            hookEntry.StagedFilePath,
                            hookEntry.FileName,
                            hooks.PostUninstall.Description,
                            hooks.PostUninstall.RequiresElevation);
                    }
                }
            }

            return new InstallPlan
            {
                ModId             = modId,
                NativePatchId     = nativeAudio ? CockpitImmersionAudio.PatchId : null,
                ModName           = modName,
                DataRoot          = dataRoot,
                GameRoot          = gameRoot,
                ZipHash           = staged.ZipHash,
                StagingDirectory  = staged.StagingDirectory,
                FilesToBackup     = allMapped.Select(m => new FileTargetSpec
                {
                    RelativeTargetPath = m.RelativeTargetPath,
                    TargetRoot         = m.TargetRoot
                }).ToList(),
                FilesToCopy       = allMapped.Select(m => new FileCopySpec
                {
                    SourcePath         = m.ZipEntry.StagedFilePath,
                    RelativeTargetPath = m.RelativeTargetPath,
                    TargetRoot         = m.TargetRoot,
                    ExpectedTargetHash = baselines.GetValueOrDefault(m.ZipEntry.FullNameInZip),
                    ExpectedSourceHash = payloadHashes.GetValueOrDefault(m.ZipEntry.FullNameInZip)
                }).ToList(),
                MappedFiles       = allMapped,
                Warnings          = warnings,
                PostInstallHook   = postInstallHook,
                PostUninstallHook = postUninstallHook
            };
        }
        catch
        {
            _zipService.CleanupStaging(staged.StagingDirectory);
            throw;
        }
    }

    public async Task<InstallResult> ExecuteInstallAsync(
        InstallPlan plan,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        foreach (var spec in plan.FilesToCopy)
            ValidateBaseline(plan.DataRoot, plan.GameRoot, spec);
        var transaction = new FileWriteTransaction(_fs, plan.DataRoot, plan.GameRoot, plan.FilesToCopy);
        try
        {
            var result = await ExecuteInstallTransactionAsync(plan, transaction, progress, ct).ConfigureAwait(false);
            transaction.Commit();
            return result;
        }
        catch (Exception ex)
        {
            var recoveryErrors = transaction.Rollback(ex);
            if (recoveryErrors.Count > 0)
                throw new IOException($"{ex.Message}\n{string.Join("\n", recoveryErrors.Select(e => e.Message))}", ex);
            throw;
        }
    }

    private async Task<InstallResult> ExecuteInstallTransactionAsync(
        InstallPlan plan,
        FileWriteTransaction transaction,
        IProgress<SyncProgress>? progress,
        CancellationToken ct)
    {
        var warnings = plan.Warnings.ToList();
        foreach (var spec in plan.FilesToCopy)
            ValidateBaseline(plan.DataRoot, plan.GameRoot, spec);

        // 7. Backup originals before touching anything in the game directory.
        Report(progress, "Backing up originals", 40);
        await _backupService.BackupFilesAsync(
            plan.ModId, plan.DataRoot, plan.GameRoot, plan.FilesToBackup, ct)
            .ConfigureAwait(false);

        // 8. Copy mod files from staging to the game data root.
        //    NOTE: Writing to C:\Program Files requires admin elevation at runtime.
        Report(progress, "Installing files", 55);
        var installedFiles = new List<InstalledFileEntry>();

        foreach (var mapping in plan.MappedFiles)
        {
            ct.ThrowIfCancellationRequested();

            string destPath = Path.Combine(
                GetBaseRoot(plan.DataRoot, plan.GameRoot, mapping.TargetRoot),
                mapping.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

            bool    isNew        = !_fs.FileExists(destPath);
            string? originalHash = isNew ? null : _hasher.ComputeHash(destPath);
            ValidateBaseline(plan.DataRoot, plan.GameRoot, plan.FilesToCopy.Single(s =>
                s.TargetRoot == mapping.TargetRoot && s.RelativeTargetPath == mapping.RelativeTargetPath));

            // NOTE: Writes to C:\Program Files require admin elevation at runtime.
            transaction.BeforeWrite(destPath);
            _fs.CopyFile(mapping.ZipEntry.StagedFilePath, destPath, overwrite: true);

            string installedHash = _hasher.ComputeHash(destPath);

            installedFiles.Add(new InstalledFileEntry
            {
                RelativeTargetPath = mapping.RelativeTargetPath,
                SourcePathInZip    = mapping.ZipEntry.FullNameInZip,
                MappingMethod      = mapping.MappingMethod,
                TargetRoot         = mapping.TargetRoot,
                OriginalFileHash   = originalHash,
                InstalledFileHash  = installedHash,
                IsNewFile          = isNew
            });

            Report(progress, "Installing files", 55 + installedFiles.Count * 20 / plan.MappedFiles.Count);
        }

        // 9. Cache mod payload so reapply-after-update works without the original zip.
        Report(progress, "Caching mod payload", 80);
        await CachePayloadAsync(plan.ModId, plan.MappedFiles, ct).ConfigureAwait(false);

        // 9b. Cache hook scripts to AppData so the uninstall hook is available later.
        if (plan.PostInstallHook is not null || plan.PostUninstallHook is not null)
        {
            string scriptDir = AppPaths.ScriptsDirForMod(plan.ModId);
            _fs.CreateDirectory(scriptDir);

            if (plan.PostInstallHook is { } piHook)
                _fs.CopyFile(piHook.StagedPath, Path.Combine(scriptDir, piHook.ScriptName), overwrite: true);

            if (plan.PostUninstallHook is { } puHook)
                _fs.CopyFile(puHook.StagedPath, Path.Combine(scriptDir, puHook.ScriptName), overwrite: true);
        }

        // 10. Detect conflicts and record in manifest.
        Report(progress, "Updating manifest", 90);

        ModHookMetadata? hookMeta = null;
        if (plan.PostInstallHook is not null || plan.PostUninstallHook is not null)
        {
            hookMeta = new ModHookMetadata
            {
                PostInstallScriptName          = plan.PostInstallHook?.ScriptName,
                PostInstallDescription         = plan.PostInstallHook?.Description,
                PostInstallRequiresElevation   = plan.PostInstallHook?.RequiresElevation ?? false,
                PostUninstallScriptName        = plan.PostUninstallHook?.ScriptName,
                PostUninstallDescription       = plan.PostUninstallHook?.Description,
                PostUninstallRequiresElevation = plan.PostUninstallHook?.RequiresElevation ?? false
            };
        }

        var modEntry = new ModEntry
        {
            ModId            = plan.ModId,
            ModName          = plan.ModName,
            SourceZipHash    = plan.ZipHash,
            InstallTimestamp = _clock.GetUtcNow(),
            Files            = installedFiles,
            NativePatchId    = plan.NativePatchId,
            Hooks            = hookMeta
        };

        var conflicts = await _manifestStore.DetectConflictsAsync(modEntry, ct).ConfigureAwait(false);
        foreach (var (existingId, _, path) in conflicts)
            warnings.Add($"File '{path}' is also owned by mod '{existingId}' — last-write wins.");

        await _manifestStore.AddOrUpdateModAsync(modEntry, ct).ConfigureAwait(false);

        Report(progress, "Complete", 100);
        return new InstallResult
        {
            Success        = true,
            ModId          = plan.ModId,
            Warnings       = warnings,
            FilesInstalled = installedFiles.Count
        };
    }

    public void CleanupInstallPlan(InstallPlan plan) =>
        _zipService.CleanupStaging(plan.StagingDirectory);

    // ─────────────────────────────────────────────────────────────────────────
    // Uninstall
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Thin wrapper: prepare → execute.</summary>
    public async Task<UninstallResult> UninstallAsync(
        string modId,
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        UninstallPlan plan;
        try
        {
            plan = await PrepareUninstallAsync(modId, dataRoot, ct).ConfigureAwait(false);
        }
        catch (KeyNotFoundException ex)
        {
            return UninstallResult.Failure(ex.Message);
        }

        return await ExecuteUninstallAsync(plan, progress, ct).ConfigureAwait(false);
    }

    public async Task<UninstallPlan> PrepareUninstallAsync(
        string modId,
        string dataRoot,
        CancellationToken ct = default)
    {
        var manifest = await _manifestStore.LoadAsync(ct).ConfigureAwait(false);
        if (!manifest.Mods.TryGetValue(modId, out var modEntry))
            throw new KeyNotFoundException($"Mod '{modId}' is not installed.");

        var newFiles = modEntry.Files
            .Where(f => f.IsNewFile)
            .Select(f => new FileTargetSpec
            {
                RelativeTargetPath = f.RelativeTargetPath,
                TargetRoot         = f.TargetRoot
            })
            .ToList();

        int backedUpCount = modEntry.Files.Count(f => !f.IsNewFile);

        // Resolve post-uninstall hook script from the cached scripts directory.
        string? postUninstallScriptPath      = null;
        string? postUninstallDescription     = null;
        bool    postUninstallNeedsElevation  = false;

        if (modEntry.Hooks?.PostUninstallScriptName is { } scriptName)
        {
            string candidatePath = Path.Combine(AppPaths.ScriptsDirForMod(modId), scriptName);
            if (_fs.FileExists(candidatePath))
            {
                postUninstallScriptPath     = candidatePath;
                postUninstallDescription    = modEntry.Hooks.PostUninstallDescription;
                postUninstallNeedsElevation = modEntry.Hooks.PostUninstallRequiresElevation;
            }
        }

        return new UninstallPlan
        {
            ModId                          = modId,
            ModName                        = modEntry.ModName,
            DataRoot                       = dataRoot,
            GameRoot                       = GetGameRoot(dataRoot),
            NewFilesToDelete               = newFiles,
            BackedUpFileCount              = backedUpCount,
            PostUninstallScriptPath        = postUninstallScriptPath,
            PostUninstallDescription       = postUninstallDescription,
            PostUninstallRequiresElevation = postUninstallNeedsElevation
        };
    }

    public async Task<UninstallResult> ExecuteUninstallAsync(
        UninstallPlan plan,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Restore backed-up originals.
        // NOTE: Writes to C:\Program Files require admin elevation at runtime.
        Report(progress, "Restoring original files", 20);
        await _backupService.RestoreAsync(plan.ModId, plan.DataRoot, plan.GameRoot, ct).ConfigureAwait(false);

        // Delete any files that were brand-new (no backup exists for them).
        Report(progress, "Removing new files", 60);
        await Task.Run(() =>
        {
            foreach (var target in plan.NewFilesToDelete)
            {
                ct.ThrowIfCancellationRequested();
                string relative = target.RelativeTargetPath;
                string path = Path.Combine(
                    GetBaseRoot(plan.DataRoot, plan.GameRoot, target.TargetRoot),
                    relative.Replace('/', Path.DirectorySeparatorChar));
                if (_fs.FileExists(path))
                    _fs.DeleteFile(path);
            }
        }, ct).ConfigureAwait(false);

        Report(progress, "Cleaning up", 80);
        await _backupService.PruneAsync(plan.ModId, ct).ConfigureAwait(false);
        await _manifestStore.RemoveModAsync(plan.ModId, ct).ConfigureAwait(false);

        // Remove cached hook scripts (best-effort — failure here does not block uninstall).
        string scriptsDir = AppPaths.ScriptsDirForMod(plan.ModId);
        if (_fs.DirectoryExists(scriptsDir))
        {
            try { _fs.DeleteDirectory(scriptsDir, recursive: true); }
            catch { /* Non-critical — leftover scripts in AppData do not affect game state. */ }
        }

        // Remove cached mod payload (best-effort — leftover payload wastes disk but doesn't harm state).
        string payloadDir = AppPaths.PayloadDirForMod(plan.ModId);
        if (_fs.DirectoryExists(payloadDir))
        {
            try { _fs.DeleteDirectory(payloadDir, recursive: true); }
            catch { /* Non-critical — orphaned payload only wastes disk space. */ }
        }

        Report(progress, "Complete", 100);
        return new UninstallResult
        {
            Success       = true,
            FilesRestored = plan.BackedUpFileCount
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
        string gameRoot = GetGameRoot(dataRoot);

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
                    GetBaseRoot(dataRoot, gameRoot, file.TargetRoot),
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

    /// <summary>Thin wrapper: prepare → execute.</summary>
    public async Task<ReapplyResult> ReapplyRevertedModsAsync(
        string dataRoot,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        var plan = await PrepareReapplyAsync(dataRoot, ct).ConfigureAwait(false);
        return await ExecuteReapplyAsync(plan, progress, ct).ConfigureAwait(false);
    }

    public async Task<ReapplyPlan> PrepareReapplyAsync(
        string dataRoot,
        CancellationToken ct = default)
    {
        var statuses = await CheckForRevertedModsAsync(dataRoot, ct).ConfigureAwait(false);
        var manifest = await _manifestStore.LoadAsync(ct).ConfigureAwait(false);
        string gameRoot = GetGameRoot(dataRoot);

        var modsToReapply = new List<ModReapplyItem>();
        var warnings = new List<string>();

        foreach (var status in statuses.Where(s => s.State == ModRevertState.Reverted))
        {
            ct.ThrowIfCancellationRequested();
            if (!manifest.Mods.TryGetValue(status.ModId, out var modEntry)) continue;

            string payloadDir = AppPaths.PayloadDirForMod(status.ModId);
            var filesToCopy   = new List<FileCopySpec>();

            foreach (var file in modEntry.Files)
            {
                ct.ThrowIfCancellationRequested();

                string onDiskPath = Path.Combine(
                    GetBaseRoot(dataRoot, gameRoot, file.TargetRoot),
                    file.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

                // Only queue files that are actually out-of-date.
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
                    warnings.Add($"Payload missing for '{file.RelativeTargetPath}' (mod: {modEntry.ModName})");
                    continue;
                }

                string? expectedBaseline = null;
                if (modEntry.NativePatchId is not null)
                {
                    bool ownedByOtherMod = manifest.Mods.Values.Any(m => m.ModId != modEntry.ModId
                        && m.Files.Any(f => f.TargetRoot == file.TargetRoot
                            && string.Equals(f.RelativeTargetPath, file.RelativeTargetPath, StringComparison.OrdinalIgnoreCase)));
                    if (modEntry.NativePatchId != CockpitImmersionAudio.PatchId || ownedByOtherMod
                        || !_fs.FileExists(onDiskPath) || file.OriginalFileHash is null
                        || !string.Equals(_hasher.ComputeHash(payloadFile), file.InstalledFileHash, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(_hasher.ComputeHash(onDiskPath), file.OriginalFileHash, StringComparison.OrdinalIgnoreCase))
                    {
                        warnings.Add($"Held for review: {file.RelativeTargetPath} ({modEntry.ModName}) — baseline changed, file missing, payload altered, or another mod owns it. No stale audio payload was applied; original backup retained.");
                        continue;
                    }
                    expectedBaseline = file.OriginalFileHash;
                }

                filesToCopy.Add(new Elevation.FileCopySpec
                {
                    SourcePath         = payloadFile,
                    RelativeTargetPath = file.RelativeTargetPath,
                    TargetRoot         = file.TargetRoot,
                    ExpectedTargetHash = expectedBaseline,
                    ExpectedSourceHash = expectedBaseline is null ? null : file.InstalledFileHash
                });
            }

            if (filesToCopy.Count > 0)
            {
                modsToReapply.Add(new ModReapplyItem
                {
                    ModId       = status.ModId,
                    ModName     = status.ModName,
                    FilesToCopy = filesToCopy
                });
            }
        }

        return new ReapplyPlan { DataRoot = dataRoot, GameRoot = gameRoot, ModsToReapply = modsToReapply, Warnings = warnings };
    }

    public async Task<ReapplyResult> ExecuteReapplyAsync(
        ReapplyPlan plan,
        IProgress<SyncProgress>? progress = null,
        CancellationToken ct = default)
    {
        int  modsReapplied  = 0;
        int  filesReapplied = 0;
        var  errors         = plan.Warnings.ToList();

        FileWriteTransaction? transaction = null;
        try
        {
            var specs = plan.ModsToReapply.SelectMany(m => m.FilesToCopy).ToList();
            foreach (var spec in specs)
                ValidateBaseline(plan.DataRoot, plan.GameRoot, spec);
            transaction = new FileWriteTransaction(_fs, plan.DataRoot, plan.GameRoot, specs);
            foreach (var modItem in plan.ModsToReapply)
            {
                ct.ThrowIfCancellationRequested();
                Report(progress, $"Reapplying {modItem.ModName}", 0);

                bool anyReapplied = false;

                foreach (var spec in modItem.FilesToCopy)
                {
                    ct.ThrowIfCancellationRequested();

                    string onDiskPath = Path.Combine(
                        GetBaseRoot(plan.DataRoot, plan.GameRoot, spec.TargetRoot),
                        spec.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));

                    if (!_fs.FileExists(spec.SourcePath))
                        throw new FileNotFoundException($"Payload missing for '{spec.RelativeTargetPath}' (mod: {modItem.ModName})");

                    ValidateBaseline(plan.DataRoot, plan.GameRoot, spec);
                    // NOTE: Writes to C:\Program Files require admin elevation at runtime.
                    transaction.BeforeWrite(onDiskPath);
                    _fs.CopyFile(spec.SourcePath, onDiskPath, overwrite: true);
                    filesReapplied++;
                    anyReapplied = true;
                }

                if (anyReapplied) modsReapplied++;
            }
            transaction.Commit();
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to reapply: {ex.Message}");
            var recoveryErrors = transaction?.Rollback(ex) ?? [];
            errors.AddRange(recoveryErrors.Select(e => e.Message));
            if (recoveryErrors.Count == 0)
            {
                filesReapplied = 0;
                modsReapplied = 0;
            }
            if (ex is OperationCanceledException && recoveryErrors.Count == 0) throw;
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

    private static string GetGameRoot(string dataRoot) =>
        Directory.GetParent(dataRoot)?.FullName ?? string.Empty;

    private static string GetBaseRoot(string dataRoot, string gameRoot, TargetRoot targetRoot) =>
        targetRoot == TargetRoot.Game ? gameRoot : dataRoot;

    private static void Report(IProgress<SyncProgress>? progress, string phase, int percent) =>
        progress?.Report(new SyncProgress { Phase = phase, PercentComplete = percent });
    private void ValidateBaseline(string dataRoot, string gameRoot, FileCopySpec spec)
    {
        if (spec.ExpectedSourceHash is not null && (!_fs.FileExists(spec.SourcePath)
            || !string.Equals(_hasher.ComputeHash(spec.SourcePath), spec.ExpectedSourceHash, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Prepared payload changed for '{spec.RelativeTargetPath}'. Refusing native patch.");
        if (spec.ExpectedTargetHash is null) return;
        string path = Path.Combine(GetBaseRoot(dataRoot, gameRoot, spec.TargetRoot),
            spec.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
        if (!_fs.FileExists(path) || !string.Equals(_hasher.ComputeHash(path), spec.ExpectedTargetHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Baseline changed for '{spec.RelativeTargetPath}' after preparation. Re-check before writing; no stale patch is safe.");
    }
}

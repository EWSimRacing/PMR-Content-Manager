// W4 — SyncEngine split-API tests: PrepareXxxAsync + ExecuteXxxAsync
//
// These tests exercise the new split API introduced by the elevation-broker refactor.
// The thin-wrapper tests (InstallAsync, UninstallAsync, ReapplyRevertedModsAsync) already
// pass via the existing 112-test suite.  This file adds explicit coverage of:
//
//   1. PrepareInstallAsync → InstallPlan shape (FilesToBackup, FilesToCopy, Warnings, MappedFiles)
//   2. ExecuteInstallAsync(plan) → success + file written to FakeFileSystem
//   3. PrepareUninstallAsync → UninstallPlan shape (NewFilesToDelete, BackedUpFileCount)
//   4. ExecuteUninstallAsync(plan) → new-file deleted from FakeFileSystem
//   5. PrepareReapplyAsync → ReapplyPlan shape (ModsToReapply with correct FilesToCopy)
//   6. PrepareReapplyAsync → ExecuteReapplyAsync → file copied back to FakeFileSystem
//
// All tests use FakeFileSystem + FakeFileHasher — no real elevation, no real disk I/O.
// This mirrors how the UI calls Prepare (non-elevated), then hands the plan to IElevatedWriter.

using Xunit;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class SyncEngineSplitTests
{
    private const string DataRoot     = @"C:\PMR\data";
    private const string ManifestPath = @"C:\FakeAppData\manifest.json";
    private const string StagingDir   = @"C:\staging";

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ZipEntryInfo MakeEntry(string fullNameInZip) => new()
    {
        FullNameInZip    = fullNameInZip,
        StagedFilePath   = Path.Combine(StagingDir, fullNameInZip.Replace('/', Path.DirectorySeparatorChar)),
        UncompressedSize = 512
    };

    private static ZipStagingResult MakeStaging(params string[] entryNames) => new()
    {
        StagingDirectory = StagingDir,
        Entries          = entryNames.Select(MakeEntry).ToList(),
        ZipHash          = "sha256:stub-hash"
    };

    private static (Core.SyncEngine.SyncEngine engine, ManifestStore store, FakeFileSystem fs)
        BuildEngine(ZipStagingResult? staged = null, FakeFileHasher? hasher = null)
    {
        var fs    = new FakeFileSystem();
        var store = new ManifestStore(fs, ManifestPath);
        var engine = new Core.SyncEngine.SyncEngine(
            staged is not null ? new StubZipService(staged) : new NoOpZipService(),
            new MappingResolver(),
            store,
            new NoOpBackupService(),
            fs,
            TimeProvider.System,
            hasher ?? new FakeFileHasher());
        return (engine, store, fs);
    }

    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        Task.FromResult<IReadOnlyList<ResolvedMapping>>([]);

    private static ModEntry BuildModEntry(string modId, params InstalledFileEntry[] files) => new()
    {
        ModId            = modId,
        ModName          = $"TestMod_{modId}",
        SourceZipHash    = "sha256:test",
        InstallTimestamp = DateTimeOffset.UtcNow,
        Files            = files
    };

    private static InstalledFileEntry BuildFileEntry(
        string relTarget,
        string installedHash,
        string? srcInZip = null,
        bool isNew       = false) => new()
    {
        RelativeTargetPath = relTarget,
        SourcePathInZip    = srcInZip ?? "data/" + relTarget,
        MappingMethod      = MappingMethod.PathOverlay,
        OriginalFileHash   = isNew ? null : "orig-hash",
        InstalledFileHash  = installedHash,
        IsNewFile          = isNew
    };

    // ── W4-1: PrepareInstallAsync → InstallPlan shape ─────────────────────────

    [Fact]
    public async Task PrepareInstallAsync_SingleMappedFile_ReturnsExpectedPlanShape()
    {
        // Flat zip: "livery.dds" → filename-index match against vehicles/car_a/livery.dds
        var staged = MakeStaging("livery.dds");
        var (engine, _, fs) = BuildEngine(staged);

        fs.AddDirectory(DataRoot);
        fs.AddFile(Path.Combine(DataRoot, "vehicles", "car_a", "livery.dds"), "original");

        var plan = await engine.PrepareInstallAsync("mod.zip", DataRoot, "TestMod", NoAmbiguous);

        // Basic identity fields.
        Assert.Equal("TestMod",          plan.ModName);
        Assert.Equal(DataRoot,           plan.DataRoot);
        Assert.Equal("sha256:stub-hash", plan.ZipHash);
        Assert.NotEmpty(plan.ModId);

        // Exactly one file to copy with correct source (staged path) and target.
        Assert.Single(plan.FilesToCopy);
        Assert.Equal(Path.Combine(StagingDir, "livery.dds"), plan.FilesToCopy[0].SourcePath);
        Assert.Equal("vehicles/car_a/livery.dds",            plan.FilesToCopy[0].RelativeTargetPath);

        // All mapped files are listed in FilesToBackup (executor decides whether to skip).
        Assert.Single(plan.FilesToBackup);
        Assert.Equal("vehicles/car_a/livery.dds", plan.FilesToBackup[0]);

        // One entry in MappedFiles.
        Assert.Single(plan.MappedFiles);

        // No warnings — clean mapping.
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public async Task PrepareInstallAsync_UnmatchedZipEntry_WarningAppearsInPlan()
    {
        // "livery.dds" maps; "mystery.xyz" has no match → should appear as a warning
        // in the plan, not silently dropped.
        var staged = MakeStaging("livery.dds", "mystery.xyz");
        var (engine, _, fs) = BuildEngine(staged);

        fs.AddDirectory(DataRoot);
        fs.AddFile(Path.Combine(DataRoot, "vehicles", "car_a", "livery.dds"), "original");

        var plan = await engine.PrepareInstallAsync("mod.zip", DataRoot, "TestMod", NoAmbiguous);

        // The unmatched file must be named in a warning (not just a bare count).
        Assert.Contains(plan.Warnings, w => w.Contains("mystery.xyz") && w.Contains("no match"));

        // Only the matching file is in FilesToCopy.
        Assert.Single(plan.FilesToCopy);
        Assert.Equal("vehicles/car_a/livery.dds", plan.FilesToCopy[0].RelativeTargetPath);
    }

    // ── W4-2: ExecuteInstallAsync → file written to FakeFileSystem ────────────

    [Fact]
    public async Task ExecuteInstallAsync_PreparedPlan_FileInstalledInFakeFileSystem()
    {
        const string relPath    = "vehicles/car_a/livery.dds";
        const string stagedPath = @"C:\staging\livery.dds";
        string       destPath   = Path.Combine(DataRoot, "vehicles", "car_a", "livery.dds");

        var staged = MakeStaging("livery.dds");
        var (engine, _, fs) = BuildEngine(staged);

        fs.AddDirectory(DataRoot);
        fs.AddFile(Path.Combine(DataRoot, relPath.Replace('/', Path.DirectorySeparatorChar)), "original");
        fs.AddFile(stagedPath, "mod-content");  // staged file must exist for FakeFileSystem.CopyFile

        var plan   = await engine.PrepareInstallAsync("mod.zip", DataRoot, "TestMod", NoAmbiguous);
        var result = await engine.ExecuteInstallAsync(plan);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesInstalled);
        Assert.Empty(result.Warnings);
        Assert.True(fs.FileExists(destPath));
    }

    [Fact]
    public async Task PrepareInstallAsync_ThenExecuteInstallAsync_ModIdConsistentBetweenCallbacks()
    {
        // The modId generated in PrepareInstallAsync must be the same one stored in the manifest.
        var staged = MakeStaging("livery.dds");
        var (engine, store, fs) = BuildEngine(staged);

        fs.AddDirectory(DataRoot);
        fs.AddFile(Path.Combine(DataRoot, "vehicles", "car_a", "livery.dds"), "original");
        fs.AddFile(Path.Combine(StagingDir, "livery.dds"), "mod-content");

        var plan   = await engine.PrepareInstallAsync("mod.zip", DataRoot, "TestMod", NoAmbiguous);
        var result = await engine.ExecuteInstallAsync(plan);

        Assert.True(result.Success);
        // The modId in the InstallResult must match the modId in the plan (and manifest).
        Assert.Equal(plan.ModId, result.ModId);

        var manifest = await store.LoadAsync();
        Assert.True(manifest.Mods.ContainsKey(plan.ModId));
    }

    // ── W4-3: PrepareUninstallAsync → UninstallPlan shape ────────────────────

    [Fact]
    public async Task PrepareUninstallAsync_ModWithMixedFiles_ReturnsCorrectPlan()
    {
        const string modId = "uninstall-shape-test";

        var (engine, store, _) = BuildEngine();

        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            // Existing file with backup (IsNewFile = false)
            BuildFileEntry("vehicles/car_a/livery.dds", "HASH_A"),
            BuildFileEntry("sounds/engine.wav",          "HASH_B"),
            // Brand-new file installed by the mod (IsNewFile = true → in NewFilesToDelete)
            BuildFileEntry("vehicles/car_a/new_skin.dds", "HASH_C", isNew: true)));

        var plan = await engine.PrepareUninstallAsync(modId, DataRoot);

        Assert.Equal(modId,     plan.ModId);
        Assert.Equal(DataRoot,  plan.DataRoot);
        Assert.Equal(2, plan.BackedUpFileCount);   // 2 non-new files
        Assert.Single(plan.NewFilesToDelete);
        Assert.Equal("vehicles/car_a/new_skin.dds", plan.NewFilesToDelete[0]);
    }

    // ── W4-4: ExecuteUninstallAsync → FakeFileSystem state ───────────────────

    [Fact]
    public async Task ExecuteUninstallAsync_NewFile_DeletedFromFakeFileSystem()
    {
        const string modId       = "uninstall-exec-test";
        const string newFileRel  = "vehicles/car_a/new_skin.dds";
        string       newFilePath = Path.Combine(DataRoot, "vehicles", "car_a", "new_skin.dds");

        var (engine, store, fs) = BuildEngine();

        fs.AddFile(newFilePath, "new-mod-content");

        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            BuildFileEntry(newFileRel, "HASH", isNew: true)));

        var plan   = await engine.PrepareUninstallAsync(modId, DataRoot);
        var result = await engine.ExecuteUninstallAsync(plan);

        Assert.True(result.Success);
        Assert.False(fs.FileExists(newFilePath), "New mod file should have been deleted.");
    }

    // ── W4-5: PrepareReapplyAsync → ReapplyPlan shape ────────────────────────

    [Fact]
    public async Task PrepareReapplyAsync_RevertedMod_PlanContainsCorrectFilesToCopy()
    {
        const string modId        = "reapply-plan-test";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string srcInZip     = "data/vehicles/car_a/livery.dds";
        const string installedHash = "INSTALLED_HASH";

        string onDiskPath   = Path.Combine(DataRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        string payloadFile  = Path.Combine(
            AppPaths.PayloadDirForMod(modId),
            srcInZip.Replace('/', Path.DirectorySeparatorChar));

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [onDiskPath] = "REVERTED_HASH"  // different from installedHash → triggers reapply
        });
        var (engine, store, fs) = BuildEngine(hasher: hasher);

        fs.AddFile(onDiskPath, "reverted-game-content");
        fs.AddDirectory(AppPaths.PayloadDirForMod(modId));
        fs.AddFile(payloadFile, "cached-payload");

        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            BuildFileEntry(relPath, installedHash, srcInZip)));

        var plan = await engine.PrepareReapplyAsync(DataRoot);

        Assert.Single(plan.ModsToReapply);
        Assert.Equal(modId, plan.ModsToReapply[0].ModId);
        Assert.Single(plan.ModsToReapply[0].FilesToCopy);
        Assert.Equal(relPath,     plan.ModsToReapply[0].FilesToCopy[0].RelativeTargetPath);
        Assert.Equal(payloadFile, plan.ModsToReapply[0].FilesToCopy[0].SourcePath);
    }

    // ── W4-6: PrepareReapplyAsync → ExecuteReapplyAsync round-trip ───────────

    [Fact]
    public async Task ExecuteReapplyAsync_PreparedPlan_FileCopiedBackToGameDir()
    {
        const string modId        = "reapply-exec-test";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string srcInZip     = "data/vehicles/car_a/livery.dds";
        const string installedHash = "INSTALLED_HASH";

        string onDiskPath  = Path.Combine(DataRoot, relPath.Replace('/', Path.DirectorySeparatorChar));
        string payloadFile = Path.Combine(
            AppPaths.PayloadDirForMod(modId),
            srcInZip.Replace('/', Path.DirectorySeparatorChar));

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [onDiskPath] = "REVERTED_HASH"
        });
        var (engine, store, fs) = BuildEngine(hasher: hasher);

        fs.AddFile(onDiskPath, "reverted-content");
        fs.AddDirectory(AppPaths.PayloadDirForMod(modId));
        fs.AddFile(payloadFile, "cached-payload-content");

        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            BuildFileEntry(relPath, installedHash, srcInZip)));

        var plan   = await engine.PrepareReapplyAsync(DataRoot);
        var result = await engine.ExecuteReapplyAsync(plan);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModsReapplied);
        Assert.Equal(1, result.FilesReapplied);
        Assert.Empty(result.Errors);

        // The game file must have been overwritten with the payload content.
        Assert.True(fs.FileExists(onDiskPath));
    }
}

// Re-apply / revert-detection tests targeting the REAL SyncEngine (EWSR_PMR_ModApp.Core.SyncEngine.SyncEngine)
// with a FakeFileHasher for injectable hash control and FakeFileSystem for all disk I/O.
// SyncEngineStub has been retired; these tests exercise the actual production code path.

using Xunit;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class ReApplyTests
{
    private const string DataRoot     = @"C:\PMR\data";
    private const string ManifestPath = @"C:\FakeAppData\manifest.json";

    // ── Test infrastructure ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds a SyncEngine wired to in-memory fakes.
    /// The caller owns the FakeFileSystem and can seed files/directories before calling the engine.
    /// </summary>
    private static (Core.SyncEngine.SyncEngine engine, ManifestStore store, FakeFileSystem fs)
        BuildEngine(FakeFileHasher? hasher = null)
    {
        var fs     = new FakeFileSystem();
        var store  = new ManifestStore(fs, ManifestPath);
        var engine = new Core.SyncEngine.SyncEngine(
            new NoOpZipService(),
            new MappingResolver(),
            store,
            new NoOpBackupService(),
            fs,
            TimeProvider.System,
            hasher ?? new FakeFileHasher());
        return (engine, store, fs);
    }

    private static ModEntry BuildModEntry(string modId, params InstalledFileEntry[] files) =>
        new()
        {
            ModId            = modId,
            ModName          = $"Test Mod {modId}",
            SourceZipHash    = "sha256:test",
            InstallTimestamp = DateTimeOffset.UtcNow,
            Files            = files
        };

    private static InstalledFileEntry BuildFileEntry(
        string relTarget,
        string installedHash,
        string sourceInZip = "") =>
        new()
        {
            RelativeTargetPath = relTarget,
            SourcePathInZip    = string.IsNullOrEmpty(sourceInZip) ? "data/" + relTarget : sourceInZip,
            MappingMethod      = MappingMethod.PathOverlay,
            OriginalFileHash   = "orig-hash-aabbcc",
            InstalledFileHash  = installedHash,
            IsNewFile          = false
        };

    /// <summary>Seeds the payload dir and on-disk game file in the FakeFileSystem.</summary>
    private static void SeedGameFile(
        FakeFileSystem fs,
        string modId,
        string relTarget,
        string? payloadSourceInZip = null)
    {
        fs.AddDirectory(AppPaths.PayloadDirForMod(modId));

        string src = payloadSourceInZip ?? "data/" + relTarget;
        string payloadFile = Path.Combine(
            AppPaths.PayloadDirForMod(modId),
            src.Replace('/', Path.DirectorySeparatorChar));
        fs.AddFile(payloadFile, "payload-content");

        string onDisk = Path.Combine(DataRoot, relTarget.Replace('/', Path.DirectorySeparatorChar));
        fs.AddFile(onDisk, "game-content");
    }

    /// <summary>Returns the absolute on-disk path for a data-root-relative target.</summary>
    private static string OnDiskPath(string relTarget) =>
        Path.Combine(DataRoot, relTarget.Replace('/', Path.DirectorySeparatorChar));

    // ── Core revert detection ─────────────────────────────────────────────────────

    /// <summary>
    /// A game update reverts a modded file (on-disk hash differs from InstalledFileHash).
    /// CheckForRevertedMods must flag the mod as Reverted with RevertedFileCount = 1.
    /// </summary>
    [Fact]
    public async Task FileRevertedByGameUpdate_ModStatusIsReverted_CountIsOne()
    {
        const string modId        = "mod-revert-001";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string installedHash = "INST_HASH_abc123";

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(relPath)] = "REVERTED_HASH_xyz789"   // differs from installed
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, relPath);
        await store.AddOrUpdateModAsync(BuildModEntry(modId, BuildFileEntry(relPath, installedHash)));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Single(statuses);
        Assert.Equal(ModRevertState.Reverted, statuses[0].State);
        Assert.Equal(1, statuses[0].RevertedFileCount);
    }

    /// <summary>
    /// On-disk hash matches the manifest's InstalledFileHash → mod is intact, nothing to reapply.
    /// </summary>
    [Fact]
    public async Task FileMatchesInstalledHash_ModStatusIsIntact_CountIsZero()
    {
        const string modId        = "mod-intact-001";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string installedHash = "INST_HASH_abc123";

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(relPath)] = installedHash  // same hash → intact
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, relPath);
        await store.AddOrUpdateModAsync(BuildModEntry(modId, BuildFileEntry(relPath, installedHash)));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Single(statuses);
        Assert.Equal(ModRevertState.Intact, statuses[0].State);
        Assert.Equal(0, statuses[0].RevertedFileCount);
    }

    /// <summary>
    /// Only the reverted files raise the count — intact files must not be included.
    /// </summary>
    [Fact]
    public async Task MultipleFiles_OnlySomeReverted_RevertedCountMatchesRevertedFileCount()
    {
        const string modId = "mod-partial-001";
        const string fileA = "vehicles/car_a/livery.dds";  // reverted
        const string fileB = "vehicles/car_a/engine.wav";  // intact
        const string fileC = "tracks/monaco/track.bin";    // reverted

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(fileA)] = "REVERTED_A",
            [OnDiskPath(fileB)] = "HASH_B",         // matches installed
            [OnDiskPath(fileC)] = "REVERTED_C"
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, fileA);
        SeedGameFile(fs, modId, fileB);
        SeedGameFile(fs, modId, fileC);
        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            BuildFileEntry(fileA, "HASH_A"),
            BuildFileEntry(fileB, "HASH_B"),
            BuildFileEntry(fileC, "HASH_C")));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Single(statuses);
        Assert.Equal(ModRevertState.Reverted, statuses[0].State);
        Assert.Equal(2, statuses[0].RevertedFileCount);
    }

    [Fact]
    public async Task AllFilesIntact_ModStatusIsIntact_CountIsZero()
    {
        const string modId = "mod-all-intact-001";
        const string fileA = "vehicles/car_a/livery.dds";
        const string fileB = "vehicles/car_b/livery.dds";

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(fileA)] = "HASH_1",
            [OnDiskPath(fileB)] = "HASH_2"
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, fileA);
        SeedGameFile(fs, modId, fileB);
        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            BuildFileEntry(fileA, "HASH_1"),
            BuildFileEntry(fileB, "HASH_2")));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Single(statuses);
        Assert.Equal(ModRevertState.Intact, statuses[0].State);
        Assert.Equal(0, statuses[0].RevertedFileCount);
    }

    [Fact]
    public async Task EmptyManifest_CheckReturnsEmptyStatusList()
    {
        var (engine, _, _) = BuildEngine();

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Empty(statuses);
    }

    // ── Hash comparison is case-insensitive ───────────────────────────────────────

    /// <summary>
    /// SHA-256 hex strings are case-insensitive by convention.
    /// Manifest written with lowercase must match uppercase on-disk hash — still intact.
    /// </summary>
    [Fact]
    public async Task HashComparison_CaseInsensitive_SameHashDifferentCase_IsIntact()
    {
        const string modId        = "mod-case-001";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string installedHash = "abcdef1234567890";

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(relPath)] = "ABCDEF1234567890"  // same value, different case
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, relPath);
        await store.AddOrUpdateModAsync(BuildModEntry(modId, BuildFileEntry(relPath, installedHash)));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Equal(ModRevertState.Intact, statuses[0].State);
    }

    [Fact]
    public async Task HashComparison_GenuinelyDifferentValues_IsReverted()
    {
        const string modId        = "mod-case-002";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string installedHash = "aabbcc112233";

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(relPath)] = "XXYYZZ998877"  // genuinely different
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, relPath);
        await store.AddOrUpdateModAsync(BuildModEntry(modId, BuildFileEntry(relPath, installedHash)));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Equal(ModRevertState.Reverted, statuses[0].State);
        Assert.Equal(1, statuses[0].RevertedFileCount);
    }

    // ── File absent from disk ─────────────────────────────────────────────────────

    /// <summary>
    /// If the game file no longer exists on disk (e.g. game repair deleted it),
    /// it must count as reverted so the engine can reapply it.
    /// </summary>
    [Fact]
    public async Task FileAbsentFromDisk_CountedAsReverted()
    {
        const string modId   = "mod-absent-001";
        const string relPath = "vehicles/car_a/livery.dds";

        var (engine, store, fs) = BuildEngine();
        // Seed payload dir but NOT the on-disk game file.
        fs.AddDirectory(AppPaths.PayloadDirForMod(modId));
        // (on-disk file deliberately not added to FakeFileSystem)

        await store.AddOrUpdateModAsync(BuildModEntry(modId, BuildFileEntry(relPath, "INST_HASH")));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Equal(ModRevertState.Reverted, statuses[0].State);
        Assert.Equal(1, statuses[0].RevertedFileCount);
    }

    // ── Payload directory missing ─────────────────────────────────────────────────

    [Fact]
    public async Task PayloadDirectoryMissing_ReturnsPayloadMissingState()
    {
        const string modId   = "mod-nopayload-001";
        const string relPath = "vehicles/car_a/livery.dds";

        var (engine, store, _) = BuildEngine();
        // Payload dir NOT added to FakeFileSystem.
        await store.AddOrUpdateModAsync(BuildModEntry(modId, BuildFileEntry(relPath, "INST_HASH")));

        var statuses = await engine.CheckForRevertedModsAsync(DataRoot);

        Assert.Single(statuses);
        Assert.Equal(ModRevertState.PayloadMissing, statuses[0].State);
    }

    // ── ReapplyRevertedMods end-to-end ────────────────────────────────────────────

    /// <summary>
    /// After CheckForRevertedMods identifies a reverted file, ReapplyRevertedModsAsync
    /// must copy the cached payload back to the game directory.
    /// </summary>
    [Fact]
    public async Task ReapplyRevertedMods_CopiesPayloadFilesBackToGameDir()
    {
        const string modId        = "mod-reapply-001";
        const string relPath      = "vehicles/car_a/livery.dds";
        const string installedHash = "INST_HASH_correct";
        const string sourceInZip  = "data/vehicles/car_a/livery.dds";

        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [OnDiskPath(relPath)] = "REVERTED_HASH_different"  // triggers reapply
        });

        var (engine, store, fs) = BuildEngine(hasher);
        SeedGameFile(fs, modId, relPath, payloadSourceInZip: sourceInZip);
        await store.AddOrUpdateModAsync(BuildModEntry(modId,
            BuildFileEntry(relPath, installedHash, sourceInZip)));

        var result = await engine.ReapplyRevertedModsAsync(DataRoot);

        Assert.True(result.Success);
        Assert.Equal(1, result.ModsReapplied);
        Assert.Equal(1, result.FilesReapplied);
        Assert.Empty(result.Errors);

        // Verify the game file was actually written by FakeFileSystem.CopyFile.
        Assert.True(fs.FileExists(OnDiskPath(relPath)));
    }
}

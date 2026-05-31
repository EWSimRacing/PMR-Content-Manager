// Reconciled against EWSR_PMR_ModApp.Core.Manifest.ManifestStore directly.
// Uses FakeFileSystem + ManifestStore's test constructor (IFileSystem, overrideManifestPath)
// so no real disk I/O occurs and no %APPDATA% path is needed.

using Xunit;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;

namespace EWSR_PMR_ModApp.Core.Tests.Manifest;

public class ManifestStoreTests
{
    private const string ManifestPath = @"C:\FakeAppData\manifest.json";

    private static (ManifestStore store, FakeFileSystem fs) CreateStore()
    {
        var fs    = new FakeFileSystem();
        var store = new ManifestStore(fs, ManifestPath);
        return (store, fs);
    }

    private static readonly DateTimeOffset FixedTimestamp =
        new(2026, 5, 31, 16, 0, 0, TimeSpan.FromHours(-4));

    private static ModEntry BuildEntry(string modId, params InstalledFileEntry[] files) =>
        new()
        {
            ModId            = modId,
            ModName          = $"Mod {modId}",
            SourceZipHash    = $"SHA256:{modId.GetHashCode():x8}",
            InstallTimestamp = FixedTimestamp,
            Files            = files
        };

    private static InstalledFileEntry BuildFile(
        string        relativeTarget,
        string        sourceInZip,
        MappingMethod method        = MappingMethod.PathOverlay,
        string?       originalHash  = "ORIG_HASH_abc123",
        string        installedHash = "INST_HASH_def456",
        bool          isNewFile     = false) =>
        new()
        {
            RelativeTargetPath = relativeTarget,
            SourcePathInZip    = sourceInZip,
            MappingMethod      = method,
            OriginalFileHash   = originalHash,
            InstalledFileHash  = installedHash,
            IsNewFile          = isNewFile
        };

    // ── Round-trip: every field survives AddOrUpdate → Load ──────────────────────

    [Fact]
    public async Task RoundTrip_ModId_Preserved()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-alpha"));

        var manifest = await store.LoadAsync();
        Assert.True(manifest.Mods.ContainsKey("mod-alpha"));
        Assert.Equal("mod-alpha", manifest.Mods["mod-alpha"].ModId);
    }

    [Fact]
    public async Task RoundTrip_SourceZipHash_Preserved()
    {
        var (store, _) = CreateStore();
        var entry = BuildEntry("mod-beta");
        await store.AddOrUpdateModAsync(entry);

        var loaded = (await store.LoadAsync()).Mods["mod-beta"];
        Assert.Equal(entry.SourceZipHash, loaded.SourceZipHash);
    }

    [Fact]
    public async Task RoundTrip_InstallTimestamp_Preserved()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-gamma"));

        var loaded = (await store.LoadAsync()).Mods["mod-gamma"];
        Assert.Equal(FixedTimestamp, loaded.InstallTimestamp);
    }

    [Fact]
    public async Task RoundTrip_AllPerFileFields_Preserved()
    {
        var (store, _) = CreateStore();
        var file = BuildFile(
            relativeTarget: "vehicles/car_a/livery.dds",
            sourceInZip:    "data/vehicles/car_a/livery.dds",
            method:         MappingMethod.PathOverlay,
            originalHash:   "ORIG_abc",
            installedHash:  "INST_xyz",
            isNewFile:      false);
        await store.AddOrUpdateModAsync(BuildEntry("mod-delta", file));

        var loadedFile = (await store.LoadAsync()).Mods["mod-delta"].Files.Single();

        Assert.Equal(file.RelativeTargetPath, loadedFile.RelativeTargetPath);
        Assert.Equal(file.SourcePathInZip,    loadedFile.SourcePathInZip);
        Assert.Equal(file.MappingMethod,      loadedFile.MappingMethod);
        Assert.Equal(file.OriginalFileHash,   loadedFile.OriginalFileHash);
        Assert.Equal(file.InstalledFileHash,  loadedFile.InstalledFileHash);
        Assert.Equal(file.IsNewFile,          loadedFile.IsNewFile);
    }

    [Fact]
    public async Task RoundTrip_NullOriginalHash_PreservedForNewFiles()
    {
        var (store, _) = CreateStore();
        var file = BuildFile(
            "vehicles/new_car/brand_new.dds",
            "brand_new.dds",
            originalHash: null,
            isNewFile: true);
        await store.AddOrUpdateModAsync(BuildEntry("mod-epsilon", file));

        var loaded = (await store.LoadAsync()).Mods["mod-epsilon"].Files.Single();
        Assert.Null(loaded.OriginalFileHash);
        Assert.True(loaded.IsNewFile);
    }

    [Fact]
    public async Task RoundTrip_FilenameMatchMappingMethod_Preserved()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-zeta",
            BuildFile("vehicles/car_a/livery.dds", "livery.dds", method: MappingMethod.FilenameMatch)));

        var loaded = (await store.LoadAsync()).Mods["mod-zeta"].Files.Single();
        Assert.Equal(MappingMethod.FilenameMatch, loaded.MappingMethod);
    }

    [Fact]
    public async Task RoundTrip_ModInfoMappingMethod_Preserved()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-eta",
            BuildFile("vehicles/car_a/livery.dds", "livery.dds", method: MappingMethod.ModInfo)));

        var loaded = (await store.LoadAsync()).Mods["mod-eta"].Files.Single();
        Assert.Equal(MappingMethod.ModInfo, loaded.MappingMethod);
    }

    [Fact]
    public async Task RoundTrip_MultipleFiles_AllPreserved()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-theta",
            BuildFile("vehicles/car_a/livery.dds",  "data/vehicles/car_a/livery.dds"),
            BuildFile("vehicles/car_a/preview.png",  "data/vehicles/car_a/preview.png"),
            BuildFile("sounds/engine_custom.wav",    "sounds/engine_custom.wav",
                      method: MappingMethod.FilenameMatch)));

        var files = (await store.LoadAsync()).Mods["mod-theta"].Files;

        Assert.Equal(3, files.Count);
        Assert.Contains(files, f => f.RelativeTargetPath == "vehicles/car_a/livery.dds");
        Assert.Contains(files, f => f.RelativeTargetPath == "vehicles/car_a/preview.png");
        Assert.Contains(files, f => f.RelativeTargetPath == "sounds/engine_custom.wav");
    }

    // ── Conflict detection ────────────────────────────────────────────────────────

    [Fact]
    public async Task TwoModsTargetingSamePath_ConflictDetected()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "data/vehicles/car_a/livery.dds")));

        var modB      = BuildEntry("mod-b",
            BuildFile("vehicles/car_a/livery.dds", "livery.dds"));
        var conflicts = await store.DetectConflictsAsync(modB);

        Assert.Single(conflicts);
        var (existingId, candidateId, path) = conflicts[0];
        Assert.Equal("mod-a",                     existingId);
        Assert.Equal("mod-b",                     candidateId);
        Assert.Equal("vehicles/car_a/livery.dds", path);
    }

    [Fact]
    public async Task TwoModsTargetingDifferentPaths_NoConflict()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "data/vehicles/car_a/livery.dds")));

        var modB = BuildEntry("mod-b",
            BuildFile("vehicles/car_b/livery.dds", "data/vehicles/car_b/livery.dds"));

        Assert.Empty(await store.DetectConflictsAsync(modB));
    }

    [Fact]
    public async Task SameMod_Reinstalled_DoesNotConflictWithItself()
    {
        var (store, _) = CreateStore();
        var mod = BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "data/vehicles/car_a/livery.dds"));
        await store.AddOrUpdateModAsync(mod);

        Assert.Empty(await store.DetectConflictsAsync(mod));
    }

    [Fact]
    public async Task MultipleConflicts_AllReported()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "data/vehicles/car_a/livery.dds"),
            BuildFile("sounds/engine.wav",          "data/sounds/engine.wav")));

        var modB = BuildEntry("mod-b",
            BuildFile("vehicles/car_a/livery.dds", "livery.dds"),
            BuildFile("sounds/engine.wav",          "engine.wav"),
            BuildFile("tracks/monaco/track.bin",    "track.bin")); // no conflict

        var conflicts = await store.DetectConflictsAsync(modB);

        Assert.Equal(2, conflicts.Count);
        Assert.All(conflicts, c => Assert.Equal("mod-a", c.ExistingModId));
        Assert.All(conflicts, c => Assert.Equal("mod-b", c.CandidateModId));
    }

    // ── IsFileOwnedByMod query ────────────────────────────────────────────────────

    [Fact]
    public async Task IsFileOwnedByMod_FileInstalledByMod_ReturnsTrue()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "data/vehicles/car_a/livery.dds")));

        Assert.True(await store.IsFileOwnedByModAsync("vehicles/car_a/livery.dds"));
    }

    [Fact]
    public async Task IsFileOwnedByMod_FileNotOwned_ReturnsFalse()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "data/vehicles/car_a/livery.dds")));

        Assert.False(await store.IsFileOwnedByModAsync("vehicles/car_b/engine.wav"));
    }

    [Fact]
    public async Task IsFileOwnedByMod_EmptyStore_ReturnsFalse()
    {
        var (store, _) = CreateStore();

        Assert.False(await store.IsFileOwnedByModAsync("vehicles/car_a/livery.dds"));
    }

    [Fact]
    public async Task IsFileOwnedByMod_PathLookup_IsCaseInsensitive()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("Vehicles/Car_A/Livery.dds", "Vehicles/Car_A/Livery.dds")));

        Assert.True(await store.IsFileOwnedByModAsync("vehicles/car_a/livery.dds"));
    }

    // ── RemoveMod and re-load ─────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMod_ExistingMod_RemovedFromManifest()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a"));
        await store.RemoveModAsync("mod-a");

        var manifest = await store.LoadAsync();
        Assert.False(manifest.Mods.ContainsKey("mod-a"));
    }

    [Fact]
    public async Task RemoveMod_NonExistentId_NoOp_DoesNotThrow()
    {
        var (store, _) = CreateStore();
        await store.RemoveModAsync("ghost-mod");
        var manifest = await store.LoadAsync();
        Assert.Empty(manifest.Mods);
    }

    // ── LoadAsync before any save returns an empty (not null) manifest ───────────

    [Fact]
    public async Task LoadAsync_BeforeAnySave_ReturnsEmptyManifest_NotNull()
    {
        var (store, _) = CreateStore();

        var manifest = await store.LoadAsync();

        Assert.NotNull(manifest);
        Assert.Empty(manifest.Mods);
    }

    // ── AddOrUpdate replaces on second save ───────────────────────────────────────

    [Fact]
    public async Task AddOrUpdate_SecondCall_ReplacesFirstEntry()
    {
        var (store, _) = CreateStore();
        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery.dds", "livery.dds")));

        await store.AddOrUpdateModAsync(BuildEntry("mod-a",
            BuildFile("vehicles/car_a/livery_v2.dds", "livery_v2.dds")));

        var files = (await store.LoadAsync()).Mods["mod-a"].Files;
        Assert.Single(files);
        Assert.Equal("vehicles/car_a/livery_v2.dds", files[0].RelativeTargetPath);
    }
}

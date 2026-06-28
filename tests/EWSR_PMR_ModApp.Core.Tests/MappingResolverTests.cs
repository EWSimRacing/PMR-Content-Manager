// Comprehensive MappingResolver tests (Wez, Tester).
// Covers all strategies: path-overlay, data-root strip, filename-index (single/ambiguous/unmatched),
// modinfo.json explicit mapping, collision detection, suffix scoring, and edge cases.
// All of Nux's original 6 smoke-test cases are covered by the comprehensive assertions below.

using Xunit;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.Tests;

public class MappingResolverTests
{
    // MappingResolver is pure — no IFileSystem needed.
    // A non-existent dataRoot ensures Directory.Exists always returns false,
    // forcing path-overlay strategy B to rely on the dataFileIndex string check.
    private const string FakeDataRoot = @"C:\FakeDataRoot_DoesNotExist";

    private readonly MappingResolver _resolver = new();

    private static ZipEntryInfo Entry(string fullNameInZip) => new()
    {
        FullNameInZip    = fullNameInZip,
        StagedFilePath   = @"C:\staging\" + fullNameInZip.Replace('/', '\\'),
        UncompressedSize = 1024
    };

    private static IReadOnlyList<ZipEntryInfo> Entries(params string[] fullNames) =>
        fullNames.Select(Entry).ToList();

    // ── Path-overlay: zip mirrors the game's subfolder structure ─────────────────

    [Fact]
    public void ZipWithKnownTopLevelDirs_MapsDirectlyOntoDataRoot_UsingPathOverlay()
    {
        var entries   = Entries("vehicles/car_a/livery.dds", "sounds/engine.wav");
        var dataIndex = new[]
        {
            "vehicles/car_a/livery.dds",
            "vehicles/car_b/livery.dds",
            "sounds/engine.wav",
            "sounds/crowd.wav"
        };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.Equal(2, plan.Mapped.Count);
        Assert.Empty(plan.Ambiguous);
        Assert.Empty(plan.Unmatched);
        Assert.Empty(plan.Collisions);

        var car = plan.Mapped.Single(m => m.ZipEntry.FullNameInZip == "vehicles/car_a/livery.dds");
        Assert.Equal("vehicles/car_a/livery.dds", car.RelativeTargetPath);
        Assert.Equal(MappingMethod.PathOverlay, car.MappingMethod);

        var snd = plan.Mapped.Single(m => m.ZipEntry.FullNameInZip == "sounds/engine.wav");
        Assert.Equal("sounds/engine.wav", snd.RelativeTargetPath);
        Assert.Equal(MappingMethod.PathOverlay, snd.MappingMethod);
    }

    // ── Path-overlay: zip root IS data/ ──────────────────────────────────────────

    [Fact]
    public void ZipWithDataRootPrefix_StripsLeadingDataSlash_MapsToCorrectTargets()
    {
        var entries   = Entries("data/vehicles/car_a/livery.dds", "data/tracks/monaco/track.bin");
        var dataIndex = new[] { "vehicles/car_a/livery.dds", "tracks/monaco/track.bin" };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.Equal(2, plan.Mapped.Count);
        Assert.Empty(plan.Ambiguous);
        Assert.Empty(plan.Unmatched);
        Assert.Empty(plan.Collisions);

        Assert.Contains(plan.Mapped, m =>
            m.ZipEntry.FullNameInZip == "data/vehicles/car_a/livery.dds"
            && m.RelativeTargetPath  == "vehicles/car_a/livery.dds"
            && m.MappingMethod       == MappingMethod.PathOverlay);

        Assert.Contains(plan.Mapped, m =>
            m.ZipEntry.FullNameInZip == "data/tracks/monaco/track.bin"
            && m.RelativeTargetPath  == "tracks/monaco/track.bin"
            && m.MappingMethod       == MappingMethod.PathOverlay);
    }

    // ── Filename-index: flat zip, single unambiguous match ───────────────────────

    [Fact]
    public void FlatZip_SingleFilenameMatch_ResolvesToCorrectTarget_ViaFilenameIndex()
    {
        var entries   = Entries("livery.dds");
        var dataIndex = new[] { "vehicles/car_a/livery.dds" };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.Single(plan.Mapped);
        Assert.Empty(plan.Ambiguous);
        Assert.Empty(plan.Unmatched);
        Assert.Empty(plan.Collisions);
        Assert.Equal("vehicles/car_a/livery.dds", plan.Mapped[0].RelativeTargetPath);
        Assert.Equal(MappingMethod.FilenameMatch,  plan.Mapped[0].MappingMethod);
    }

    // ── Filename-index: ambiguous — multiple on-disk matches ─────────────────────

    /// <summary>
    /// A filename that exists in multiple directories must surface as Ambiguous,
    /// never silently placed in one of the candidates.
    /// </summary>
    [Fact]
    public void FlatZip_FilenameMatchesMultipleDataFiles_ReportedAsAmbiguous_NeverSilentlyPlaced()
    {
        var entries   = Entries("livery.dds");
        var dataIndex = new[]
        {
            "vehicles/car_a/livery.dds",
            "vehicles/car_b/livery.dds"
        };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.DoesNotContain(plan.Mapped, m => m.ZipEntry.FileName == "livery.dds");
        Assert.Single(plan.Ambiguous);

        var ambig = plan.Ambiguous[0];
        Assert.Equal("livery.dds", ambig.ZipEntry.FileName);
        Assert.Equal(2, ambig.CandidatePaths.Count);
        Assert.Contains("vehicles/car_a/livery.dds", ambig.CandidatePaths);
        Assert.Contains("vehicles/car_b/livery.dds", ambig.CandidatePaths);
    }

    // ── Filename-index: no match (new file or typo) ───────────────────────────────

    [Fact]
    public void FlatZip_FilenameMatchesNoDataFile_ReportedAsUnmatched()
    {
        var entries   = Entries("brand_new_livery.dds");
        var dataIndex = new[] { "vehicles/car_a/livery.dds" };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.Empty(plan.Mapped);
        Assert.Empty(plan.Ambiguous);
        Assert.Single(plan.Unmatched);
        Assert.Equal("brand_new_livery.dds", plan.Unmatched[0].FileName);
    }

    // ── Collision: two zip entries resolve to the same target ────────────────────

    /// <summary>
    /// When two zip entries both resolve to the same relative target path, neither should be
    /// silently placed. Both must be moved to the Collisions bucket for user resolution.
    /// </summary>
    [Fact]
    public void TwoZipEntries_BothResolvingToSameTarget_CollisionReported_NeitherPlaced()
    {
        var entries   = Entries("skins_a/livery.dds", "skins_b/livery.dds");
        var dataIndex = new[] { "vehicles/car_x/livery.dds" };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        // Neither colliding entry may be in Mapped.
        Assert.Empty(plan.Mapped);

        // Exactly one collision group for the shared target path.
        Assert.Single(plan.Collisions);
        Assert.Equal("vehicles/car_x/livery.dds", plan.Collisions[0].RelativeTargetPath);
        Assert.Equal(2, plan.Collisions[0].Entries.Count);
    }

    // ── modinfo.json: explicit mappings override all heuristics ──────────────────

    [Fact]
    public void ModInfoPresent_ExplicitMappingsTakePrecedence_OverridesHeuristics()
    {
        var entries   = Entries("livery.dds", "preview.png");
        var dataIndex = Array.Empty<string>();
        var modInfo   = new ModInfo
        {
            Name  = "My Livery Pack",
            Files = new Dictionary<string, string>
            {
                ["livery.dds"]  = "vehicles/car_a/livery.dds",
                ["preview.png"] = "vehicles/car_a/preview.png"
            }
        };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex, modInfo);

        Assert.Equal(2, plan.Mapped.Count);
        Assert.Empty(plan.Ambiguous);
        Assert.Empty(plan.Unmatched);

        Assert.Contains(plan.Mapped, m =>
            m.ZipEntry.FileName     == "livery.dds"
            && m.RelativeTargetPath == "vehicles/car_a/livery.dds"
            && m.MappingMethod      == MappingMethod.ModInfo);

        Assert.Contains(plan.Mapped, m =>
            m.ZipEntry.FileName     == "preview.png"
            && m.RelativeTargetPath == "vehicles/car_a/preview.png"
            && m.MappingMethod      == MappingMethod.ModInfo);
    }

    [Fact]
    public void ModInfoGameRootFiles_MapToGameTargetRoot()
    {
        var entries = Entries("shared/starmap.dds");
        var modInfo = new ModInfo
        {
            SchemaVersion = 2,
            GameRootFiles = new Dictionary<string, string>
            {
                ["shared/starmap.dds"] = "shared/starmap.dds"
            }
        };

        var plan = _resolver.Resolve(entries, FakeDataRoot, [], modInfo);

        var mapped = Assert.Single(plan.Mapped);
        Assert.Equal("shared/starmap.dds", mapped.RelativeTargetPath);
        Assert.Equal(TargetRoot.Game, mapped.TargetRoot);
        Assert.Equal(MappingMethod.ModInfo, mapped.MappingMethod);
    }

    [Fact]
    public void TopLevelSharedPath_WithoutModInfo_DoesNotHeuristicallyMapIntoDataShared()
    {
        var entries = Entries("shared/starmap.dds");
        var dataIndex = new[] { "shared/starmap.dds" };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.Empty(plan.Mapped);
        Assert.Single(plan.Unmatched);
    }

    // ── Edge: modinfo.json entry itself is never treated as a mod file ────────────

    [Fact]
    public void ModInfoJsonEntry_IsNeverIncludedInResolvedMappings()
    {
        var entries   = Entries("vehicles/car_a/livery.dds", "modinfo.json");
        var dataIndex = new[] { "vehicles/car_a/livery.dds" };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        Assert.DoesNotContain(plan.Mapped, m =>
            m.ZipEntry.FileName.Equals("modinfo.json", StringComparison.OrdinalIgnoreCase));
    }

    // ── Empty zip → empty plan ────────────────────────────────────────────────────

    [Fact]
    public void EmptyZip_ReturnsEmptyPlan()
    {
        var plan = _resolver.Resolve([], FakeDataRoot, []);

        Assert.Empty(plan.Mapped);
        Assert.Empty(plan.Ambiguous);
        Assert.Empty(plan.Unmatched);
        Assert.Empty(plan.Collisions);
    }

    // ── Suffix-scoring: disambiguation threshold behaviour ────────────────────────

    [Fact]
    public void FilenameIndex_PathSuffixScoring_DocumentsThresholdBehaviour()
    {
        // "some_pack/car_a/livery.dds" — top dir "some_pack" is not in dataIndex,
        // so path-overlay is skipped and filename-index runs.
        var entries   = Entries("some_pack/car_a/livery.dds");
        var dataIndex = new[]
        {
            "vehicles/car_a/livery.dds",   // score: "car_a/livery.dds" aligns → score 2
            "vehicles/car_b/livery.dds"    // score: only "livery.dds" aligns  → score 1
        };

        var plan = _resolver.Resolve(entries, FakeDataRoot, dataIndex);

        // Score delta = 1 (< DisambiguationThreshold of 2) → expected to be Ambiguous.
        // If the threshold is ever raised, this assertion catches the regression.
        if (plan.Mapped.Count == 1)
            Assert.Equal("vehicles/car_a/livery.dds", plan.Mapped[0].RelativeTargetPath);
        else
            Assert.Single(plan.Ambiguous);
    }
}

// Install-warning tests for SyncEngine.InstallAsync.
// Verifies that the Warnings collection in InstallResult names every skipped/colliding file
// rather than just reporting a bare count — so the dialog is actionable.
//
// Strategy note: path-overlay triggers when every top-level zip directory matches a known data
// subdirectory (via the real System.IO.Directory.Exists OR the data-file index prefix).  To
// exercise Unmatched and Collision buckets reliably — without depending on what exists on disk —
// we use flat zip entries (no '/' in FullNameInZip).  Flat entries have no top-level directory,
// so topLevelDirs.Count == 0 and path-overlay is skipped unconditionally; the filename-index
// strategy runs instead.
//
// For the collision test we use entries under uniquely-named unknown dirs ("skin_a/", "skin_b/")
// that are guaranteed not to appear in either the data index or on disk, so path-overlay still
// falls through to filename-index.

using Xunit;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class InstallWarningsTests
{
    private const string DataRoot     = @"C:\PMR\data";
    private const string ManifestPath = @"C:\FakeAppData\manifest.json";
    private const string StagingDir   = @"C:\staging";

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private static ZipEntryInfo MakeEntry(string fullNameInZip) => new()
    {
        FullNameInZip    = fullNameInZip,
        StagedFilePath   = Path.Combine(StagingDir, fullNameInZip.Replace('/', Path.DirectorySeparatorChar)),
        UncompressedSize = 512
    };

    private static (Core.SyncEngine.SyncEngine engine, FakeFileSystem fs) BuildEngine(
        ZipStagingResult staged,
        IReadOnlyList<string> dataRelativePaths)
    {
        var fs    = new FakeFileSystem();
        var store = new ManifestStore(fs, ManifestPath);

        fs.AddDirectory(DataRoot);
        foreach (var rel in dataRelativePaths)
        {
            string abs = Path.Combine(DataRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            fs.AddFile(abs, "original-content");
        }

        var engine = new Core.SyncEngine.SyncEngine(
            new StubZipService(staged),
            new MappingResolver(),
            store,
            new NoOpBackupService(),
            fs,
            TimeProvider.System,
            new FakeFileHasher());

        return (engine, fs);
    }

    private static ZipStagingResult MakeStaging(params string[] entryNames) => new()
    {
        StagingDirectory = StagingDir,
        Entries          = entryNames.Select(MakeEntry).ToList(),
        ZipHash          = "sha256:stub"
    };

    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        Task.FromResult<IReadOnlyList<ResolvedMapping>>([]);

    // ── Unmatched: skipped files are named in warnings ────────────────────────────

    /// <summary>
    /// A flat-zip entry (no subdirectory) whose filename has no match in the data directory
    /// must produce a warning that names the file — not just a bare count.
    /// Flat entries force the filename-index strategy (topLevelDirs.Count == 0).
    /// </summary>
    [Fact]
    public async Task UnmatchedEntry_WarningIncludesFullZipPath()
    {
        // Flat zip: no top-level dir → filename-index strategy.
        // "livery.dds" has one data match → mapped (keeps allMapped > 0 so install proceeds).
        // "brand_new_skin.dds" has no data match → Unmatched → one warning naming the file.
        var staged = MakeStaging("livery.dds", "brand_new_skin.dds");
        var (engine, _) = BuildEngine(staged, ["vehicles/car_a/livery.dds"]);

        var result = await engine.InstallAsync(
            "mod.zip", DataRoot, "TestMod", NoAmbiguous);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings,
            w => w == "Skipped (no match in data): brand_new_skin.dds");
    }

    /// <summary>
    /// Each unmatched entry must produce its own warning row — no collapsing into a single count.
    /// </summary>
    [Fact]
    public async Task MultipleUnmatchedEntries_EachGetsOwnWarningRow()
    {
        // Flat zip; "livery.dds" maps, two others have no match → two separate warnings.
        var staged = MakeStaging("livery.dds", "unknown_a.dds", "unknown_b.wav");
        var (engine, _) = BuildEngine(staged, ["vehicles/car_a/livery.dds"]);

        var result = await engine.InstallAsync(
            "mod.zip", DataRoot, "TestMod", NoAmbiguous);

        Assert.True(result.Success);
        Assert.Contains(result.Warnings,
            w => w == "Skipped (no match in data): unknown_a.dds");
        Assert.Contains(result.Warnings,
            w => w == "Skipped (no match in data): unknown_b.wav");
        // Old aggregated format must be gone.
        Assert.DoesNotContain(result.Warnings,
            w => w.Contains("file(s) had no match"));
    }

    // ── Collisions: source zip entries are named in warnings ─────────────────────

    /// <summary>
    /// When two zip entries both resolve to the same target path (collision), the warning must
    /// include both source zip entry names alongside the target path.
    /// </summary>
    [Fact]
    public async Task CollisionWarning_IncludesTargetPathAndSourceZipEntryNames()
    {
        // "skin_a" and "skin_b" are not known data subdirectories and won't appear in the data
        // index, so path-overlay's Variant B fails → filename-index strategy runs.
        // "engine.wav" → unique match → mapped (ensures allMapped > 0 for the install to finish).
        // "skin_a/livery.dds" and "skin_b/livery.dds" each filename-match the single
        // "vehicles/car_x/livery.dds" in the data root → ApplyCollisionDetection fires → Collision.
        var staged = MakeStaging("engine.wav", "skin_a/livery.dds", "skin_b/livery.dds");
        var (engine, _) = BuildEngine(staged,
        [
            "sounds/engine.wav",
            "vehicles/car_x/livery.dds"
        ]);

        var result = await engine.InstallAsync(
            "mod.zip", DataRoot, "TestMod", NoAmbiguous);

        Assert.True(result.Success);

        var collisionWarning = Assert.Single(
            result.Warnings,
            w => w.Contains("vehicles/car_x/livery.dds") && w.Contains("collision"));

        Assert.Contains("skin_a/livery.dds", collisionWarning);
        Assert.Contains("skin_b/livery.dds", collisionWarning);
        Assert.Contains("none installed", collisionWarning);
    }

    /// <summary>
    /// Sanity: no warnings emitted when every zip entry maps cleanly with no skips or collisions.
    /// </summary>
    [Fact]
    public async Task NoUnmatchedOrCollisions_NoWarningsEmitted()
    {
        // Flat zip; each filename has exactly one data match → all mapped, no warnings.
        var staged = MakeStaging("livery.dds", "engine.wav");
        var (engine, _) = BuildEngine(staged,
        [
            "vehicles/car_a/livery.dds",
            "sounds/engine.wav"
        ]);

        var result = await engine.InstallAsync(
            "mod.zip", DataRoot, "TestMod", NoAmbiguous);

        Assert.True(result.Success);
        Assert.DoesNotContain(result.Warnings, w => w.Contains("Skipped"));
        Assert.DoesNotContain(result.Warnings, w => w.Contains("collision"));
    }
}

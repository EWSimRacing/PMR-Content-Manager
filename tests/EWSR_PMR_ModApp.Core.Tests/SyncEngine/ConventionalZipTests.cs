using System.IO.Compression;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class ConventionalZipTests
{
    private const string DataRoot = @"C:\ZipFixturePMR\data";
    private const string Target = @"C:\ZipFixturePMR\data\vehicles\car\skin.dds";

    [Theory]
    [InlineData("data/vehicles/car/skin.dds")]
    [InlineData("MyMod/data/vehicles/car/skin.dds")]
    [InlineData("Download/MyMod/v1/data/vehicles/car/skin.dds")]
    [InlineData("vehicles/car/skin.dds")]
    [InlineData("skin.dds")]
    public async Task SupportedLayouts_InstallBackupAndUninstall(string source)
    {
        await WithArchive([(source, "mod"), ("README.md", "docs")], async (zip, fs, engine) =>
        {
            var result = await engine.InstallAsync(zip, DataRoot, "Normal", NoAmbiguous);
            Assert.True(result.Success);
            Assert.Equal("mod", fs.ReadAllText(Target));
            Assert.True((await engine.UninstallAsync(result.ModId!, DataRoot)).Success);
            Assert.Equal("original", fs.ReadAllText(Target));
        });
    }

    [Fact]
    public async Task RootDataAndLooseAsset_BothMappedWithoutStrippingLooseFile()
    {
        await WithArchive([("data/vehicles/car/skin.dds", "skin"), ("settings.xml", "config"), ("backup.bak", "junk")],
            async (zip, fs, engine) =>
            {
                string config = Path.Combine(DataRoot, "vehicles", "car", "settings.xml");
                fs.AddFile(config, "old");
                var result = await engine.InstallAsync(zip, DataRoot, "Mixed", NoAmbiguous);
                Assert.Equal(2, result.FilesInstalled);
                Assert.Equal("config", fs.ReadAllText(config));
                Assert.Contains(result.Warnings, w => w.Contains("backup.bak"));
            });
    }

    [TireSenseArchiveFact]
    public async Task ActualTireSenseArchive_InstallsTwoLooseEvalAssets_NotBundledAudioArchive()
    {
        string archive = Environment.GetEnvironmentVariable("PMR_CM_TIRESENSE_ARCHIVE")!;
        const string bundled = "Cockpit_Immersion_Audio_PMR_v1.0.3.zip";
        string[] names = ["ffb.rack.eval", "ffb.tire.eval"];
        var payloads = new Dictionary<string, byte[]>();
        using (var zip = ZipFile.OpenRead(archive))
        {
            Assert.Equal(3, zip.Entries.Count);
            Assert.NotNull(zip.GetEntry(bundled));
            foreach (string name in names)
            {
                using var input = zip.GetEntry(name)!.Open();
                using var bytes = new MemoryStream();
                input.CopyTo(bytes);
                payloads[name] = bytes.ToArray();
            }
        }

        var fs = new FakeFileSystem().AddFile(archive);
        foreach (string name in names)
            fs.AddFile(Path.Combine(DataRoot, "vehicles", "_shared", "physics", "hadron", "Chassis", name),
                $"original {name}");
        var store = new ManifestStore(fs, @"C:\ZipFixtureManifest\manifest.json");
        var engine = new Core.SyncEngine.SyncEngine(new ZipService(fs), new MappingResolver(),
            store, new BackupService(fs), fs, TimeProvider.System, new FakeFileHasher());

        var result = await engine.InstallAsync(archive, DataRoot, "TireSensev1.6.1", NoAmbiguous);
        Assert.True(result.Success);
        Assert.Equal(2, result.FilesInstalled);
        Assert.Contains(bundled, Assert.Single(result.Warnings));
        Assert.Contains("Packaging artifact", result.Warnings[0]);
        var mod = Assert.Single((await store.LoadAsync()).Mods.Values);
        Assert.Equal(2, mod.Files.Count);
        Assert.Null(mod.NativePatchId);
        Assert.Null(mod.Hooks);
        foreach (string name in names)
        {
            Assert.Contains(mod.Files, f => f.RelativeTargetPath.Replace('\\', '/').Equals(
                $"vehicles/_shared/physics/hadron/Chassis/{name}", StringComparison.OrdinalIgnoreCase));
            using var installed = fs.OpenRead(Path.Combine(DataRoot, "vehicles", "_shared", "physics", "hadron", "Chassis", name));
            using var bytes = new MemoryStream();
            installed.CopyTo(bytes);
            Assert.Equal(payloads[name], bytes.ToArray());
        }
        Assert.DoesNotContain(fs.EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories),
            path => Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase));
        Assert.False(fs.DirectoryExists(Path.Combine(AppPaths.AppDataRoot, "scripts")));
        Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));

        Assert.True((await engine.UninstallAsync(mod.ModId, DataRoot)).Success);
        foreach (string name in names)
            Assert.Equal($"original {name}", fs.ReadAllText(
                Path.Combine(DataRoot, "vehicles", "_shared", "physics", "hadron", "Chassis", name)));
        Assert.Empty((await store.LoadAsync()).Mods);
    }

    [Fact]
    public async Task DuplicateBasename_RequiresConfirmation_AndDoesNotWriteIfSkipped()
    {
        await WithArchive([("skin.dds", "mod")], async (zip, fs, engine) =>
        {
            fs.AddFile(Path.Combine(DataRoot, "vehicles", "other", "skin.dds"), "other-original");
            bool prompted = false;
            await Assert.ThrowsAsync<UnsupportedModPackageException>(() => engine.InstallAsync(zip, DataRoot, "Ambiguous", entries =>
            {
                prompted = true;
                Assert.Equal(2, Assert.Single(entries).CandidatePaths.Count);
                return Task.FromResult<IReadOnlyList<ResolvedMapping>>([]);
            }));
            Assert.True(prompted);
            Assert.Equal("original", fs.ReadAllText(Target));
        });
    }

    public sealed class TireSenseArchiveFactAttribute : FactAttribute
    {
        public TireSenseArchiveFactAttribute()
        {
            string? archive = Environment.GetEnvironmentVariable("PMR_CM_TIRESENSE_ARCHIVE");
            if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive))
                Skip = "Set PMR_CM_TIRESENSE_ARCHIVE to opt into read-only TireSense ZIP fixture coverage.";
        }
    }

    [Fact]
    public async Task DuplicateBasename_ChosenCandidateInstallsOnlyChosenFile()
    {
        await WithArchive([("skin.dds", "mod")], async (zip, fs, engine) =>
        {
            string other = Path.Combine(DataRoot, "vehicles", "other", "skin.dds");
            fs.AddFile(other, "other-original");
            var result = await engine.InstallAsync(zip, DataRoot, "Selected", entries =>
                Task.FromResult<IReadOnlyList<ResolvedMapping>>([new ResolvedMapping
                {
                    ZipEntry = entries[0].ZipEntry, ChosenRelativeTargetPath = "vehicles/car/skin.dds"
                }]));
            Assert.Equal(1, result.FilesInstalled);
            Assert.Equal("mod", fs.ReadAllText(Target));
            Assert.Equal("other-original", fs.ReadAllText(other));
        });
    }

    [Theory]
    [InlineData("../escape.dds")]
    [InlineData("vehicles/new.dds")]
    public async Task AmbiguousResolution_CannotIntroduceAnUnofferedDestination(string destination)
    {
        await WithArchive([("skin.dds", "mod")], async (zip, fs, engine) =>
        {
            fs.AddFile(Path.Combine(DataRoot, "vehicles", "other", "skin.dds"), "other-original");
            await Assert.ThrowsAsync<InvalidDataException>(() => engine.InstallAsync(zip, DataRoot, "Bad Selection", entries =>
                Task.FromResult<IReadOnlyList<ResolvedMapping>>([new ResolvedMapping
                {
                    ZipEntry = entries[0].ZipEntry, ChosenRelativeTargetPath = destination
                }])));
            Assert.Equal("original", fs.ReadAllText(Target));
        });
    }

    [Fact]
    public async Task DuplicateTargetSources_AreNeverInstalled()
    {
        await WithArchive([("skin_a/skin.dds", "first"), ("skin_b/skin.dds", "second")], async (zip, fs, engine) =>
        {
            var ex = await Assert.ThrowsAsync<UnsupportedModPackageException>(() => engine.InstallAsync(zip, DataRoot, "Collision", NoAmbiguous));
            Assert.Contains("skin_a/skin.dds", ex.Message);
            Assert.Contains("skin_b/skin.dds", ex.Message);
            Assert.Equal("original", fs.ReadAllText(Target));
        });
    }

    [Fact]
    public async Task ConfirmedDestinations_CollideEvenWithDifferentSeparators()
    {
        await WithArchive([("skin_a/skin.dds", "first"), ("skin_b/skin.dds", "second")], async (zip, fs, engine) =>
        {
            fs.AddFile(Path.Combine(DataRoot, "vehicles", "other", "skin.dds"), "other-original");
            var ex = await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
                engine.InstallAsync(zip, DataRoot, "Confirmed Collision", entries =>
                    Task.FromResult<IReadOnlyList<ResolvedMapping>>([
                        new ResolvedMapping { ZipEntry = entries[0].ZipEntry, ChosenRelativeTargetPath = "vehicles/car/skin.dds" },
                        new ResolvedMapping { ZipEntry = entries[1].ZipEntry, ChosenRelativeTargetPath = @"vehicles\car\skin.dds" }
                    ])));
            Assert.Contains("collision after confirmation", ex.Message);
            Assert.Contains("skin_a/skin.dds", ex.Message);
            Assert.Contains("skin_b/skin.dds", ex.Message);
            Assert.Equal("original", fs.ReadAllText(Target));
        });
    }

    [Fact]
    public async Task CustomManifest_FullPathMappingWinsOverBasename()
    {
        const string metadata = """
            {"schemaVersion":1,"files":{"effect.fx":"vehicles/wrong.fx","custom/effect.fx":"vehicles/car/effect.fx"}}
            """;
        await WithArchive([("custom/effect.fx", "effect"), ("modinfo.json", metadata)], async (zip, fs, engine) =>
        {
            var result = await engine.InstallAsync(zip, DataRoot, "Custom", NoAmbiguous);
            Assert.Equal(1, result.FilesInstalled);
            Assert.Equal("effect", fs.ReadAllText(Path.Combine(DataRoot, "vehicles", "car", "effect.fx")));
            Assert.False(fs.FileExists(Path.Combine(DataRoot, "vehicles", "wrong.fx")));
        });
    }

    [Fact]
    public async Task DirectGameLayout_ContainingNestedData_DoesNotStripThatGameFolder()
    {
        const string source = "vehicles/Acura_ARX-06_2023/data/Acura_ARX-06_2023_audioConfig.xml";
        await WithArchive([(source, "mod")], async (zip, fs, engine) =>
        {
            string target = Path.Combine(DataRoot, source.Replace('/', '\\'));
            fs.AddFile(target, "original audio");
            var result = await engine.InstallAsync(zip, DataRoot, "Direct Audio", NoAmbiguous);
            Assert.Equal(1, result.FilesInstalled);
            Assert.Equal("mod", fs.ReadAllText(target));
            Assert.False(fs.FileExists(Path.Combine(DataRoot, "Acura_ARX-06_2023_audioConfig.xml")));
        });
    }

    [Fact]
    public async Task ManifestInstallMarker_UsesHeuristics_NotLiteralInstallFilename()
    {
        await WithArchive([("data/vehicles/car/skin.dds", "mod"),
            ("modinfo.json", """{"files":{"data/vehicles/car/skin.dds":"install"}}""")],
            async (zip, fs, engine) =>
            {
                var result = await engine.InstallAsync(zip, DataRoot, "Marker", NoAmbiguous);
                Assert.Equal(1, result.FilesInstalled);
                Assert.Equal("mod", fs.ReadAllText(Target));
                Assert.False(fs.FileExists(Path.Combine(DataRoot, "install")));
            });
    }

    [Fact]
    public async Task NestedModinfoNamedGameAsset_IsNotMistakenForRootMetadata()
    {
        await WithArchive([("data/config/modinfo.json", "game-asset"),
            ("modinfo.json", """{"files":{"data/config/modinfo.json":"config/modinfo.json"}}""")],
            async (zip, fs, engine) =>
            {
                var result = await engine.InstallAsync(zip, DataRoot, "Nested JSON", NoAmbiguous);
                Assert.Equal(1, result.FilesInstalled);
                Assert.Equal("game-asset", fs.ReadAllText(Path.Combine(DataRoot, "config", "modinfo.json")));
            });
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{\"files\":null}")]
    [InlineData("{\"files\":{\"skin.dds\":null}}")]
    [InlineData("{\"schemaVersion\":99}")]
    public async Task InvalidManifest_RejectsRatherThanFallingBackSilently(string json)
    {
        await WithArchive([("skin.dds", "mod"), ("modinfo.json", json)], async (zip, fs, engine) =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => engine.InstallAsync(zip, DataRoot, "Bad Metadata", NoAmbiguous));
            Assert.Equal("original", fs.ReadAllText(Target));
            Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        });
    }

    [Theory]
    [InlineData("../escape.dds")]
    [InlineData("/absolute.dds")]
    [InlineData("data/../escape.dds")]
    [InlineData("data/skin.dds:stream")]
    [InlineData("C:/escape.dds")]
    public async Task UnsafeArchivePath_RejectsBeforeExtraction(string entry)
    {
        await WithArchive([(entry, "bad")], async (zip, fs, engine) =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => engine.PrepareInstallAsync(zip, DataRoot, "Unsafe", NoAmbiguous));
            Assert.Equal("original", fs.ReadAllText(Target));
            Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        });
    }

    [Fact]
    public async Task CaseCollidingZipEntries_RejectBeforeExtraction()
    {
        await WithArchive([("data/skin.dds", "a"), ("DATA/SKIN.dds", "b")], async (zip, fs, engine) =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => engine.PrepareInstallAsync(zip, DataRoot, "Duplicate", NoAmbiguous));
            Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        });
    }

    [Theory]
    [InlineData("../escape.dds")]
    [InlineData("/absolute.dds")]
    [InlineData("vehicles/../escape.dds")]
    [InlineData("skin.dds:stream")]
    public async Task UnsafeManifestTarget_IsNotNormalizedIntoAnAcceptedPath(string target)
    {
        string metadata = System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = 1, files = new Dictionary<string, string> { ["skin.dds"] = target } });
        await WithArchive([("skin.dds", "bad"), ("modinfo.json", metadata)], async (zip, fs, engine) =>
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => engine.PrepareInstallAsync(zip, DataRoot, "Unsafe", NoAmbiguous));
            Assert.Equal("original", fs.ReadAllText(Target));
        });
    }

    [Fact]
    public async Task ArbitraryScript_ExplicitMappingCannotInstallOrRunIt()
    {
        await WithArchive([("install.ps1", "throw 'must never execute'"),
            ("modinfo.json", """{"files":{"install.ps1":"vehicles/car/install.ps1"}}""")], async (zip, fs, engine) =>
        {
            await Assert.ThrowsAsync<UnsupportedModPackageException>(() => engine.InstallAsync(zip, DataRoot, "Unsafe", NoAmbiguous));
            Assert.Equal("original", fs.ReadAllText(Target));
            Assert.False(fs.FileExists(Path.Combine(DataRoot, "vehicles", "car", "install.ps1")));
        });
    }

    private static async Task WithArchive((string Name, string Content)[] entries,
        Func<string, FakeFileSystem, Core.SyncEngine.SyncEngine, Task> test)
    {
        string zip = Path.GetFullPath($"zip-fixture-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                foreach (var (name, content) in entries)
                {
                    using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                    writer.Write(content);
                }
            var fs = new FakeFileSystem().AddFile(zip).AddFile(Target, "original");
            var engine = new Core.SyncEngine.SyncEngine(new ZipService(fs), new MappingResolver(),
                new ManifestStore(fs, @"C:\ZipFixtureManifest\manifest.json"), new BackupService(fs),
                fs, TimeProvider.System, new FakeFileHasher());
            await test(zip, fs, engine);
        }
        finally { File.Delete(zip); }
    }

    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        throw new InvalidOperationException("Unexpected ambiguous mapping.");
}

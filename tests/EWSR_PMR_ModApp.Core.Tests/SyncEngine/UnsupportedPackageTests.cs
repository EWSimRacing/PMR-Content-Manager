using System.IO.Compression;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class UnsupportedPackageTests
{
    private const string DataRoot = @"C:\FakePMR\data";
    private const string ManifestPath = @"C:\FakePMRAppData\manifest.json";
    private const string GameAsset = @"C:\FakePMR\data\vehicles\Acura_ARX-06_2023\data\Acura_ARX-06_2023_audioConfig.xml";
    private static readonly string[] ScriptNames =
    [
        "Apply_Cockpit_Immersion_Audio.ps1",
        "Restore_Cockpit_Immersion_Audio.ps1",
        "Run_Apply_Cockpit_Immersion_Audio.bat",
        "Run_Restore_Cockpit_Immersion_Audio.bat"
    ];
    private static readonly string[] ArchiveNames = [.. ScriptNames, "README.md", "CHANGELOG.md"];

    [Fact]
    public async Task UnknownScriptArchive_RejectsScriptsWithActionableReason_AndNoGameWrites()
    {
        string zipPath = CreateArchive(ArchiveNames);
        try
        {
            var (engine, zipService, fs) = BuildEngine(zipPath);
            var staged = await zipService.StageAsync(zipPath);
            try
            {
                Assert.Null(staged.ModInfo);
                Assert.Empty(staged.Entries);
                Assert.Empty(staged.HookScripts);
                Assert.Equal(ScriptNames.Order(), staged.SkippedFiles.Select(f => f.PathInZip).Order());
                Assert.All(staged.SkippedFiles, f => Assert.Equal(SkipCategory.UnsafeFile, f.Category));
                Assert.Equal(new[] { "CHANGELOG.md", "README.md" },
                    staged.DisplayFiles.Select(f => f.FullNameInZip).Order());
                var mapping = new MappingResolver().Resolve(
                    staged.Entries, DataRoot, ["vehicles/Acura_ARX-06_2023/data/Acura_ARX-06_2023_audioConfig.xml"]);
                Assert.Empty(mapping.Mapped);
                Assert.Empty(mapping.Unmatched);
            }
            finally
            {
                zipService.CleanupStaging(staged.StagingDirectory);
            }

            var ex = await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
                engine.InstallAsync(zipPath, DataRoot, "Cockpit Immersion Audio", NoAmbiguous));

            Assert.Contains("no installable game assets", ex.Message);
            Assert.Contains("does not automatically run", ex.Message);
            Assert.Contains("CM-compatible ZIP", ex.Message);
            Assert.Contains("Changing the game path", ex.Message);
            Assert.All(ScriptNames, name => Assert.Contains(name, ex.Message));
            AssertUnchanged(fs);
            Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Theory]
    [InlineData("data/vehicles/Acura_ARX-06_2023/data/Acura_ARX-06_2023_audioConfig.xml")]
    [InlineData("data/vehicles/_shared/audio/default_audioConfig.xml")]
    public async Task ScriptsAlongsideGameAsset_OnlyAssetIsPrepared_WithoutExecutingScripts(string assetPath)
    {
        string zipPath = CreateArchive([.. ArchiveNames, assetPath]);
        var (engine, _, fs) = BuildEngine(zipPath);
        try
        {
            var plan = await engine.PrepareInstallAsync(zipPath, DataRoot, "Audio", NoAmbiguous);
            try
            {
                var copy = Assert.Single(plan.FilesToCopy);
                Assert.Equal(assetPath["data/".Length..], copy.RelativeTargetPath);
                Assert.Single(plan.FilesToBackup);
                Assert.Null(plan.PostInstallHook);
                Assert.Null(plan.PostUninstallHook);
                AssertUnchanged(fs);
            }
            finally
            {
                engine.CleanupInstallPlan(plan);
            }
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Theory]
    [InlineData("Apply_Cockpit_Immersion_Audio.ps1")]
    [InlineData("data/Apply_Cockpit_Immersion_Audio.ps1")]
    [InlineData("Run_Apply_Cockpit_Immersion_Audio.bat")]
    [InlineData("data/Run_Apply_Cockpit_Immersion_Audio.bat")]
    public void ScriptFileMapping_DoesNotBypassUnsafeExtensionGuard(string path)
    {
        var entry = new ZipEntryInfo { FullNameInZip = path, StagedFilePath = @"C:\fake\script" };
        var modInfo = new ModInfo { Files = new() { [path] = "vehicles/car/car_audioConfig.xml" } };

        Assert.Equal(SkipCategory.UnsafeFile, FileClassifier.Classify(entry, modInfo));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("unknown.payload")]
    public async Task NoScripts_ReportsUnsupportedMappingFailureWithoutWriting(string entryName)
    {
        string zipPath = CreateArchive([entryName]);
        var (engine, _, fs) = BuildEngine(zipPath);
        try
        {
            var ex = await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
                engine.PrepareInstallAsync(zipPath, DataRoot, "Other", NoAmbiguous));
            Assert.StartsWith("No files could be mapped to the game data directory.", ex.Message);
            AssertUnchanged(fs);
            Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    private static (Core.SyncEngine.SyncEngine Engine, ZipService ZipService, FakeFileSystem Fs)
        BuildEngine(string zipPath)
    {
        var fs = new FakeFileSystem().AddFile(zipPath).AddFile(GameAsset, "original");
        var zipService = new ZipService(fs);
        var engine = new Core.SyncEngine.SyncEngine(
            zipService, new MappingResolver(), new ManifestStore(fs, ManifestPath),
            new NoOpBackupService(), fs, TimeProvider.System, new FakeFileHasher());
        return (engine, zipService, fs);
    }

    private static void AssertUnchanged(FakeFileSystem fs)
    {
        Assert.Equal("original", fs.ReadAllText(GameAsset));
        Assert.Equal(new[] { GameAsset }, fs.EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories));
        Assert.False(fs.FileExists(ManifestPath));
        Assert.False(fs.DirectoryExists(AppPaths.BackupsRoot));
        Assert.False(fs.DirectoryExists(AppPaths.ModsCache));
        Assert.False(fs.DirectoryExists(Path.Combine(AppPaths.AppDataRoot, "scripts")));
    }

    private static string CreateArchive(IEnumerable<string> names)
    {
        string path = Path.GetFullPath($"unsupported-package-{Guid.NewGuid():N}.zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (string name in names)
        {
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write("fixture content; never executed");
        }
        return path;
    }

    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        throw new InvalidOperationException("Unexpected ambiguous mapping callback.");
}

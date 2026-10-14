using System.Text;
using System.Text.Json;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class InstallRollbackTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    public async Task DirectInstall_LockedSecondTarget_RollsBack_RetryAndUninstallKeepTrueOriginals(
        bool nativeAudio, bool firstIsNew, bool blockRollback)
    {
        string root = Path.GetFullPath($"core-rollback-fixture-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "game", "data");
        string firstRelative = "vehicles/01/first_audioConfig.xml";
        string secondRelative = "vehicles/02/second_audioConfig.xml";
        string first = Path.Combine(data, firstRelative.Replace('/', '\\'));
        string second = Path.Combine(data, secondRelative.Replace('/', '\\'));
        string staging = Path.Combine(root, "staging");
        string manifestPath = Path.Combine(root, "manifest.json");
        byte[] original = [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes("<audioConfig name=\"properties\">\r\n<!-- original -->\r\n</audioConfig>")];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(first)!);
            Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            Directory.CreateDirectory(staging);
            if (!firstIsNew) File.WriteAllBytes(first, original);
            File.WriteAllBytes(second, original);
            string payload = Path.Combine(staging, "replacement.xml");
            File.WriteAllText(payload, "new XML payload");
            var fs = new ProjectFixtureFileSystem(root);
            var store = new ManifestStore(fs, manifestPath);
            var backup = new BackupService(fs);
            var zip = new StubZipService(new ZipStagingResult
            {
                ZipHash = nativeAudio ? CockpitImmersionAudio.ArchiveHash : "ordinary",
                StagingDirectory = staging,
                Entries = nativeAudio ? [] :
                [
                    new ZipEntryInfo { FullNameInZip = "data/" + firstRelative, StagedFilePath = payload },
                    new ZipEntryInfo { FullNameInZip = "data/" + secondRelative, StagedFilePath = payload }
                ]
            });
            var engine = new Core.SyncEngine.SyncEngine(zip, new MappingResolver(), store, backup, fs,
                TimeProvider.System, new RealFileHasher());
            var plan = await engine.PrepareInstallAsync("fixture.zip", data, "Fixture", NoAmbiguous);
            FileStream? firstLock = null;
            using (var held = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try
                {
                    var error = await Assert.ThrowsAsync<IOException>(() => engine.ExecuteInstallAsync(plan,
                        new InlineProgress(p =>
                        {
                            if (blockRollback && p.Phase == "Installing files" && p.PercentComplete == 65)
                                firstLock = File.Open(first, FileMode.Open, FileAccess.Read, FileShare.Read);
                        })));
                    if (blockRollback)
                    {
                        Assert.Contains("Rollback failed", error.Message);
                        Assert.IsAssignableFrom<IOException>(error.InnerException);
                        Assert.Contains("second_audioConfig.xml", error.InnerException!.Message);
                        Assert.NotEqual(original, File.ReadAllBytes(first));
                    }
                    else if (firstIsNew) Assert.False(File.Exists(first));
                    else Assert.Equal(original, File.ReadAllBytes(first));
                }
                finally { firstLock?.Dispose(); }
                Assert.Equal(original, File.ReadAllBytes(second));
                Assert.Empty((await store.LoadAsync()).Mods);
                if (!firstIsNew)
                    Assert.Equal(original, File.ReadAllBytes(fs.Resolve(backup.GetBackupPath(plan.ModId, firstRelative)!)));
                Assert.Equal(original, File.ReadAllBytes(fs.Resolve(backup.GetBackupPath(plan.ModId, secondRelative)!)));
                string recoveryRoot = Path.Combine(AppPaths.AppDataRoot, "write-recovery");
                if (blockRollback)
                {
                    string recovery = Assert.Single(fs.EnumerateDirectories(recoveryRoot));
                    using var journal = JsonDocument.Parse(fs.ReadAllText(Path.Combine(recovery, "journal.json")));
                    Assert.Equal("rollback-failed", journal.RootElement.GetProperty("State").GetString());
                    var snapshot = journal.RootElement.GetProperty("Files").EnumerateArray().Single(e =>
                        e.GetProperty("Target").GetString() == first);
                    string bytes = snapshot.GetProperty("Backup").GetString()!;
                    Assert.Equal(original, File.ReadAllBytes(fs.Resolve(bytes)));
                    fs.CopyFile(bytes, first, true);
                    fs.DeleteDirectory(recovery, true);
                }
                Assert.Empty(fs.EnumerateDirectories(recoveryRoot));
            }
            // Same prepared plan retry exercises preservation of already-created original backups.
            Assert.True((await engine.ExecuteInstallAsync(plan)).Success);
            var mod = Assert.Single((await store.LoadAsync()).Mods.Values);
            Assert.Equal(firstIsNew, mod.Files.Single(f => f.RelativeTargetPath == firstRelative).IsNewFile);
            if (!firstIsNew)
                Assert.All(mod.Files, f => Assert.Equal(HashHelper.ComputeBytesHash(original), f.OriginalFileHash));
            Assert.True((await engine.UninstallAsync(mod.ModId, data)).Success);
            if (firstIsNew) Assert.False(File.Exists(first));
            else Assert.Equal(original, File.ReadAllBytes(first));
            Assert.Equal(original, File.ReadAllBytes(second));
            Assert.Empty((await store.LoadAsync()).Mods);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectReapply_LockedSecondTarget_RestoresModifiedPreoperationState_NotOriginalBackup(bool firstIsNew)
    {
        string root = Path.GetFullPath($"core-reapply-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string first = Path.Combine(root, "first.xml");
        string second = Path.Combine(root, "second.xml");
        string source = Path.Combine(root, "source.xml");
        try
        {
            if (!firstIsNew) File.WriteAllText(first, "pre-operation mod state");
            File.WriteAllText(second, "second original");
            File.WriteAllText(source, "reapplied");
            var fs = new ProjectFixtureFileSystem(root);
            string backup = Path.Combine(AppPaths.BackupDirForMod("fixture"), "__data__", "first.xml");
            fs.CreateDirectory(Path.GetDirectoryName(backup)!);
            fs.WriteAllText(backup, "true vanilla");
            var engine = new Core.SyncEngine.SyncEngine(new StubZipService(new ZipStagingResult
            {
                ZipHash = "fixture", StagingDirectory = root, Entries = []
            }), new MappingResolver(), new ManifestStore(fs, Path.Combine(root, "manifest.json")),
                new BackupService(fs), fs, TimeProvider.System, new RealFileHasher());
            var plan = new ReapplyPlan
            {
                DataRoot = root, GameRoot = root,
                ModsToReapply = [new ModReapplyItem
                {
                    ModId = "fixture", ModName = "Fixture", FilesToCopy =
                    [
                        new FileCopySpec { SourcePath = source, RelativeTargetPath = "first.xml" },
                        new FileCopySpec { SourcePath = source, RelativeTargetPath = "second.xml" }
                    ]
                }]
            };
            using var held = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read);
            var result = await engine.ExecuteReapplyAsync(plan);
            Assert.False(result.Success);
            Assert.Equal(0, result.FilesReapplied);
            Assert.Equal(0, result.ModsReapplied);
            if (firstIsNew) Assert.False(File.Exists(first));
            else Assert.Equal("pre-operation mod state", File.ReadAllText(first));
            Assert.Equal("second original", File.ReadAllText(second));
            Assert.Equal("true vanilla", fs.ReadAllText(backup));
        }
        finally { Directory.Delete(root, true); }
    }

    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        throw new InvalidOperationException("Fixture must use path overlay.");

    private sealed class InlineProgress(Action<SyncProgress> report) : IProgress<SyncProgress>
    {
        public void Report(SyncProgress value) => report(value);
    }
}

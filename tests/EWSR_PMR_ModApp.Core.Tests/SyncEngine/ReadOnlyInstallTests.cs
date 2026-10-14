using System.Text;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class ReadOnlyInstallTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeAudio_InstallReapplyUninstall_PreservesOriginalReadOnlyMetadata(bool failSecondWrite)
    {
        string root = Path.GetFullPath($"readonly-core-fixture-{Guid.NewGuid():N}");
        string data = Path.Combine(root, "game", "data");
        string firstRelative = "vehicles/01/first_audioConfig.xml", secondRelative = "vehicles/02/second_audioConfig.xml";
        string first = Path.Combine(data, firstRelative.Replace('/', '\\'));
        string second = Path.Combine(data, secondRelative.Replace('/', '\\'));
        var originalAttributes = FileAttributes.ReadOnly | FileAttributes.Archive;
        byte[] original = [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes("<audioConfig name=\"properties\"/>")];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(first)!);
            Directory.CreateDirectory(Path.GetDirectoryName(second)!);
            File.WriteAllBytes(first, original);
            File.WriteAllBytes(second, original);
            File.SetAttributes(first, originalAttributes);
            File.SetAttributes(second, originalAttributes);
            var fs = new ProjectFixtureFileSystem(root);
            var store = new ManifestStore(fs, Path.Combine(root, "manifest.json"));
            var backup = new BackupService(fs);
            var engine = new Core.SyncEngine.SyncEngine(new StubZipService(new ZipStagingResult
            {
                StagingDirectory = Path.Combine(root, "staging"), Entries = [], ZipHash = CockpitImmersionAudio.ArchiveHash
            }), new MappingResolver(), store, backup, fs, TimeProvider.System, new Hasher(fs));
            var plan = await engine.PrepareInstallAsync("reviewed.zip", data, "Audio", NoAmbiguous);
            if (failSecondWrite)
            {
                using (var held = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read))
                    await Assert.ThrowsAnyAsync<IOException>(() => engine.ExecuteInstallAsync(plan));
                Assert.Empty((await store.LoadAsync()).Mods);
                Assert.Equal(original, File.ReadAllBytes(first));
                Assert.Equal(original, File.ReadAllBytes(second));
                Assert.Equal(originalAttributes, File.GetAttributes(first));
                Assert.Equal(originalAttributes, File.GetAttributes(second));
                Assert.Empty(fs.EnumerateDirectories(Path.Combine(AppPaths.AppDataRoot, "write-recovery")));
            }
            var result = await engine.ExecuteInstallAsync(plan);
            Assert.True(result.Success);
            Assert.NotEqual(original, File.ReadAllBytes(first));
            Assert.Equal(originalAttributes, File.GetAttributes(first));
            Assert.Equal(originalAttributes, File.GetAttributes(second));
            string saved = backup.GetBackupPath(plan.ModId, firstRelative)!;
            Assert.Equal(originalAttributes, fs.GetFileAttributes(saved));
            Assert.Equal(original, File.ReadAllBytes(fs.Resolve(saved)));

            // The engine's ordinary reapply transaction shares the same attribute-safe copying.
            fs.CopyFile(saved, first, true, restoreSourceAttributes: true);
            Assert.True((await engine.ReapplyRevertedModsAsync(data)).Success);
            Assert.Equal(originalAttributes, File.GetAttributes(first));
            File.SetAttributes(first, FileAttributes.Normal);
            Assert.True((await engine.UninstallAsync(plan.ModId, data)).Success);
            Assert.Equal(original, File.ReadAllBytes(first));
            Assert.Equal(original, File.ReadAllBytes(second));
            Assert.Equal(originalAttributes, File.GetAttributes(first));
            Assert.Equal(originalAttributes, File.GetAttributes(second));
            Assert.Empty((await store.LoadAsync()).Mods);
            Assert.False(fs.DirectoryExists(AppPaths.BackupDirForMod(plan.ModId)));
            Assert.Empty(fs.EnumerateDirectories(Path.Combine(AppPaths.AppDataRoot, "write-recovery")));
        }
        finally { if (Directory.Exists(root)) ManagedFileOperations.DeleteDirectory(root, true); }
    }

    private sealed class Hasher(IFileSystem fs) : IFileHasher
    {
        public string ComputeHash(string path)
        {
            using var input = fs.OpenRead(path);
            return HashHelper.ComputeStreamHash(input);
        }
    }
    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        throw new InvalidOperationException("Native paths are deterministic.");
}

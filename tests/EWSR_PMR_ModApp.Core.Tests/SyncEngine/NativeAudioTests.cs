using System.Text;
using System.Xml.Linq;
using System.IO.Compression;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Backup;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;
using EWSR_PMR_ModApp.Core.ZipHandling;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.SyncEngine;

public class NativeAudioTests
{
    private const string DataRoot = @"C:\FixturePMR\data";
    private const string Car = @"C:\FixturePMR\data\vehicles\Acura_ARX-06_2023\data\Acura_ARX-06_2023_audioConfig.xml";
    private const string Shared = @"C:\FixturePMR\data\vehicles\_shared\audio\default_audioConfig.xml";
    private const string Manifest = @"C:\FixtureManifest\manifest.json";
    private const string CarXml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n<audioConfig name=\"properties\">\r\n    <!-- unrelated -->\r\n    <enginePitchOffset value=\"-52.0\"/>\r\n    <interiorReverbMix value=\"0.38\" extra=\"keep\"/>\r\n    <rtmEngineVolume value=\"-4.7\"/>\r\n    <rtmExhaustVolume value=\"2.6\"/>\r\n    <rtmInteriorFXRattlesVolume value=\"-2.0\"/>\r\n    <rtmTransmissionVolume value=\"-1.4\"/>\r\n</audioConfig>";
    private static readonly Dictionary<string, string> Values = new()
    {
        ["interiorReverbMix"] = "0.24", ["rtmEngineVolume"] = "6.2",
        ["rtmExhaustVolume"] = "3.2", ["rtmTransmissionVolume"] = "4.2",
        ["rtmInteriorFXWindVolume"] = "0.95", ["rtmInteriorFXRattlesVolume"] = "-3.0"
    };

    [ArchiveFact]
    public async Task ActualArchive_ManagedInstallReapplyAndUninstall_RestoreExactOriginalBytes()
    {
        string archive = Environment.GetEnvironmentVariable("PMR_CM_SCRIPT_ARCHIVE")!;
        var fs = Fixture().AddFile(archive);
        var zip = new ZipService(fs);
        Assert.True(await zip.ValidateIntegrityAsync(archive));
        await ExerciseLifecycle(Build(fs, zip), fs, archive);
        Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        Assert.False(fs.DirectoryExists(Path.Combine(AppPaths.AppDataRoot, "scripts")));

        string tampered = Path.GetFullPath($"tampered-audio-{Guid.NewGuid():N}.zip");
        try
        {
            using (var original = ZipFile.OpenRead(archive))
            using (var copy = ZipFile.Open(tampered, ZipArchiveMode.Create))
                foreach (var entry in original.Entries)
                {
                    using var input = entry.Open();
                    using var output = copy.CreateEntry(entry.FullName).Open();
                    input.CopyTo(output);
                    if (entry.FullName == "Apply_Cockpit_Immersion_Audio.ps1")
                        output.WriteByte(0x20);
                }
            var rejectedFs = Fixture().AddFile(tampered);
            await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
                Build(rejectedFs, new ZipService(rejectedFs)).InstallAsync(tampered, DataRoot, "Tampered", NoAmbiguous));
            Assert.Equal(Encoding.UTF8.GetBytes(CarXml), ReadBytes(rejectedFs, Car));
            Assert.False(rejectedFs.FileExists(Manifest));
        }
        finally { File.Delete(tampered); }
    }

    [ArchiveFact]
    public async Task ActualArchive_PhysicalFixture_ManagedBackupManifestUninstallAreByteExact()
    {
        string fixture = Path.GetFullPath($"audio-physical-fixture-{Guid.NewGuid():N}");
        string data = Path.Combine(fixture, "game", "data");
        string car = Path.Combine(data, "vehicles", "Acura_ARX-06_2023", "data", "Acura_ARX-06_2023_audioConfig.xml");
        string shared = Path.Combine(data, "vehicles", "_shared", "audio", "default_audioConfig.xml");
        string manifestPath = Path.Combine(fixture, "manifest.json");
        byte[] carOriginal = Encoding.UTF8.GetBytes(CarXml);
        byte[] sharedOriginal = [.. new UTF8Encoding(true).GetPreamble(), .. Encoding.UTF8.GetBytes("<audioConfig name=\"properties\"/>")];
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(car)!);
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            File.WriteAllBytes(car, carOriginal);
            File.WriteAllBytes(shared, sharedOriginal);
            var fs = new ProjectFixtureFileSystem(fixture);
            var store = new ManifestStore(fs, manifestPath);
            var backup = new BackupService(fs);
            var engine = new Core.SyncEngine.SyncEngine(new ZipService(fs), new MappingResolver(), store,
                backup, fs, TimeProvider.System, new FsHasher(fs));
            var installed = await engine.InstallAsync(Environment.GetEnvironmentVariable("PMR_CM_SCRIPT_ARCHIVE")!,
                data, "Actual Audio ZIP", NoAmbiguous);
            Assert.Equal(2, installed.FilesInstalled);
            AssertPatched(File.ReadAllBytes(car), true);
            AssertPatched(File.ReadAllBytes(shared), false);
            Assert.True(File.Exists(manifestPath));
            var mod = Assert.Single((await store.LoadAsync()).Mods.Values);
            var carRecord = mod.Files.Single(f => f.RelativeTargetPath.Contains("Acura"));
            Assert.Equal(carOriginal, File.ReadAllBytes(fs.Resolve(backup.GetBackupPath(mod.ModId, carRecord.RelativeTargetPath)!)));
            File.WriteAllBytes(car, carOriginal);
            Assert.Equal(1, (await engine.ReapplyRevertedModsAsync(data)).FilesReapplied);
            byte[] updated = Encoding.UTF8.GetBytes(CarXml.Replace("-52.0", "-51.0"));
            File.WriteAllBytes(car, updated);
            Assert.NotEmpty((await engine.PrepareReapplyAsync(data)).Warnings);
            Assert.Equal(updated, File.ReadAllBytes(car));
            Assert.True((await engine.UninstallAsync(mod.ModId, data)).Success);
            Assert.Equal(carOriginal, File.ReadAllBytes(car));
            Assert.Equal(sharedOriginal, File.ReadAllBytes(shared));
            Assert.Empty((await store.LoadAsync()).Mods);
            Assert.Empty(fs.EnumerateDirectories(AppPaths.StagingRoot));
        }
        finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true); }
    }

    [Fact]
    public async Task NativePreparation_ManagedLifecycle_UsesSyntheticCarAndSharedShapes()
    {
        var fs = Fixture();
        var zip = new StubZipService(new ZipStagingResult
        {
            StagingDirectory = @"C:\FixtureStaging", Entries = [],
            ZipHash = CockpitImmersionAudio.ArchiveHash
        });
        await ExerciseLifecycle(Build(fs, zip), fs, "reviewed.zip");
    }

    [Fact]
    public async Task NativeAdapter_NeverCarriesArchiveHooksIntoExecution()
    {
        var zip = new StubZipService(new ZipStagingResult
        {
            StagingDirectory = @"C:\FixtureStaging", Entries = [], ZipHash = CockpitImmersionAudio.ArchiveHash,
            ModInfo = new ModInfo { Hooks = new ModHooks { PostInstall = new HookScript { Script = "untrusted.ps1" } } },
            HookScripts = [new ZipEntryInfo { FullNameInZip = "untrusted.ps1", StagedFilePath = @"C:\FixtureStaging\untrusted.ps1" }]
        });
        var plan = await Build(Fixture(), zip).PrepareInstallAsync("reviewed.zip", DataRoot, "Audio", NoAmbiguous);
        Assert.Null(plan.PostInstallHook);
        Assert.Null(plan.PostUninstallHook);
    }

    private static async Task ExerciseLifecycle(Core.SyncEngine.SyncEngine engine, FakeFileSystem fs, string archive)
    {
        byte[] carOriginal = ReadBytes(fs, Car);
        byte[] sharedOriginal = ReadBytes(fs, Shared);
        var result = await engine.InstallAsync(archive, DataRoot, "Cockpit Immersion Audio", NoAmbiguous);
        Assert.True(result.Success);
        Assert.Equal(2, result.FilesInstalled);
        AssertPatched(ReadBytes(fs, Car), hasUnrelated: true);
        AssertPatched(ReadBytes(fs, Shared), hasUnrelated: false);
        var store = new ManifestStore(fs, Manifest);
        var mod = Assert.Single((await store.LoadAsync()).Mods.Values);
        Assert.Equal(CockpitImmersionAudio.PatchId, mod.NativePatchId);
        Assert.Null(mod.Hooks);
        Assert.All(mod.Files, f => Assert.False(f.IsNewFile));
        var backups = new BackupService(fs);
        Assert.Equal(carOriginal, ReadBytes(fs, backups.GetBackupPath(mod.ModId, mod.Files.Single(f => f.RelativeTargetPath.Contains("Acura")).RelativeTargetPath)!));
        Assert.Equal("unrelated", fs.ReadAllText(Path.Combine(DataRoot, "vehicles", "other.xml")));

        // Exact vanilla reversal can safely reuse the reviewed generated payload.
        fs.AddBytes(Car, carOriginal);
        var reapplied = await engine.ReapplyRevertedModsAsync(DataRoot);
        Assert.True(reapplied.Success);
        Assert.Equal(1, reapplied.FilesReapplied);
        AssertPatched(ReadBytes(fs, Car), true);

        // A new vanilla baseline must never be replaced by the stale cached whole file.
        byte[] updated = Encoding.UTF8.GetBytes(CarXml.Replace("-52.0", "-51.0"));
        fs.AddBytes(Car, updated);
        var held = await engine.PrepareReapplyAsync(DataRoot);
        Assert.Empty(held.ModsToReapply);
        Assert.Contains(held.Warnings, w => w.Contains("baseline changed"));
        Assert.Equal(updated, ReadBytes(fs, Car));
        Assert.Equal(carOriginal, ReadBytes(fs, backups.GetBackupPath(mod.ModId, mod.Files.Single(f => f.RelativeTargetPath.Contains("Acura")).RelativeTargetPath)!));

        // Uninstall follows existing policy: restore the pre-install original, byte-for-byte.
        Assert.True((await engine.UninstallAsync(mod.ModId, DataRoot)).Success);
        Assert.Equal(carOriginal, ReadBytes(fs, Car));
        Assert.Equal(sharedOriginal, ReadBytes(fs, Shared));
        Assert.Empty((await store.LoadAsync()).Mods);
        Assert.False(fs.DirectoryExists(AppPaths.BackupDirForMod(mod.ModId)));
        Assert.False(fs.DirectoryExists(AppPaths.PayloadDirForMod(mod.ModId)));
    }

    [Fact]
    public async Task BaselineChangedAfterPreparation_RefusesBeforeBackupOrWrites()
    {
        var fs = Fixture();
        var engine = Build(fs, NativeStub());
        var plan = await engine.PrepareInstallAsync("reviewed.zip", DataRoot, "Audio", NoAmbiguous);
        fs.AddBytes(Car, Encoding.UTF8.GetBytes(CarXml.Replace("-52.0", "-50.0")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.ExecuteInstallAsync(plan));
        Assert.False(fs.DirectoryExists(AppPaths.BackupDirForMod(plan.ModId)));
        Assert.False(fs.FileExists(Manifest));
        Assert.Equal("<audioConfig name=\"properties\">\r\n</audioConfig>", fs.ReadAllText(Shared));
    }

    [Fact]
    public async Task InvalidAudioAsset_AbortsEntirePreparationWithoutGameChanges()
    {
        var fs = Fixture().AddFile(Shared, "<notAudio/>");
        await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
            Build(fs, NativeStub()).PrepareInstallAsync("reviewed.zip", DataRoot, "Audio", NoAmbiguous));
        Assert.Equal(Encoding.UTF8.GetBytes(CarXml), ReadBytes(fs, Car));
        Assert.False(fs.FileExists(Manifest));
    }

    [Fact]
    public async Task DuplicateNativeInstallation_IsRejectedWithoutLosingOriginalBackups()
    {
        var fs = Fixture();
        var engine = Build(fs, NativeStub());
        await engine.InstallAsync("reviewed.zip", DataRoot, "Audio", NoAmbiguous);
        await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
            engine.PrepareInstallAsync("reviewed.zip", DataRoot, "Audio again", NoAmbiguous));
        Assert.Single((await new ManifestStore(fs, Manifest).LoadAsync()).Mods);
    }

    [Fact]
    public async Task TamperedCache_AndMissingBaseline_AreHeldWithoutWrites()
    {
        var fs = Fixture();
        var engine = Build(fs, NativeStub());
        var install = await engine.InstallAsync("reviewed.zip", DataRoot, "Audio", NoAmbiguous);
        var mod = (await new ManifestStore(fs, Manifest).LoadAsync()).Mods[install.ModId!];
        var car = mod.Files.Single(f => f.RelativeTargetPath.Contains("Acura"));
        fs.AddFile(Path.Combine(AppPaths.PayloadDirForMod(mod.ModId), car.SourcePathInZip.Replace('/', '\\')), "tampered");
        fs.AddBytes(Car, Encoding.UTF8.GetBytes(CarXml));
        fs.DeleteFile(Shared);
        var plan = await engine.PrepareReapplyAsync(DataRoot);
        Assert.Empty(plan.ModsToReapply);
        Assert.Equal(2, plan.Warnings.Count);
        Assert.Equal(Encoding.UTF8.GetBytes(CarXml), ReadBytes(fs, Car));
        Assert.False(fs.FileExists(Shared));
    }

    [Fact]
    public async Task OtherModOwnership_IsRejectedBeforeAudioInstallation()
    {
        var fs = Fixture();
        await new ManifestStore(fs, Manifest).AddOrUpdateModAsync(new ModEntry
        {
            ModId = "other", ModName = "Other", SourceZipHash = "other", InstallTimestamp = DateTimeOffset.UtcNow,
            Files = [new InstalledFileEntry
            {
                RelativeTargetPath = "vehicles/Acura_ARX-06_2023/data/Acura_ARX-06_2023_audioConfig.xml",
                SourcePathInZip = "audio.xml", MappingMethod = MappingMethod.PathOverlay,
                InstalledFileHash = "other"
            }]
        });
        await Assert.ThrowsAsync<UnsupportedModPackageException>(() =>
            Build(fs, NativeStub()).PrepareInstallAsync("reviewed.zip", DataRoot, "Audio", NoAmbiguous));
        Assert.Equal(Encoding.UTF8.GetBytes(CarXml), ReadBytes(fs, Car));
    }

    [ReadOnlyGameFact]
    public void ActualGameAudioXml_TransformsInMemoryOnly()
    {
        string data = Environment.GetEnvironmentVariable("PMR_CM_READONLY_GAME_DATA")!;
        var paths = Directory.GetFiles(Path.Combine(data, "vehicles"), "*audioConfig.xml", SearchOption.AllDirectories);
        Assert.NotEmpty(paths);
        foreach (string path in paths)
        {
            byte[] original = File.ReadAllBytes(path);
            byte[] transformed = CockpitImmersionAudio.Transform(original);
            using var stream = new MemoryStream(transformed);
            var root = XDocument.Load(stream).Root!;
            foreach (var (name, value) in Values)
                Assert.Equal(value, (string?)Assert.Single(root.Elements(name)).Attribute("value"));
            Assert.Equal(original, File.ReadAllBytes(path));
        }
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16")]
    [InlineData("utf16be")]
    public void Transformation_PreservesEncodingAndUnrelatedXml_AndIsIdempotent(string kind)
    {
        Encoding encoding = kind switch
        {
            "utf8bom" => new UTF8Encoding(true),
            "utf16" => new UnicodeEncoding(false, true),
            "utf16be" => new UnicodeEncoding(true, true),
            _ => new UTF8Encoding(false)
        };
        string declaration = kind.StartsWith("utf16") ? "utf-16" : "utf-8";
        byte[] original = [.. encoding.GetPreamble(), .. encoding.GetBytes(CarXml.Replace("utf-8", declaration))];
        byte[] patched = CockpitImmersionAudio.Transform(original);
        Assert.True(patched.AsSpan().StartsWith(encoding.GetPreamble()));
        AssertPatched(patched, true);
        Assert.Equal(patched, CockpitImmersionAudio.Transform(patched));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UndeclaredUtf16Encoding_PreservesBomAndDoesNotIntroduceDeclaration(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, true);
        byte[] original = [.. encoding.GetPreamble(), .. encoding.GetBytes("<audioConfig name=\"properties\"/>")];
        byte[] patched = CockpitImmersionAudio.Transform(original);
        Assert.True(patched.AsSpan().StartsWith(encoding.GetPreamble()));
        Assert.DoesNotContain("<?xml", encoding.GetString(patched));
        AssertPatched(patched, false);
    }

    [Theory]
    [InlineData("<other/>")]
    [InlineData("<audioConfig xmlns=\"future\"/>")]
    [InlineData("<audioConfig><rtmEngineVolume xmlns=\"future\"/></audioConfig>")]
    [InlineData("<audioConfig><rtmEngineVolume/><rtmEngineVolume/></audioConfig>")]
    [InlineData("<audioConfig><rtmEngineVolume><nested/></rtmEngineVolume></audioConfig>")]
    [InlineData("<!DOCTYPE audioConfig [<!ENTITY x SYSTEM 'file:///C:/private'>]><audioConfig>&x;</audioConfig>")]
    public void InvalidXml_IsRejected(string xml)
    {
        Assert.ThrowsAny<Exception>(() => CockpitImmersionAudio.Transform(Encoding.UTF8.GetBytes(xml)));
    }

    [Fact]
    public void Recognition_IsExactNotByFileNameOrScriptText()
    {
        Assert.True(CockpitImmersionAudio.Recognizes(CockpitImmersionAudio.ArchiveHash.ToLowerInvariant()));
        Assert.False(CockpitImmersionAudio.Recognizes(new string('0', 64)));
        Assert.False(CockpitImmersionAudio.Recognizes("Cockpit_Immersion_Audio_PMR_v1.0.3.zip"));
    }

    private static void AssertPatched(byte[] bytes, bool hasUnrelated)
    {
        using var source = new MemoryStream(bytes);
        var xml = XDocument.Load(source, LoadOptions.PreserveWhitespace);
        Assert.Equal("audioConfig", xml.Root!.Name.LocalName);
        Assert.Equal("properties", (string?)xml.Root.Attribute("name"));
        foreach (var (name, value) in Values)
            Assert.Equal(value, (string?)Assert.Single(xml.Root.Elements(name)).Attribute("value"));
        Assert.Single(xml.Root.Nodes().OfType<XComment>(), c => c.Value.Contains("Cockpit Immersion Audio"));
        if (hasUnrelated)
        {
            Assert.Equal("-52.0", (string?)xml.Root.Element("enginePitchOffset")!.Attribute("value"));
            Assert.Equal("keep", (string?)xml.Root.Element("interiorReverbMix")!.Attribute("extra"));
            Assert.Contains(xml.Root.Nodes().OfType<XComment>(), c => c.Value == " unrelated ");
            Assert.Equal(7, xml.Root.Elements().Count());
        }
        else Assert.Equal(6, xml.Root.Elements().Count());
    }

    private static StubZipService NativeStub() => new(new ZipStagingResult
    {
        StagingDirectory = @"C:\FixtureStaging", Entries = [], ZipHash = CockpitImmersionAudio.ArchiveHash
    });
    private static FakeFileSystem Fixture() => new FakeFileSystem()
        .AddBytes(Car, Encoding.UTF8.GetBytes(CarXml))
        .AddBytes(Shared, Encoding.UTF8.GetBytes("<audioConfig name=\"properties\">\r\n</audioConfig>"))
        .AddFile(Path.Combine(DataRoot, "vehicles", "other.xml"), "unrelated");
    private static Core.SyncEngine.SyncEngine Build(FakeFileSystem fs, IZipService zip) => new(
        zip, new MappingResolver(), new ManifestStore(fs, Manifest), new BackupService(fs),
        fs, TimeProvider.System, new FsHasher(fs));
    private static byte[] ReadBytes(FakeFileSystem fs, string path)
    {
        using var input = fs.OpenRead(path);
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return bytes.ToArray();
    }
    private sealed class FsHasher(IFileSystem fs) : IFileHasher
    {
        public string ComputeHash(string path)
        {
            using var stream = fs.OpenRead(path);
            return HashHelper.ComputeStreamHash(stream);
        }
    }
    private static Task<IReadOnlyList<ResolvedMapping>> NoAmbiguous(IReadOnlyList<AmbiguousMapping> _) =>
        throw new InvalidOperationException("Native audio must not use filename inference.");
}

public sealed class ArchiveFactAttribute : FactAttribute
{
    public ArchiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PMR_CM_SCRIPT_ARCHIVE")))
            Skip = "Set PMR_CM_SCRIPT_ARCHIVE to the reviewed ZIP for read-only integration validation.";
    }

}

public sealed class ReadOnlyGameFactAttribute : FactAttribute
{
    public ReadOnlyGameFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("PMR_CM_READONLY_GAME_DATA")))
            Skip = "Set PMR_CM_READONLY_GAME_DATA for explicitly authorized read-only XML compatibility validation.";
    }
}

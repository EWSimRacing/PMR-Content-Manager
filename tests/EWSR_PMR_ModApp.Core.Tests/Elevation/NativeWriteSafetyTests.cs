using System.Text.Json;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.Elevation;

public class NativeWriteSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedExecutor_PreflightRefusesChangedBaselineOrPayload_BeforeAnyWrites(bool changePayload)
    {
        string root = Path.GetFullPath($"native-write-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "replacement.xml");
        string target = Path.Combine(root, "audio.xml");
        string modId = Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(source, "replacement");
            File.WriteAllText(target, "original");
            var spec = new FileCopySpec
            {
                SourcePath = source, RelativeTargetPath = "audio.xml",
                ExpectedSourceHash = HashHelper.ComputeFileHash(source),
                ExpectedTargetHash = HashHelper.ComputeFileHash(target)
            };
            var roundTrip = JsonSerializer.Deserialize<FileCopySpec>(JsonSerializer.Serialize(spec))!;
            Assert.Equal(spec.ExpectedTargetHash, roundTrip.ExpectedTargetHash);
            Assert.Equal(spec.ExpectedSourceHash, roundTrip.ExpectedSourceHash);
            File.WriteAllText(changePayload ? source : target, "changed");
            var result = WritePlanExecutor.Execute(new WritePlanRequest
            {
                ModId = modId, DataRoot = root, GameRoot = root, Operation = WritePlanOperation.Install,
                FilesToCopy = [roundTrip], FilesToBackup = [new FileTargetSpec { RelativeTargetPath = "audio.xml" }]
            });
            Assert.False(result.Success);
            Assert.Equal(0, result.FilesCopied);
            Assert.Equal(0, result.FilesBackedUp);
            Assert.Equal(changePayload ? "original" : "changed", File.ReadAllText(target));
            Assert.False(Directory.Exists(AppPaths.BackupDirForMod(modId)));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SharedExecutor_UnchangedBaselineCopies_ChangedReapplyBaselineDoesNot()
    {
        string root = Path.GetFullPath($"native-write-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            string source = Path.Combine(root, "replacement.xml");
            string target = Path.Combine(root, "audio.xml");
            File.WriteAllText(source, "replacement");
            File.WriteAllText(target, "original");
            var request = new WritePlanRequest
            {
                ModId = Guid.NewGuid().ToString("N"), DataRoot = root, GameRoot = root,
                Operation = WritePlanOperation.Reapply,
                FilesToCopy = [new FileCopySpec
                {
                    SourcePath = source, RelativeTargetPath = "audio.xml",
                    ExpectedSourceHash = HashHelper.ComputeFileHash(source),
                    ExpectedTargetHash = HashHelper.ComputeFileHash(target)
                }]
            };
            Assert.True(WritePlanExecutor.Execute(request).Success);
            Assert.Equal("replacement", File.ReadAllText(target));
            File.WriteAllText(target, "new vanilla");
            Assert.False(WritePlanExecutor.Execute(request).Success);
            Assert.Equal("new vanilla", File.ReadAllText(target));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

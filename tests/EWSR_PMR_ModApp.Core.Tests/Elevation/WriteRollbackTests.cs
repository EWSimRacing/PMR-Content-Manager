using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using System.Text.Json;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.Elevation;

public class WriteRollbackTests
{
    [Theory]
    [InlineData(WritePlanOperation.Install, false)]
    [InlineData(WritePlanOperation.Install, true)]
    [InlineData(WritePlanOperation.Reapply, false)]
    [InlineData(WritePlanOperation.Reapply, true)]
    public void LockedSecondTarget_RestoresPreoperationState_AndRetryUninstallRestoresOriginals(
        WritePlanOperation operation, bool firstIsNew)
    {
        string root = Path.GetFullPath($"rollback-fixture-{Guid.NewGuid():N}");
        string modId = Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(root);
        byte[] firstOriginal = [0xff, 0, 1, 13, 10];
        byte[] secondOriginal = [0xfe, 0, 2, 10];
        string first = Path.Combine(root, "first.xml");
        string second = Path.Combine(root, "second.xml");
        string source = Path.Combine(root, "payload.xml");
        string backup = AppPaths.BackupDirForMod(modId);
        try
        {
            if (!firstIsNew) File.WriteAllBytes(first, firstOriginal);
            File.WriteAllBytes(second, secondOriginal);
            File.WriteAllText(source, "patched");
            if (operation == WritePlanOperation.Reapply && !firstIsNew)
            {
                Directory.CreateDirectory(Path.Combine(backup, "__data__"));
                File.WriteAllText(Path.Combine(backup, "__data__", "first.xml"), "true vanilla");
            }
            var request = new WritePlanRequest
            {
                Operation = operation, ModId = modId, DataRoot = root, GameRoot = root,
                FilesToCopy =
                [
                    new FileCopySpec
                    {
                        SourcePath = source, RelativeTargetPath = "first.xml",
                        ExpectedSourceHash = HashHelper.ComputeFileHash(source),
                        ExpectedTargetHash = firstIsNew ? null : HashHelper.ComputeBytesHash(firstOriginal)
                    },
                    new FileCopySpec
                    {
                        SourcePath = source, RelativeTargetPath = "second.xml",
                        ExpectedSourceHash = HashHelper.ComputeFileHash(source),
                        ExpectedTargetHash = HashHelper.ComputeBytesHash(secondOriginal)
                    }
                ],
                FilesToBackup = operation == WritePlanOperation.Install
                    ? [new FileTargetSpec { RelativeTargetPath = "first.xml" }, new FileTargetSpec { RelativeTargetPath = "second.xml" }]
                    : []
            };
            using (var held = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = WritePlanExecutor.Execute(request);
                Assert.False(result.Success);
                Assert.True(result.RollbackAttempted);
                Assert.True(result.RollbackSucceeded);
                Assert.Contains(result.Errors, e => e.RelativePath == "second.xml");
                Assert.Equal(0, result.FilesCopied);
                if (firstIsNew) Assert.False(File.Exists(first));
                else Assert.Equal(firstOriginal, File.ReadAllBytes(first));
                Assert.Equal(secondOriginal, File.ReadAllBytes(second));
            }
            Assert.True(WritePlanExecutor.Execute(request).Success);
            Assert.Equal("patched", File.ReadAllText(first));
            if (operation == WritePlanOperation.Install)
            {
                Assert.True(WritePlanExecutor.Execute(new WritePlanRequest
                {
                    Operation = WritePlanOperation.Uninstall, ModId = modId, DataRoot = root, GameRoot = root,
                    FilesToDelete = firstIsNew ? [new FileTargetSpec { RelativeTargetPath = "first.xml" }] : []
                }).Success);
                if (firstIsNew) Assert.False(File.Exists(first));
                else Assert.Equal(firstOriginal, File.ReadAllBytes(first));
                Assert.Equal(secondOriginal, File.ReadAllBytes(second));
            }
            else if (!firstIsNew)
                Assert.Equal("true vanilla", File.ReadAllText(Path.Combine(backup, "__data__", "first.xml")));
        }
        finally
        {
            Directory.Delete(root, true);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
    }

    [Fact]
    public void RollbackFailure_RetainsJournalAndBytes_WithoutMaskingCopyError()
    {
        string root = Path.GetFullPath($"rollback-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string first = Path.Combine(root, "first.xml");
        string second = Path.Combine(root, "second.xml");
        string source = Path.Combine(root, "payload.xml");
        string? recovery = null;
        FileStream? firstLock = null;
        try
        {
            File.WriteAllText(first, "first original");
            File.WriteAllText(second, "second original");
            File.WriteAllText(source, "modded");
            using (var secondLock = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var result = WritePlanExecutor.Execute(new WritePlanRequest
                {
                    Operation = WritePlanOperation.Reapply, ModId = Guid.NewGuid().ToString("N"),
                    DataRoot = root, GameRoot = root,
                    FilesToCopy =
                    [
                        new FileCopySpec { SourcePath = source, RelativeTargetPath = "first.xml" },
                        new FileCopySpec { SourcePath = source, RelativeTargetPath = "second.xml" }
                    ]
                }, line =>
                {
                    if (line.Contains("copied: first.xml"))
                        firstLock = File.Open(first, FileMode.Open, FileAccess.Read, FileShare.Read);
                });
                Assert.False(result.Success);
                Assert.True(result.RollbackAttempted);
                Assert.False(result.RollbackSucceeded);
                Assert.Contains(result.Errors, e => e.RelativePath == "second.xml" && !e.Message.Contains("Rollback"));
                Assert.Contains(result.Errors, e => e.RelativePath == first && e.Message.Contains("Rollback failed"));
                recovery = Assert.IsType<string>(result.RecoveryDirectory);
                using var journal = JsonDocument.Parse(File.ReadAllText(Path.Combine(recovery, "journal.json")));
                Assert.Equal("rollback-failed", journal.RootElement.GetProperty("State").GetString());
                Assert.Contains("second.xml", journal.RootElement.GetProperty("OriginalError").GetString()!);
                var entry = journal.RootElement.GetProperty("Files").EnumerateArray().Single(e =>
                    e.GetProperty("Target").GetString() == first);
                Assert.True(entry.GetProperty("Attempted").GetBoolean());
                Assert.Equal("first original", File.ReadAllText(entry.GetProperty("Backup").GetString()!));
            }
            firstLock!.Dispose();
            firstLock = null;
            // Prove the retained snapshot can recover the exact pre-operation bytes.
            File.Copy(Path.Combine(recovery!, "0.bin"), first, true);
            Assert.Equal("first original", File.ReadAllText(first));
            Assert.Equal("second original", File.ReadAllText(second));
        }
        finally
        {
            firstLock?.Dispose();
            Directory.Delete(root, true);
            if (recovery is not null) Directory.Delete(recovery, true);
        }
    }
}

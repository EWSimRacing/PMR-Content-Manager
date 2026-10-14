using System.Security.AccessControl;
using System.Security.Principal;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using Xunit;

namespace EWSR_PMR_ModApp.Core.Tests.Elevation;

public class WriteAccessAndAttributesTests
{
    [Fact]
    public void DirectoryWritableButExistingFileAclIsNot_RoutesToHelperWithoutChangingAclOrBytes()
    {
        WithFixture((root, source, target) =>
        {
            var info = new FileInfo(target);
            var originalAcl = info.GetAccessControl(AccessControlSections.Access);
            var restricted = new FileSecurity();
            restricted.SetAccessRuleProtection(true, false);
            restricted.SetOwner(WindowsIdentity.GetCurrent().User!);
            restricted.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!,
                FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize, AccessControlType.Allow));
            try
            {
                info.SetAccessControl(restricted);
                string descriptor = info.GetAccessControl(AccessControlSections.Access)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
                Assert.True(new RealFileSystem().CanWriteDirectory(root));
                Assert.Throws<UnauthorizedAccessException>(() => File.Copy(source, target, true));
                Assert.True(WriteAccessPolicy.RequiresElevation(Request(root, source)));
                Assert.Equal("original", File.ReadAllText(target));
                Assert.Equal(descriptor, info.GetAccessControl(AccessControlSections.Access)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
                // Elevation is not permission to override an explicit ACL: the copy still fails.
                Assert.Throws<UnauthorizedAccessException>(() => ManagedFileOperations.Copy(source, target, true));
                Assert.Equal(descriptor, info.GetAccessControl(AccessControlSections.Access)
                    .GetSecurityDescriptorSddlForm(AccessControlSections.Access));
            }
            finally { info.SetAccessControl(originalAcl); }
        });
    }

    [Fact]
    public void ProtectedProgramFiles_AlwaysRoutesToHelper_NotDirectoryCreationProbe()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        Assert.True(WriteAccessPolicy.RequiresElevation(new WritePlanRequest
        {
            DataRoot = Path.Combine(root, "SyntheticPMR", "data"),
            GameRoot = Path.Combine(root, "SyntheticPMR"), ModId = "fixture"
        }));
        Assert.False(WriteAccessPolicy.IsProtectedRoot(root + "_not_program_files"));
    }

    [Fact]
    public void WritableExistingTarget_DirectWriterCheckDoesNotChangeBytesOrTimestamp()
    {
        WithFixture((root, source, target) =>
        {
            var stamp = File.GetLastWriteTimeUtc(target);
            Assert.False(WriteAccessPolicy.RequiresElevation(Request(root, source)));
            Assert.Equal(stamp, File.GetLastWriteTimeUtc(target));
            Assert.Equal("original", File.ReadAllText(target));
        });
    }

    [Theory]
    [InlineData(WritePlanOperation.Install)]
    [InlineData(WritePlanOperation.Reapply)]
    public void ReadOnlyOverwrite_ReproducesRawFailure_ManagedWriterPreservesAttributes(WritePlanOperation operation)
    {
        WithFixture((root, source, target) =>
        {
            var attributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive;
            File.SetAttributes(target, attributes);
            Assert.Throws<UnauthorizedAccessException>(() => File.Copy(source, target, true));
            Assert.Equal("original", File.ReadAllText(target));
            Assert.True(WriteAccessPolicy.RequiresElevation(Request(root, source)));
            string mod = Guid.NewGuid().ToString("N");
            try
            {
                var result = WritePlanExecutor.Execute(new WritePlanRequest
                {
                    DataRoot = root, GameRoot = root, ModId = mod, Operation = operation,
                    FilesToCopy = [new FileCopySpec { SourcePath = source, RelativeTargetPath = "target.xml" }],
                    FilesToBackup = operation == WritePlanOperation.Install
                        ? [new FileTargetSpec { RelativeTargetPath = "target.xml" }] : []
                });
                Assert.True(result.Success, result.ErrorMessage);
                Assert.Equal("replacement", File.ReadAllText(target));
                Assert.Equal(attributes, File.GetAttributes(target));
                if (operation == WritePlanOperation.Install)
                {
                    string backup = Path.Combine(AppPaths.BackupDirForMod(mod), "__data__", "target.xml");
                    Assert.Equal("original", File.ReadAllText(backup));
                    Assert.Equal(attributes, File.GetAttributes(backup));
                    File.SetAttributes(target, FileAttributes.Normal);
                    var restored = WritePlanExecutor.Execute(new WritePlanRequest
                    {
                        DataRoot = root, GameRoot = root, ModId = mod, Operation = WritePlanOperation.Uninstall
                    });
                    Assert.True(restored.Success, restored.ErrorMessage);
                    Assert.Equal("original", File.ReadAllText(target));
                    Assert.Equal(attributes, File.GetAttributes(target));
                }
            }
            finally
            {
                string backup = AppPaths.BackupDirForMod(mod);
                if (Directory.Exists(backup)) ManagedFileOperations.DeleteDirectory(backup, true);
            }
        });
    }

    [Theory]
    [InlineData(WritePlanOperation.Install)]
    [InlineData(WritePlanOperation.Reapply)]
    public void ReadOnlyFirstTarget_LockedSecond_RollbackRestoresBytesAndFlags(WritePlanOperation operation)
    {
        WithFixture((root, source, target) =>
        {
            var originalAttributes = FileAttributes.ReadOnly | FileAttributes.Archive;
            File.SetAttributes(target, originalAttributes);
            string second = Path.Combine(root, "second.xml");
            File.WriteAllText(second, "second original");
            string mod = Guid.NewGuid().ToString("N");
            try
            {
                using var held = File.Open(second, FileMode.Open, FileAccess.Read, FileShare.Read);
                var result = WritePlanExecutor.Execute(new WritePlanRequest
                {
                    DataRoot = root, GameRoot = root, ModId = mod, Operation = operation,
                    FilesToCopy =
                    [
                        new FileCopySpec { SourcePath = source, RelativeTargetPath = "target.xml" },
                        new FileCopySpec { SourcePath = source, RelativeTargetPath = "second.xml" }
                    ],
                    FilesToBackup = operation == WritePlanOperation.Install
                        ? [new FileTargetSpec { RelativeTargetPath = "target.xml" }, new FileTargetSpec { RelativeTargetPath = "second.xml" }] : []
                });
                Assert.False(result.Success);
                Assert.True(result.RollbackAttempted);
                Assert.True(result.RollbackSucceeded, result.ErrorMessage);
                Assert.Equal(0, result.FilesCopied);
                Assert.Equal("original", File.ReadAllText(target));
                Assert.Equal(originalAttributes, File.GetAttributes(target));
                Assert.Equal("second original", File.ReadAllText(second));
            }
            finally
            {
                string backup = AppPaths.BackupDirForMod(mod);
                if (Directory.Exists(backup)) ManagedFileOperations.DeleteDirectory(backup, true);
            }
        });
    }

    [Fact]
    public void FailedReadOnlyCopy_RestoresAttributes_AndReadOnlyDeleteHandlesNewPayload()
    {
        WithFixture((root, source, target) =>
        {
            var attributes = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive;
            File.SetAttributes(target, attributes);
            Assert.Throws<FileNotFoundException>(() => ManagedFileOperations.Copy(Path.Combine(root, "missing.xml"), target, true));
            Assert.Equal(attributes, File.GetAttributes(target));
            Assert.Equal("original", File.ReadAllText(target));
            ManagedFileOperations.Delete(target);
            Assert.False(File.Exists(target));
        });
    }

    private static WritePlanRequest Request(string root, string source) => new()
    {
        DataRoot = root, GameRoot = root, ModId = "fixture", Operation = WritePlanOperation.Install,
        FilesToCopy = [new FileCopySpec { SourcePath = source, RelativeTargetPath = "target.xml" }]
    };
    private static void WithFixture(Action<string, string, string> test)
    {
        string root = Path.GetFullPath($"access-attributes-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        string source = Path.Combine(root, "source.xml"), target = Path.Combine(root, "target.xml");
        try
        {
            File.WriteAllText(source, "replacement");
            File.WriteAllText(target, "original");
            test(root, source, target);
        }
        finally { ManagedFileOperations.DeleteDirectory(root, true); }
    }
}

// W3 — WritePlanExecutor integration tests + InProcessWriter security tests
//
// "Integration" means real System.IO: temp directories on disk, real File.Copy / Directory.Create.
// WritePlanExecutor is stateless and uses no IFileSystem abstraction, so we must exercise it
// against a real (temp) filesystem.
//
// BackupFiles/RestoreBackups write to AppPaths.BackupDirForMod(modId) — the real %APPDATA%.
// Tests that trigger backup use a unique GUID modId and register it with TempScope for cleanup.
//
// InProcessWriter security tests validate path rejection BEFORE WritePlanExecutor is called,
// so they need no real source files to exist — the request is rejected in validation.

using Xunit;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Tests.Elevation;

public class WritePlanExecutorTests
{
    // ── TempScope: creates temp dirs and ensures cleanup even on test failure ─

    private sealed class TempScope : IDisposable
    {
        private readonly List<string> _paths = new();

        /// <summary>Creates a new temp directory and registers it for cleanup.</summary>
        public string NewDir()
        {
            var di = Directory.CreateTempSubdirectory("wez-");
            _paths.Add(di.FullName);
            return di.FullName;
        }

        /// <summary>Registers an additional path (e.g., AppData backup dir) for cleanup.</summary>
        public void AlsoCleanup(string path) => _paths.Add(path);

        public void Dispose()
        {
            foreach (string p in _paths)
            {
                try
                {
                    if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
                }
                catch { /* best-effort — don't mask the actual test failure */ }
            }
        }
    }

    private static string WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ── WritePlanExecutor: Install ────────────────────────────────────────────

    [Fact]
    public void Execute_Install_NoBackup_CopiesFilesToDataRoot()
    {
        using var scope   = new TempScope();
        string srcDir     = scope.NewDir();
        string dataDir    = scope.NewDir();
        string srcFile    = WriteFile(Path.Combine(srcDir, "livery.dds"), "mod-content");

        var request = new WritePlanRequest
        {
            Operation   = WritePlanOperation.Install,
            DataRoot    = dataDir,
            ModId       = "test-no-backup",
            FilesToCopy = [new FileCopySpec { SourcePath = srcFile, RelativeTargetPath = "vehicles/car_a/livery.dds" }]
        };

        var result = WritePlanExecutor.Execute(request);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesBackedUp);
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal("mod-content",
            File.ReadAllText(Path.Combine(dataDir, "vehicles", "car_a", "livery.dds")));
    }

    [Fact]
    public void Execute_Install_WithBackup_BackupsExistingFileThenCopiesNew()
    {
        string modId    = "wez-backup-" + Guid.NewGuid().ToString("N");
        using var scope = new TempScope();
        scope.AlsoCleanup(AppPaths.BackupDirForMod(modId));

        string srcDir  = scope.NewDir();
        string dataDir = scope.NewDir();

        // "Original" game file in DataRoot that must be backed up.
        string existing = WriteFile(
            Path.Combine(dataDir, "vehicles", "car_a", "livery.dds"), "original-content");

        // Mod source file.
        string srcFile = WriteFile(Path.Combine(srcDir, "livery.dds"), "mod-content");

        var request = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Install,
            DataRoot      = dataDir,
            ModId         = modId,
            FilesToBackup = [Target("vehicles/car_a/livery.dds")],
            FilesToCopy   = [new FileCopySpec { SourcePath = srcFile, RelativeTargetPath = "vehicles/car_a/livery.dds" }]
        };

        var result = WritePlanExecutor.Execute(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(1, result.FilesBackedUp);

        // Mod file written to DataRoot.
        Assert.Equal("mod-content", File.ReadAllText(existing));

        // Original backed up to AppData backup dir.
        string backupFile = Path.Combine(
            AppPaths.BackupDirForMod(modId), "__data__", "vehicles", "car_a", "livery.dds");
        Assert.True(File.Exists(backupFile));
        Assert.Equal("original-content", File.ReadAllText(backupFile));
    }

    [Fact]
    public void Execute_Install_BackupSkipsNewFiles_CountReflectsOnlyActualBackups()
    {
        // If FilesToBackup lists a path that does not exist in DataRoot yet (a brand-new
        // mod file), BackupFiles must skip it gracefully — not count it as backed up.
        string modId    = "wez-skip-" + Guid.NewGuid().ToString("N");
        using var scope = new TempScope();
        scope.AlsoCleanup(AppPaths.BackupDirForMod(modId));

        string srcDir  = scope.NewDir();
        string dataDir = scope.NewDir();

        string srcFile = WriteFile(Path.Combine(srcDir, "brand_new.dds"), "new-mod-content");

        var request = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Install,
            DataRoot      = dataDir,
            ModId         = modId,
            FilesToBackup = [Target("vehicles/car_a/brand_new.dds")],  // does NOT exist in DataRoot yet
            FilesToCopy   = [new FileCopySpec { SourcePath = srcFile, RelativeTargetPath = "vehicles/car_a/brand_new.dds" }]
        };

        var result = WritePlanExecutor.Execute(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesBackedUp);  // skipped — nothing to back up
    }

    // ── WritePlanExecutor: Uninstall ──────────────────────────────────────────

    [Fact]
    public void Execute_Uninstall_RestoresBackupAndDeletesNewFile()
    {
        string modId    = "wez-uninstall-" + Guid.NewGuid().ToString("N");
        using var scope = new TempScope();
        scope.AlsoCleanup(AppPaths.BackupDirForMod(modId));

        string dataDir = scope.NewDir();

        // Pre-seed backup directory (simulates a prior Install).
        WriteFile(
            Path.Combine(AppPaths.BackupDirForMod(modId), "__data__", "vehicles", "car_a", "livery.dds"),
            "original-content-for-restore");

        // A "new" file added by the mod — no backup exists, so it must be deleted.
        string newModFile = WriteFile(
            Path.Combine(dataDir, "vehicles", "car_a", "new_skin.dds"), "new-mod-file");

        var request = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Uninstall,
            DataRoot      = dataDir,
            ModId         = modId,
            FilesToDelete = [Target("vehicles/car_a/new_skin.dds")]
        };

        var result = WritePlanExecutor.Execute(request);

        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Equal(1, result.FilesDeleted);
        // FilesBackedUp is reused by the executor to report the restore count.
        Assert.Equal(1, result.FilesBackedUp);

        // Original file was restored to DataRoot.
        string restored = Path.Combine(dataDir, "vehicles", "car_a", "livery.dds");
        Assert.True(File.Exists(restored));
        Assert.Equal("original-content-for-restore", File.ReadAllText(restored));

        // New mod file was deleted.
        Assert.False(File.Exists(newModFile));
    }

    [Fact]
    public void Execute_Uninstall_MixedRoot_RestoresGameRootBackupAndDeletesGameRootNewFile()
    {
        string modId    = "wez-mixed-" + Guid.NewGuid().ToString("N");
        using var scope = new TempScope();
        scope.AlsoCleanup(AppPaths.BackupDirForMod(modId));

        string gameDir = scope.NewDir();
        string dataDir = Path.Combine(gameDir, "data");
        Directory.CreateDirectory(dataDir);

        string gameExisting = WriteFile(Path.Combine(gameDir, "shared", "starmap.dds"), "original-stars");
        string dataExisting = WriteFile(Path.Combine(dataDir, "vehicles", "car_a", "livery.dds"), "original-livery");
        string srcDir       = scope.NewDir();
        string gameSrc      = WriteFile(Path.Combine(srcDir, "starmap.dds"), "mod-stars");
        string dataSrc      = WriteFile(Path.Combine(srcDir, "livery.dds"), "mod-livery");

        var install = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Install,
            DataRoot      = dataDir,
            GameRoot      = gameDir,
            ModId         = modId,
            FilesToBackup = [Target("vehicles/car_a/livery.dds"), Target("shared/starmap.dds", TargetRoot.Game)],
            FilesToCopy =
            [
                new FileCopySpec { SourcePath = dataSrc, RelativeTargetPath = "vehicles/car_a/livery.dds" },
                new FileCopySpec { SourcePath = gameSrc, RelativeTargetPath = "shared/starmap.dds", TargetRoot = TargetRoot.Game }
            ]
        };

        var installResult = WritePlanExecutor.Execute(install);

        Assert.True(installResult.Success);
        Assert.Equal("mod-livery", File.ReadAllText(dataExisting));
        Assert.Equal("mod-stars", File.ReadAllText(gameExisting));
        Assert.True(File.Exists(Path.Combine(AppPaths.BackupDirForMod(modId), "__data__", "vehicles", "car_a", "livery.dds")));
        Assert.True(File.Exists(Path.Combine(AppPaths.BackupDirForMod(modId), "__game__", "shared", "starmap.dds")));

        string gameNew = WriteFile(Path.Combine(gameDir, "shared", "new_stars.dds"), "new-game-root-file");
        var uninstall = new WritePlanRequest
        {
            Operation     = WritePlanOperation.Uninstall,
            DataRoot      = dataDir,
            GameRoot      = gameDir,
            ModId         = modId,
            FilesToDelete = [Target("shared/new_stars.dds", TargetRoot.Game)]
        };

        var uninstallResult = WritePlanExecutor.Execute(uninstall);

        Assert.True(uninstallResult.Success);
        Assert.Equal("original-livery", File.ReadAllText(dataExisting));
        Assert.Equal("original-stars", File.ReadAllText(gameExisting));
        Assert.False(File.Exists(gameNew));
    }

    // ── WritePlanExecutor: Reapply ────────────────────────────────────────────

    [Fact]
    public void Execute_Reapply_CopiesSourceFilesToDataRoot()
    {
        using var scope = new TempScope();
        string srcDir   = scope.NewDir();
        string dataDir  = scope.NewDir();

        string srcFile = WriteFile(Path.Combine(srcDir, "livery.dds"), "cached-payload-content");

        var request = new WritePlanRequest
        {
            Operation   = WritePlanOperation.Reapply,
            DataRoot    = dataDir,
            ModId       = "test-reapply",
            FilesToCopy = [new FileCopySpec { SourcePath = srcFile, RelativeTargetPath = "vehicles/car_a/livery.dds" }]
        };

        var result = WritePlanExecutor.Execute(request);

        Assert.True(result.Success);
        Assert.Equal(1, result.FilesCopied);
        Assert.Equal(0, result.FilesBackedUp);  // Reapply never backs up
        Assert.Equal("cached-payload-content",
            File.ReadAllText(Path.Combine(dataDir, "vehicles", "car_a", "livery.dds")));
    }

    // ── InProcessWriter: security rejection tests ─────────────────────────────
    // These tests exercise the validation layer that runs BEFORE WritePlanExecutor.
    // No files are ever created because the request is rejected during validation.

    [Fact]
    public async Task InProcessWriter_TraversalRelativeTargetPath_RejectsRequest()
    {
        // Attacker crafts a RelativeTargetPath with ".." to escape the DataRoot
        // and write an arbitrary file as Administrator.
        var writer  = new InProcessWriter();
        string fake = @"C:\game\data";

        var result = await writer.ExecuteAsync(new WritePlanRequest
        {
            Operation = WritePlanOperation.Install,
            DataRoot  = fake,
            ModId     = "sec-test",
            FilesToCopy =
            [
                new FileCopySpec
                {
                    SourcePath         = Path.Combine(AppPaths.AppDataRoot, "staging", "test.dds"),
                    RelativeTargetPath = @"..\..\Windows\evil-wez-test.txt"
                }
            ]
        });

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("Unsafe target path", result.ErrorMessage);
        // Belt-and-suspenders: verify the evil file was never created.
        Assert.False(File.Exists(Path.GetFullPath(Path.Combine(fake, @"..\..\Windows\evil-wez-test.txt"))));
    }

    [Fact]
    public async Task InProcessWriter_RootedAbsoluteTargetPath_RejectsRequest()
    {
        var writer = new InProcessWriter();

        var result = await writer.ExecuteAsync(new WritePlanRequest
        {
            Operation = WritePlanOperation.Install,
            DataRoot  = @"C:\game\data",
            ModId     = "sec-test-rooted",
            FilesToCopy =
            [
                new FileCopySpec
                {
                    SourcePath         = Path.Combine(AppPaths.AppDataRoot, "staging", "test.dds"),
                    RelativeTargetPath = @"C:\Windows\evil.dll"  // rooted path
                }
            ]
        });

        Assert.False(result.Success);
        Assert.Contains("Unsafe target path", result.ErrorMessage);
    }

    [Fact]
    public async Task InProcessWriter_SourceOutsideAppDataRoot_RejectsRequest()
    {
        // Valid target path, but source is not under %APPDATA%\EWSR_PMR_ModApp\.
        var writer = new InProcessWriter();

        var result = await writer.ExecuteAsync(new WritePlanRequest
        {
            Operation = WritePlanOperation.Install,
            DataRoot  = @"C:\game\data",
            ModId     = "sec-test-src",
            FilesToCopy =
            [
                new FileCopySpec
                {
                    SourcePath         = @"C:\Windows\notepad.exe",   // outside AppData
                    RelativeTargetPath = "vehicles/car_a/notepad.exe" // valid target
                }
            ]
        });

        Assert.False(result.Success);
        Assert.Contains("Source path not in app data", result.ErrorMessage);
    }

    [Fact]
    public async Task InProcessWriter_TraversalInFilesToBackup_RejectsRequest()
    {
        // A traversal in FilesToBackup would trick the Helper into reading a file
        // outside DataRoot during the backup phase.
        var writer = new InProcessWriter();

        var result = await writer.ExecuteAsync(new WritePlanRequest
        {
            Operation     = WritePlanOperation.Install,
            DataRoot      = @"C:\game\data",
            ModId         = "sec-test-backup",
            FilesToBackup = [Target(@"..\..\Windows\important.dll")]
        });

        Assert.False(result.Success);
        Assert.Contains("Unsafe backup path", result.ErrorMessage);
    }

    [Fact]
    public async Task InProcessWriter_EmptyDataRoot_RejectsRequest()
    {
        var writer = new InProcessWriter();

        var result = await writer.ExecuteAsync(new WritePlanRequest
        {
            Operation = WritePlanOperation.Install,
            DataRoot  = "   ",
            ModId     = "test"
        });

        Assert.False(result.Success);
        Assert.Contains("DataRoot", result.ErrorMessage);
    }

    [Fact]
    public async Task InProcessWriter_EmptyModId_RejectsRequest()
    {
        var writer = new InProcessWriter();

        var result = await writer.ExecuteAsync(new WritePlanRequest
        {
            Operation = WritePlanOperation.Install,
            DataRoot  = @"C:\game\data",
            ModId     = string.Empty
        });

        Assert.False(result.Success);
        Assert.Contains("ModId", result.ErrorMessage);
    }

    private static FileTargetSpec Target(string relativePath, TargetRoot targetRoot = TargetRoot.Data) =>
        new() { RelativeTargetPath = relativePath, TargetRoot = targetRoot };
}

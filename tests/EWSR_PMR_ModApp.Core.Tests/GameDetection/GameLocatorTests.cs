// Reconciled against EWSR_PMR_ModApp.Core.GameDetection.GameLocator directly
// via an injected FakeFileSystem so no real disk access occurs.
// ValidateDataRoot requires the path AND at least one known sub-folder to exist
// (vehicles, tracks, configs, or sounds) — the FakeFileSystem seeds these.

using Xunit;
using EWSR_PMR_ModApp.Core.GameDetection;
using EWSR_PMR_ModApp.Core.Tests.TestDoubles;

namespace EWSR_PMR_ModApp.Core.Tests.GameDetection;

public class GameLocatorTests
{
    private const string DefaultDataRoot = @"C:\Program Files\Project Motor Racing\data";
    private const string CustomPath      = @"D:\Games\ProjectMotorRacing\data";
    private const string KnownSubfolder  = "vehicles"; // one of {vehicles, tracks, configs, sounds}

    // Builds a FakeFileSystem where the given paths are valid data roots.
    // ValidateDataRoot requires root dir + at least one known subfolder.
    private static FakeFileSystem FsWithValidPath(params string[] dataRoots)
    {
        var fs = new FakeFileSystem();
        foreach (var root in dataRoots)
        {
            fs.AddDirectory(root);
            fs.AddDirectory(Path.Combine(root, KnownSubfolder));
        }
        return fs;
    }

    // ── User-configured path takes absolute precedence ────────────────────────────

    /// <summary>
    /// When the user has saved a custom path in Settings it must win even if the
    /// hard-coded default would also be valid.
    /// </summary>
    [Fact]
    public async Task UserConfiguredPath_WhenValid_TakesPrecedenceOverDefaultPath()
    {
        var fs      = FsWithValidPath(DefaultDataRoot, CustomPath);
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync(userConfiguredPath: CustomPath);

        Assert.True(result.Found);
        Assert.Equal(CustomPath, result.DataRoot);
        Assert.Equal(LocationSource.UserConfigured, result.Source);
    }

    [Fact]
    public async Task UserConfiguredPath_IsUsedEvenIfDefaultAlsoExists()
    {
        var fs      = FsWithValidPath(DefaultDataRoot, CustomPath);
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync(userConfiguredPath: CustomPath);

        Assert.NotEqual(DefaultDataRoot, result.DataRoot);
        Assert.Equal(CustomPath, result.DataRoot);
    }

    // ── Default path resolution when no user config exists ───────────────────────

    [Fact]
    public async Task NoUserConfig_DefaultPathExists_ResolvesToDefaultDataRoot()
    {
        var fs      = FsWithValidPath(DefaultDataRoot);
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync();

        Assert.True(result.Found);
        Assert.Equal(DefaultDataRoot, result.DataRoot);
        Assert.Equal(LocationSource.DefaultPath, result.Source);
    }

    /// <summary>
    /// The canonical default must match the path documented in decisions.md and ARCHITECTURE.md.
    /// Changing it is a breaking decision that requires updating the architecture docs.
    /// </summary>
    [Fact]
    public async Task DefaultDataRoot_MatchesArchitectureSpec()
    {
        var fs      = FsWithValidPath(DefaultDataRoot);
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync();

        Assert.Equal(@"C:\Program Files\Project Motor Racing\data", result.DataRoot);
    }

    // ── Non-existent / invalid path → graceful failure, no crash ─────────────────

    /// <summary>
    /// If neither the user-configured path, nor the default, nor Steam detection finds a
    /// valid folder, the locator must return Found=false — never throw an exception.
    /// </summary>
    [Fact]
    public async Task NoUserConfig_DefaultPathAbsent_ReturnsNotFound_DoesNotThrow()
    {
        var fs      = new FakeFileSystem();
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync();

        Assert.False(result.Found);
        Assert.Null(result.DataRoot);
        Assert.Equal(LocationSource.NotFound, result.Source);
        Assert.NotNull(result.FailureReason);
        Assert.NotEmpty(result.FailureReason);
    }

    /// <summary>
    /// A user-configured path pointing to a non-existent / invalid directory must NOT crash.
    /// It should return Found=false with a clear failure reason so the UI can re-prompt.
    /// </summary>
    [Fact]
    public async Task UserConfiguredPath_DoesNotExist_ReturnsNotFound_DoesNotThrow()
    {
        const string badPath = @"Z:\DoesNotExist\data";
        var fs      = new FakeFileSystem();
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync(userConfiguredPath: badPath);

        Assert.False(result.Found);
        Assert.Null(result.DataRoot);
        Assert.Equal(LocationSource.NotFound, result.Source);
    }

    /// <summary>
    /// A path that exists on disk but has none of the expected sub-folders is invalid.
    /// ValidateDataRoot should reject it and LocateAsync should fall through to NotFound.
    /// </summary>
    [Fact]
    public async Task UserConfiguredPath_ExistsButLacksKnownSubfolders_ReturnsNotFound()
    {
        const string emptyDir = @"C:\EmptyDir\data";
        var fs = new FakeFileSystem();
        fs.AddDirectory(emptyDir);

        var locator = new GameLocator(fs);
        var result  = await locator.LocateAsync(userConfiguredPath: emptyDir);

        Assert.False(result.Found);
    }

    [Fact]
    public async Task EmptyUserConfiguredPath_TreatedAsNotProvided_FallsBackToDefaultChain()
    {
        var fs      = new FakeFileSystem();
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync(userConfiguredPath: "");

        Assert.Equal(LocationSource.NotFound, result.Source);
    }

    [Fact]
    public async Task WhitespaceUserConfiguredPath_TreatedAsNotProvided_FallsBackToDefaultChain()
    {
        var fs      = new FakeFileSystem();
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync(userConfiguredPath: "   ");

        Assert.Equal(LocationSource.NotFound, result.Source);
    }

    // ── Result shape is always safe to consume ───────────────────────────────────

    [Fact]
    public async Task WhenFound_DataRootIsNeverNull()
    {
        var fs      = FsWithValidPath(DefaultDataRoot);
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync();

        Assert.True(result.Found);
        Assert.NotNull(result.DataRoot);
        Assert.NotEmpty(result.DataRoot);
    }

    [Fact]
    public async Task WhenNotFound_DataRootIsNull()
    {
        var fs      = new FakeFileSystem();
        var locator = new GameLocator(fs);

        var result = await locator.LocateAsync();

        Assert.False(result.Found);
        Assert.Null(result.DataRoot);
    }

    // ── ValidateDataRoot helper ───────────────────────────────────────────────────

    [Fact]
    public void ValidateDataRoot_ValidPath_ReturnsTrue()
    {
        var fs      = FsWithValidPath(@"C:\PMR\data");
        var locator = new GameLocator(fs);

        Assert.True(locator.ValidateDataRoot(@"C:\PMR\data"));
    }

    [Fact]
    public void ValidateDataRoot_NonExistentPath_ReturnsFalse()
    {
        var locator = new GameLocator(new FakeFileSystem());

        Assert.False(locator.ValidateDataRoot(@"Z:\Ghost\data"));
    }

    [Fact]
    public void ValidateDataRoot_EmptyPath_ReturnsFalse()
    {
        var locator = new GameLocator(new FakeFileSystem());

        Assert.False(locator.ValidateDataRoot(""));
    }

    // ── CanWriteDataRoot ──────────────────────────────────────────────────────────

    [Fact]
    public void CanWriteDataRoot_WritableFs_ReturnsTrue()
    {
        var fs      = FsWithValidPath(DefaultDataRoot);
        var locator = new GameLocator(fs);

        Assert.True(locator.CanWriteDataRoot(DefaultDataRoot));
    }

    [Fact]
    public void CanWriteDataRoot_ReadOnlyFs_ReturnsFalse()
    {
        var fs = new FakeFileSystem(canWrite: false);
        fs.AddDirectory(DefaultDataRoot);
        var locator = new GameLocator(fs);

        Assert.False(locator.CanWriteDataRoot(DefaultDataRoot));
    }
}

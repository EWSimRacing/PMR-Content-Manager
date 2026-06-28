// W1 — PathValidator unit tests (security-critical)
// Tests every category of traversal attack and edge case that could allow a malicious
// WritePlanRequest to escape the game data root or use an untrusted source file.
// These are the most important tests in Phase 3 — a bypass here lets an elevated
// Helper process write arbitrary files anywhere on the system.

using Xunit;
using EWSR_PMR_ModApp.Core.Elevation;

namespace EWSR_PMR_ModApp.Core.Tests.Elevation;

public class PathValidatorTests
{
    private const string DataRoot        = @"C:\game\data";
    private const string GameRoot        = @"C:\game";
    private const string FakeAppDataRoot = @"C:\Users\TestUser\AppData\Roaming\EWSR_PMR_ModApp";

    // ── IsUnderDataRoot: accept legitimate relative paths ─────────────────────

    [Theory]
    [InlineData(@"vehicles\car_a\skin.dds")]
    [InlineData(@"vehicles/car_a/skin.dds")]
    [InlineData("tracks/monaco/circuit.bin")]
    [InlineData("configs/graphics.ini")]
    [InlineData("sounds/engine.wav")]
    public void IsUnderDataRoot_LegitRelativePaths_ReturnsTrue(string relative)
    {
        Assert.True(PathValidator.IsUnderDataRoot(DataRoot, relative));
    }

    // ── IsUnderDataRoot: reject traversal and absolute paths (security) ───────

    [Fact]
    public void IsUnderDataRoot_DotDotTraversalToWindowsSystem32_ReturnsFalse()
    {
        // Classic directory traversal attack: escape data root and reach System32.
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, @"..\..\Windows\System32\evil.dll"));
    }

    [Fact]
    public void IsUnderDataRoot_RootedAbsolutePath_ReturnsFalse()
    {
        // Absolute paths are never valid as "relative target paths".
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, @"C:\Windows\notepad.exe"));
    }

    [Fact]
    public void IsUnderDataRoot_RootedAbsolutePathOnDifferentDrive_ReturnsFalse()
    {
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, @"D:\evil\payload.dll"));
    }

    [Fact]
    public void IsUnderDataRoot_SneakyTraversalThroughSubdirectory_ReturnsFalse()
    {
        // Looks innocuous (starts with "vehicles\") but escapes via repeated ".."
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, @"vehicles\..\..\..\Windows\evil.dll"));
    }

    [Fact]
    public void IsUnderDataRoot_ForwardSlashTraversal_ReturnsFalse()
    {
        // Path.GetFullPath normalises forward-slash traversal on Windows too.
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, "../../Windows/evil.dll"));
    }

    // ── IsUnderDataRoot: edge cases ───────────────────────────────────────────

    [Fact]
    public void IsUnderDataRoot_EmptyRelativePath_ReturnsFalse()
    {
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, string.Empty));
    }

    [Fact]
    public void IsUnderDataRoot_SingleDotResolvesToRoot_ReturnsFalse()
    {
        // "." normalises to the dataRoot directory itself — not a child of it.
        // combined = C:\game\data, normalizedRoot = C:\game\data\ → no match.
        Assert.False(PathValidator.IsUnderDataRoot(DataRoot, "."));
    }

    [Fact]
    public void IsUnderDataRoot_TrailingBackslashOnDataRoot_StillAcceptsLegitPath()
    {
        // TrimEnd must not strip too much; a trailing separator on dataRoot is legal.
        Assert.True(PathValidator.IsUnderDataRoot(@"C:\game\data\", @"vehicles\car.dds"));
    }

    [Fact]
    public void IsUnderDataRoot_CaseInsensitive_UpperCaseDataRoot_AcceptsLowerCasePath()
    {
        // Windows FS is case-insensitive; the comparison must honour that.
        Assert.True(PathValidator.IsUnderDataRoot(@"C:\GAME\DATA", @"vehicles\car_a\skin.dds"));
    }

    [Fact]
    public void IsUnderDataRoot_SiblingDirectoryWithSharedPrefix_ReturnsFalse()
    {
        // Regression guard for the classic "prefix without separator" bypass.
        // "..\data_evil\x" resolves to C:\game\data_evil\x which must NOT be
        // accepted just because the string starts with "C:\game\data".
        // The trailing-separator in normalizedRoot prevents the false positive.
        Assert.False(PathValidator.IsUnderDataRoot(@"C:\game\data", @"..\data_evil\x"));
    }

    // ── IsAllowedSource: accept sources inside the app AppData root ───────────

    [Fact]
    public void IsAllowedSource_SourceUnderStagingSubdir_ReturnsTrue()
    {
        string source = Path.Combine(FakeAppDataRoot, "staging", "session-abc", "vehicles", "livery.dds");
        Assert.True(PathValidator.IsAllowedSource(source, FakeAppDataRoot));
    }

    [Fact]
    public void IsAllowedSource_SourceDirectlyInAppDataRoot_ReturnsTrue()
    {
        string source = Path.Combine(FakeAppDataRoot, "file.json");
        Assert.True(PathValidator.IsAllowedSource(source, FakeAppDataRoot));
    }

    // ── IsAllowedSource: reject sources outside AppData root (security) ───────

    [Fact]
    public void IsAllowedSource_SourceInWindowsSystem32_ReturnsFalse()
    {
        Assert.False(PathValidator.IsAllowedSource(@"C:\Windows\System32\malware.dll", FakeAppDataRoot));
    }

    [Fact]
    public void IsAllowedSource_SourceOnDesktop_ReturnsFalse()
    {
        Assert.False(PathValidator.IsAllowedSource(@"C:\Users\TestUser\Desktop\mod.dds", FakeAppDataRoot));
    }

    [Fact]
    public void IsAllowedSource_TraversalEscapingAppDataRoot_ReturnsFalse()
    {
        // After GetFullPath normalisation this resolves well outside the app root.
        string source = Path.Combine(FakeAppDataRoot, "staging", "..", "..", "..", "Windows", "evil.dll");
        Assert.False(PathValidator.IsAllowedSource(source, FakeAppDataRoot));
    }

    [Fact]
    public void IsAllowedSource_EmptySourcePath_ReturnsFalse()
    {
        Assert.False(PathValidator.IsAllowedSource(string.Empty, FakeAppDataRoot));
    }

    [Fact]
    public void IsAllowedSource_SiblingDirectoryWithSharedNamePrefix_ReturnsFalse()
    {
        // "EWSR_PMR_ModApp_Evil" shares a string prefix with "EWSR_PMR_ModApp".
        // The trailing-separator check must prevent this from being treated as a child.
        const string evilSibling = @"C:\Users\TestUser\AppData\Roaming\EWSR_PMR_ModApp_Evil\payload.dds";
        Assert.False(PathValidator.IsAllowedSource(evilSibling, FakeAppDataRoot));
    }

    [Fact]
    public void IsAllowedSource_CaseInsensitive_UpperCasedSourcePath_ReturnsTrue()
    {
        // OrdinalIgnoreCase comparison must accept the same path in a different case.
        string source = FakeAppDataRoot.ToUpperInvariant() + @"\staging\file.dds";
        Assert.True(PathValidator.IsAllowedSource(source, FakeAppDataRoot));
    }

    [Theory]
    [InlineData(@"shared\starmap.dds")]
    [InlineData("shared/moon_diffuse.dds")]
    [InlineData("SHARED/Starmap.dds")]
    public void IsAllowedGameRootTarget_AllowedSharedChild_ReturnsTrue(string relative)
    {
        Assert.True(PathValidator.IsAllowedGameRootTarget(GameRoot, DataRoot, relative));
    }

    [Theory]
    [InlineData(@"C:\game\shared\starmap.dds")]
    [InlineData(@"D:\mods\shared\starmap.dds")]
    public void IsAllowedGameRootTarget_RootedPath_ReturnsFalse(string relative)
    {
        Assert.False(PathValidator.IsAllowedGameRootTarget(GameRoot, DataRoot, relative));
    }

    [Theory]
    [InlineData(@"shared\..\x64\launcher.exe")]
    [InlineData(@"..\shared\starmap.dds")]
    [InlineData(@"shared\..\shared\starmap.dds")]
    public void IsAllowedGameRootTarget_DotDotTraversal_ReturnsFalse(string relative)
    {
        Assert.False(PathValidator.IsAllowedGameRootTarget(GameRoot, DataRoot, relative));
    }

    [Theory]
    [InlineData(@"data\vehicles\car.dds")]
    [InlineData(@"x64\launcher.exe")]
    [InlineData(@"updater\update.exe")]
    [InlineData(@"sdk\tool.dll")]
    [InlineData(@"profileTemplate\profile.xml")]
    [InlineData(@"PMR.exe")]
    public void IsAllowedGameRootTarget_ReservedOrRootTarget_ReturnsFalse(string relative)
    {
        Assert.False(PathValidator.IsAllowedGameRootTarget(GameRoot, DataRoot, relative));
    }
}

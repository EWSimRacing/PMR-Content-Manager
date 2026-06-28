// FileClassifier unit tests (Wez, Tester).
// Covers: UnsafeFile, DisplayOnly (docs + images at root/preview/images folders),
//         Install (game formats inside data/), NoPathMatch (artifacts, OS junk, nested
//         archives, game formats outside data/), MetaFile, modinfo.json overrides
//         (SkipFiles glob, DisplayFiles, Files), HookScript (ps1/bat declared in Hooks),
//         edge cases, and case-insensitive matching.

using Xunit;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.Tests.ZipHandling;

public class FileClassifierTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────────

    private static ZipEntryInfo Entry(string fullNameInZip) => new()
    {
        FullNameInZip  = fullNameInZip,
        StagedFilePath = @"C:\staging\" + fullNameInZip.Replace('/', '\\'),
    };

    private static SkipCategory Classify(string fullNameInZip, ModInfo? modInfo = null)
        => FileClassifier.Classify(Entry(fullNameInZip), modInfo);

    // ── UnsafeFile — common executable / script extensions ───────────────────────

    [Theory]
    [InlineData("setup.exe")]
    [InlineData("helper.dll")]
    [InlineData("install.ps1")]
    [InlineData("run.bat")]
    public void UnsafeExtension_AtRoot_IsUnsafeFile(string fileName)
        => Assert.Equal(SkipCategory.UnsafeFile, Classify(fileName));

    [Theory]
    [InlineData("tools/setup.exe")]
    [InlineData("data/scripts/helper.dll")]
    public void UnsafeExtension_InSubfolder_IsStillUnsafeFile(string path)
        => Assert.Equal(SkipCategory.UnsafeFile, Classify(path));

    // ── DisplayOnly — documentation extensions at zip root ───────────────────────

    [Theory]
    [InlineData("README.md")]
    [InlineData("readme.txt")]
    [InlineData("manual.pdf")]
    public void DocExtension_AtRoot_IsDisplayOnly(string fileName)
        => Assert.Equal(SkipCategory.DisplayOnly, Classify(fileName));

    // ── DisplayOnly — images at zip root ─────────────────────────────────────────

    [Theory]
    [InlineData("preview.jpg")]
    [InlineData("cover.png")]
    [InlineData("thumbnail.jpeg")]
    public void Image_AtRoot_IsDisplayOnly(string fileName)
        => Assert.Equal(SkipCategory.DisplayOnly, Classify(fileName));

    // ── DisplayOnly — images inside preview/ folder ───────────────────────────────

    [Fact]
    public void Image_InPreviewFolder_IsDisplayOnly()
        => Assert.Equal(SkipCategory.DisplayOnly, Classify("preview/screenshot.png"));

    // ── DisplayOnly — images inside images/ folder ────────────────────────────────

    [Fact]
    public void Image_InImagesFolder_IsDisplayOnly()
        => Assert.Equal(SkipCategory.DisplayOnly, Classify("images/mod_preview.jpg"));

    // ── Install — image inside data/ (textures belong in data/) ─────────────────

    [Fact]
    public void Dds_InsideDataPath_IsInstall()
        => Assert.Equal(SkipCategory.Install, Classify("data/vehicles/car/livery.dds"));

    [Theory]
    [InlineData("data/vehicles/car/skin.png")]
    [InlineData("data/tracks/overview.jpg")]
    public void Image_InsideDataPath_IsInstall(string path)
        => Assert.Equal(SkipCategory.Install, Classify(path));

    // ── Install — core game-data formats inside data/ ────────────────────────────

    [Theory]
    [InlineData("data/tracks/circuit.xml")]
    [InlineData("data/weather/rain.hadron")]
    [InlineData("data/physics/engine.tweakers")]
    [InlineData("data/vehicles/car_a/model.i3d")]
    [InlineData("data/vehicles/car_a/livery.dds")]
    public void GameDataExtension_InsideDataPath_IsInstall(string path)
        => Assert.Equal(SkipCategory.Install, Classify(path));

    // ── NoPathMatch — game-data format outside data/ ─────────────────────────────

    [Theory]
    [InlineData("weather.xml")]
    [InlineData("engine.hadron")]
    [InlineData("model.i3d")]
    public void GameDataExtension_AtRoot_IsNoPathMatch(string fileName)
        => Assert.Equal(SkipCategory.NoPathMatch, Classify(fileName));

    // ── MetaFile — modinfo.json at zip root ───────────────────────────────────────

    [Fact]
    public void ModinfoJson_AtRoot_IsMetaFile()
        => Assert.Equal(SkipCategory.MetaFile, Classify("modinfo.json"));

    [Fact]
    public void ModinfoJson_InSubfolder_IsNotMetaFile_IsInstallIfInsideData()
    {
        // modinfo.json inside data/ is not the zip-root meta — .json is a GameDataExtension,
        // so it should be classified as Install when located under data/.
        Assert.Equal(SkipCategory.Install, Classify("data/config/modinfo.json"));
    }

    // ── NoPathMatch — packaging artifact extensions ──────────────────────────────

    [Theory]
    [InlineData("build.log")]
    [InlineData("backup.bak")]
    [InlineData("temp.tmp")]
    public void PackagingArtifactExtension_IsNoPathMatch(string fileName)
        => Assert.Equal(SkipCategory.NoPathMatch, Classify(fileName));

    // ── NoPathMatch — OS artifact filenames ───────────────────────────────────────

    [Theory]
    [InlineData("thumbs.db")]
    [InlineData(".DS_Store")]
    public void OsArtifactFileName_IsNoPathMatch(string fileName)
        => Assert.Equal(SkipCategory.NoPathMatch, Classify(fileName));

    // ── NoPathMatch — macOS zip noise ─────────────────────────────────────────────

    [Theory]
    [InlineData("__MACOSX/._something.xml")]
    [InlineData("__MACOSX/data/._livery.dds")]
    public void MacOsxPrefixPath_IsNoPathMatch(string path)
        => Assert.Equal(SkipCategory.NoPathMatch, Classify(path));

    // ── NoPathMatch — nested archive files ────────────────────────────────────────

    [Theory]
    [InlineData("extra.zip")]
    [InlineData("mod.rar")]
    [InlineData("assets.7z")]
    public void NestedArchiveExtension_IsNoPathMatch(string fileName)
        => Assert.Equal(SkipCategory.NoPathMatch, Classify(fileName));

    // ── modinfo.json DisplayFiles override ────────────────────────────────────────

    [Fact]
    public void ModinfoDisplayFiles_OverridesNormalClassification_IsDisplayOnly()
    {
        // "custom_render.fx" has no known extension → normally NoPathMatch.
        // modinfo.DisplayFiles entry must promote it to DisplayOnly.
        var modInfo = new ModInfo
        {
            DisplayFiles = new Dictionary<string, DisplayFileInfo>
            {
                ["custom_render.fx"] = new DisplayFileInfo { Type = "other" }
            }
        };
        Assert.Equal(SkipCategory.DisplayOnly, Classify("custom_render.fx", modInfo));
    }

    // ── modinfo.json SkipFiles glob ───────────────────────────────────────────────

    [Fact]
    public void ModinfoSkipFiles_GlobStar_MatchesBakFile_IsUserExcluded()
    {
        var modInfo = new ModInfo { SkipFiles = ["*.bak"] };
        Assert.Equal(SkipCategory.UserExcluded, Classify("backup.bak", modInfo));
    }

    [Fact]
    public void ModinfoSkipFiles_GlobStar_NonMatchingExtension_DoesNotExclude()
    {
        // *.bak does not match build.log — falls through to normal rules (packaging artifact → NoPathMatch).
        var modInfo = new ModInfo { SkipFiles = ["*.bak"] };
        Assert.Equal(SkipCategory.NoPathMatch, Classify("build.log", modInfo));
    }

    [Fact]
    public void ModinfoSkipFiles_Evaluated_BeforeDisplayFiles()
    {
        // SkipFiles must win when both SkipFiles and DisplayFiles list the same path.
        var modInfo = new ModInfo
        {
            SkipFiles = ["README.md"],
            DisplayFiles = new Dictionary<string, DisplayFileInfo>
            {
                ["README.md"] = new DisplayFileInfo { Type = "readme" }
            }
        };
        Assert.Equal(SkipCategory.UserExcluded, Classify("README.md", modInfo));
    }

    // ── modinfo.json explicit Files mapping ───────────────────────────────────────

    [Fact]
    public void ModinfoFiles_ExplicitEntry_IsInstall_EvenIfExtensionWouldBeNoPathMatch()
    {
        // "custom_shader.fx" is an unrecognized extension → would be NoPathMatch without modinfo.
        // An explicit modinfo.Files entry declares its target path, making it Install.
        var modInfo = new ModInfo
        {
            Files = new Dictionary<string, string>
            {
                ["custom_shader.fx"] = "vehicles/car_a/custom_shader.fx"
            }
        };
        Assert.Equal(SkipCategory.Install, Classify("custom_shader.fx", modInfo));
    }

    // ── Edge: .md inside data/ → NoPathMatch (docs don't belong in game data tree) ─

    [Fact]
    public void Md_InsideDataPath_IsNoPathMatch()
        => Assert.Equal(SkipCategory.NoPathMatch, Classify("data/tracks/README.md"));

    // ── Edge: .json files that are NOT modinfo.json at root ──────────────────────

    [Fact]
    public void Json_InsideDataPath_IsInstall()
        => Assert.Equal(SkipCategory.Install, Classify("data/config/settings.json"));

    [Fact]
    public void Json_AtRoot_IsNoPathMatch()
        => Assert.Equal(SkipCategory.NoPathMatch, Classify("settings.json"));

    // ── Install — .eval (FFB / physics eval files) inside data/ ─────────────────

    [Theory]
    [InlineData("data/vehicles/_shared/physics/hadron/Chassis/ffb.rack.eval")]
    [InlineData("data/vehicles/_shared/physics/hadron/Chassis/ffb.tire.eval")]
    public void EvalExtension_InsideDataPath_IsInstall(string path)
        => Assert.Equal(SkipCategory.Install, Classify(path));

    [Fact]
    public void EvalExtension_AtRoot_IsNoPathMatch()
        => Assert.Equal(SkipCategory.NoPathMatch, Classify("ffb.rack.eval"));

    // ── Case insensitivity — extensions and paths ─────────────────────────────────

    [Theory]
    [InlineData("data/tracks/CIRCUIT.XML")]
    [InlineData("data/tracks/circuit.XML")]
    [InlineData("DATA/tracks/circuit.xml")]
    public void GameDataExtension_UpperCase_InsideDataPath_IsInstall(string path)
        => Assert.Equal(SkipCategory.Install, Classify(path));

    [Fact]
    public void Dds_UpperCase_InsideDataPath_IsInstall()
        => Assert.Equal(SkipCategory.Install, Classify("data/vehicles/LIVERY.DDS"));

    [Fact]
    public void ThumbsDb_UpperCase_IsNoPathMatch()
        => Assert.Equal(SkipCategory.NoPathMatch, Classify("THUMBS.DB"));

    [Theory]
    [InlineData("SETUP.EXE")]
    [InlineData("helper.DLL")]
    public void UnsafeExtension_UpperCase_IsUnsafeFile(string fileName)
        => Assert.Equal(SkipCategory.UnsafeFile, Classify(fileName));

    // ── HookScript — declared in modinfo.json hooks block ───────────────────────

    [Fact]
    public void Ps1_DeclaredAsPostInstallHook_IsHookScript()
    {
        var modInfo = new ModInfo
        {
            Hooks = new ModHooks
            {
                PostInstall = new HookScript { Script = "EWS_Setup_RaceIQ.ps1" }
            }
        };
        Assert.Equal(SkipCategory.HookScript, Classify("EWS_Setup_RaceIQ.ps1", modInfo));
    }

    [Fact]
    public void Ps1_DeclaredAsPostUninstallHook_IsHookScript()
    {
        var modInfo = new ModInfo
        {
            Hooks = new ModHooks
            {
                PostUninstall = new HookScript { Script = "EWS_Cleanup_RaceIQ.ps1" }
            }
        };
        Assert.Equal(SkipCategory.HookScript, Classify("EWS_Cleanup_RaceIQ.ps1", modInfo));
    }

    [Fact]
    public void Ps1_HookDeclaration_CaseInsensitiveMatch()
    {
        var modInfo = new ModInfo
        {
            Hooks = new ModHooks
            {
                PostInstall = new HookScript { Script = "EWS_SETUP_RaceIQ.PS1" }
            }
        };
        // The zip entry uses lowercase — should still match the hook declaration.
        Assert.Equal(SkipCategory.HookScript, Classify("EWS_SETUP_RaceIQ.PS1", modInfo));
    }

    [Fact]
    public void Ps1_NotDeclaredInHooks_RemainsUnsafeFile()
    {
        // A .ps1 that is NOT listed as a hook must still be blocked.
        var modInfo = new ModInfo
        {
            Hooks = new ModHooks
            {
                PostInstall = new HookScript { Script = "other_setup.ps1" }
            }
        };
        Assert.Equal(SkipCategory.UnsafeFile, Classify("install.ps1", modInfo));
    }

    [Fact]
    public void Ps1_NoModinfo_RemainsUnsafeFile()
        => Assert.Equal(SkipCategory.UnsafeFile, Classify("install.ps1"));

    [Fact]
    public void Bat_DeclaredAsHook_IsHookScript()
    {
        var modInfo = new ModInfo
        {
            Hooks = new ModHooks
            {
                PostInstall = new HookScript { Script = "scripts/setup.bat" }
            }
        };
        Assert.Equal(SkipCategory.HookScript, Classify("scripts/setup.bat", modInfo));
    }
}

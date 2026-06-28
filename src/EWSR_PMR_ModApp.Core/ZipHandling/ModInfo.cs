namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Represents an optional <c>modinfo.json</c> found at the root of a mod zip.
/// When present this is treated as the authoritative file mapping — heuristics are skipped
/// for files listed in <see cref="Files"/>.
/// </summary>
public sealed class ModInfo
{
    public int SchemaVersion { get; set; } = 1;
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Website { get; set; }
    public string? MinGameVersion { get; set; }
    public List<string> Tags { get; set; } = [];

    /// <summary>
    /// Maps zip-internal paths to their relative target paths under the game data root
    /// (or the string <c>"install"</c> to use path-overlay heuristics).
    /// </summary>
    public Dictionary<string, string> Files { get; set; } = [];

    /// <summary>
    /// Maps zip-internal paths to their relative target paths under the game root.
    /// Targets are still validated by the game-root allowlist before any write.
    /// </summary>
    public Dictionary<string, string> GameRootFiles { get; set; } = [];

    /// <summary>
    /// Files to show in the mod detail UI but never install.
    /// Key: path in zip. Value: display metadata.
    /// </summary>
    public Dictionary<string, DisplayFileInfo> DisplayFiles { get; set; } = [];

    /// <summary>
    /// Glob patterns for files to ignore entirely (not installed, not displayed).
    /// </summary>
    public List<string> SkipFiles { get; set; } = [];

    /// <summary>
    /// Optional lifecycle hook scripts to run after install / after uninstall.
    /// Hook scripts are cached in CM's AppData directory — never copied to the game tree.
    /// </summary>
    public ModHooks? Hooks { get; set; }
}

/// <summary>Metadata for a file that is shown in the UI but never installed.</summary>
public sealed class DisplayFileInfo
{
    public string? Label { get; set; }

    /// <summary>One of: "readme", "preview", "changelog", "license", "other".</summary>
    public string? Type { get; set; }
}

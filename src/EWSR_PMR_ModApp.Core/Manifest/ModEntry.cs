namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>Represents a single installed mod entry in the application manifest.</summary>
public sealed class ModEntry
{
    /// <summary>Stable UUID string identifying this mod installation.</summary>
    public required string ModId { get; init; }

    /// <summary>Human-readable display name for the mod.</summary>
    public required string ModName { get; init; }

    /// <summary>SHA-256 of the original source zip file.</summary>
    public required string SourceZipHash { get; init; }

    /// <summary>UTC timestamp of when the mod was installed.</summary>
    public required DateTimeOffset InstallTimestamp { get; init; }

    /// <summary>All files written to the game directory by this mod.</summary>
    public required IReadOnlyList<InstalledFileEntry> Files { get; init; }

    /// <summary>
    /// Lifecycle hook metadata, or <see langword="null"/> when the mod has no hooks.
    /// Persisted so CM can run the post-uninstall script at uninstall time.
    /// </summary>
    public ModHookMetadata? Hooks { get; init; }
}

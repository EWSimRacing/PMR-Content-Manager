namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>
/// Root manifest document stored as JSON at
/// <c>%APPDATA%\EWSR_PMR_ModApp\manifest.json</c>.
/// </summary>
public sealed class AppManifest
{
    /// <summary>
    /// Schema version — increment when making breaking changes to this document structure.
    /// The store uses this field to drive migration logic.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>All currently installed mods, keyed by <see cref="ModEntry.ModId"/>.</summary>
    public Dictionary<string, ModEntry> Mods { get; set; } = [];
}

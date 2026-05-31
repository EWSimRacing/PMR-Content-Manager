namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Represents an optional <c>modinfo.json</c> found at the root of a mod zip.
/// When present this is treated as the authoritative file mapping — heuristics are skipped.
/// </summary>
public sealed class ModInfo
{
    public string? Name    { get; set; }
    public string? Author  { get; set; }
    public string? Version { get; set; }

    /// <summary>
    /// Maps zip-internal filenames or paths to their relative target paths under the game data root.
    /// Key:   filename/path as it appears in the zip  (e.g. <c>livery.dds</c>).
    /// Value: relative target path under data root    (e.g. <c>vehicles/car_a/livery.dds</c>).
    /// </summary>
    public Dictionary<string, string> Files { get; set; } = [];
}

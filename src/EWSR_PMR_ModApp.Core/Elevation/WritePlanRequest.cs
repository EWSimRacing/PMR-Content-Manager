namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Request payload written by the UI to a temp JSON file and passed to the elevated helper
/// as its sole command-line argument.
/// </summary>
/// <remarks>
/// All <c>init</c> properties use non-<c>required</c> declarations with sensible defaults so that
/// <see cref="System.Text.Json.JsonSerializer"/> can round-trip instances without a source-generated
/// context or special options.  Callers are expected to populate every field before serialising.
/// </remarks>
public sealed class WritePlanRequest
{
    /// <summary>Discriminator for the type of write operation.</summary>
    public WritePlanOperation Operation { get; init; }

    /// <summary>Absolute path to the game data root (e.g. <c>C:\Program Files\...\data</c>).</summary>
    public string DataRoot { get; init; } = string.Empty;

    /// <summary>Mod identifier used for backup-directory naming.</summary>
    public string ModId { get; init; } = string.Empty;

    /// <summary>Files to copy into <see cref="DataRoot"/> (Install / Reapply).</summary>
    public IReadOnlyList<FileCopySpec>? FilesToCopy { get; init; }

    /// <summary>Relative paths under <see cref="DataRoot"/> to delete (Uninstall new-file removal).</summary>
    public IReadOnlyList<string>? FilesToDelete { get; init; }

    /// <summary>
    /// Relative paths under <see cref="DataRoot"/> to back up before overwriting (Install only).
    /// </summary>
    public IReadOnlyList<string>? FilesToBackup { get; init; }
}

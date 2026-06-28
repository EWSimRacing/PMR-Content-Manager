namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>Describes a lifecycle hook script declared in <c>modinfo.json</c>.</summary>
public sealed class HookScript
{
    /// <summary>
    /// Path inside the zip to the script file (e.g. <c>"EWS_Setup_RaceIQ.ps1"</c>).
    /// Relative sub-folder paths are supported (e.g. <c>"scripts/setup.ps1"</c>).
    /// </summary>
    public required string Script { get; set; }

    /// <summary>Human-readable description shown in the hook confirmation dialog.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// When <see langword="true"/>, the hook is launched via <c>runas</c> (UAC elevation).
    /// Defaults to <see langword="true"/> because mod hooks typically write to Program Files.
    /// </summary>
    public bool RequiresElevation { get; set; } = true;
}

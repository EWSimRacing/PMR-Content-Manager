namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>Progress event emitted during install, uninstall, or reapply operations.</summary>
public sealed class SyncProgress
{
    /// <summary>Human-readable description of the current phase (e.g. "Extracting archive").</summary>
    public required string Phase { get; init; }

    /// <summary>0–100 completion percentage.</summary>
    public int PercentComplete { get; init; }

    /// <summary>The file currently being processed, if applicable.</summary>
    public string? CurrentFile { get; init; }
}

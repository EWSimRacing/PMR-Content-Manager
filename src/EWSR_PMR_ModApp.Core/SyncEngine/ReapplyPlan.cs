using EWSR_PMR_ModApp.Core.Elevation;

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Output of <see cref="ISyncEngine.PrepareReapplyAsync"/>.
/// Lists the mods whose files need to be re-copied from the cached payload.
/// </summary>
public sealed class ReapplyPlan
{
    /// <summary>Absolute path to the game data root.</summary>
    public required string DataRoot { get; init; }

    /// <summary>Absolute path to the game root.</summary>
    public required string GameRoot { get; init; }

    /// <summary>One entry per mod that has reverted files requiring re-copy.</summary>
    public required IReadOnlyList<ModReapplyItem> ModsToReapply { get; init; }
}

/// <summary>Per-mod reapply work item inside a <see cref="ReapplyPlan"/>.</summary>
public sealed class ModReapplyItem
{
    /// <summary>Mod identifier.</summary>
    public required string ModId { get; init; }

    /// <summary>Human-readable display name (for progress / error reporting).</summary>
    public required string ModName { get; init; }

    /// <summary>
    /// Files to copy: absolute payload path → relative path under the data root.
    /// Each <see cref="FileCopySpec.SourcePath"/> is an absolute path inside the mod's
    /// payload cache (<c>%APPDATA%\EWSR_PMR_ModApp\mods\{modId}\</c>).
    /// </summary>
    public required IReadOnlyList<FileCopySpec> FilesToCopy { get; init; }
}

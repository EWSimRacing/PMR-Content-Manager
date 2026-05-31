namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>Whether a game update has reverted a mod's files.</summary>
public enum ModRevertState
{
    /// <summary>All modded files match the installed hashes — mod is intact.</summary>
    Intact,

    /// <summary>One or more files differ from the installed hashes — game update reverted them.</summary>
    Reverted,

    /// <summary>The mod payload cache is missing; reapply is impossible without re-installing.</summary>
    PayloadMissing
}

public sealed class ModUpdateStatus
{
    public required string         ModId             { get; init; }
    public required string         ModName           { get; init; }
    public required ModRevertState State             { get; init; }
    public          int            RevertedFileCount { get; init; }
}

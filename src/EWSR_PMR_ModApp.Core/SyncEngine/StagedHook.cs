namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// A hook script that has been extracted to the staging directory and is ready to cache.
/// Carried on <see cref="InstallPlan"/> so the UI layer can run and persist the hook.
/// </summary>
/// <param name="StagedPath">Absolute path to the extracted script inside the staging directory.</param>
/// <param name="ScriptName">Filename only — used as the cache key under ScriptsDirForMod.</param>
/// <param name="Description">Human-readable description for the confirmation dialog, or null.</param>
/// <param name="RequiresElevation">Whether the script must run elevated via UAC.</param>
public sealed record StagedHook(
    string  StagedPath,
    string  ScriptName,
    string? Description,
    bool    RequiresElevation);

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Runs a mod lifecycle hook script (PowerShell) with optional UAC elevation.
/// </summary>
public interface IHookRunner
{
    /// <summary>
    /// Executes <paramref name="scriptPath"/> via <c>powershell.exe</c>,
    /// passing <c>-DataRoot</c> and <c>-ModId</c> as named parameters.
    /// When <paramref name="requiresElevation"/> is <see langword="true"/> the script is
    /// launched via <c>runas</c> and a UAC prompt is shown to the user.
    /// </summary>
    Task<HookResult> RunAsync(
        string scriptPath,
        string dataRoot,
        string modId,
        bool   requiresElevation,
        CancellationToken ct = default);
}

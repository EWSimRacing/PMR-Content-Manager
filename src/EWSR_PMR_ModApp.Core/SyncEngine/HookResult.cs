namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>Result of running a mod lifecycle hook script.</summary>
public sealed class HookResult
{
    public bool    Success      { get; init; }
    public int     ExitCode     { get; init; }
    public string? Output       { get; init; }
    public string? ErrorMessage { get; init; }

    public static HookResult Ok(int exitCode, string? output = null) =>
        new() { Success = true, ExitCode = exitCode, Output = output };

    public static HookResult Fail(string error, int exitCode = -1) =>
        new() { Success = false, ExitCode = exitCode, ErrorMessage = error };
}

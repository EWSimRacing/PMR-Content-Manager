namespace EWSR_PMR_ModApp.Core.SyncEngine;

public sealed class UninstallResult
{
    public bool    Success       { get; init; }
    public string? ErrorMessage  { get; init; }
    public int     FilesRestored { get; init; }

    public static UninstallResult Failure(string error) =>
        new() { Success = false, ErrorMessage = error };
}

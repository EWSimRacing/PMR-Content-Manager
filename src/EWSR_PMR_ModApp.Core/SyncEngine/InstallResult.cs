namespace EWSR_PMR_ModApp.Core.SyncEngine;

public sealed class InstallResult
{
    public bool                   Success        { get; init; }
    public string?                ModId          { get; init; }
    public string?                ErrorMessage   { get; init; }
    public IReadOnlyList<string>  Warnings       { get; init; } = [];
    public int                    FilesInstalled { get; init; }

    public static InstallResult Failure(string error) =>
        new() { Success = false, ErrorMessage = error };
}

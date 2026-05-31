namespace EWSR_PMR_ModApp.Core.SyncEngine;

public sealed class ReapplyResult
{
    public bool                  Success        { get; init; }
    public int                   ModsReapplied  { get; init; }
    public int                   FilesReapplied { get; init; }
    public IReadOnlyList<string> Errors         { get; init; } = [];
}

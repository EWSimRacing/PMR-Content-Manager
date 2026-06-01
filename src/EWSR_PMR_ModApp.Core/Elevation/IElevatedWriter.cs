namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Abstraction over the two file-write strategies:
/// <list type="bullet">
///   <item><see cref="InProcessWriter"/> — used when <c>IGameLocator.CanWriteDataRoot</c> is <c>true</c> (game not in Program Files, or app already elevated).</item>
///   <item><see cref="HelperProcessWriter"/> — spawns <c>EWSR_PMR_ModApp.Helper.exe</c> with <c>runas</c> when elevation is required.</item>
/// </list>
/// </summary>
public interface IElevatedWriter
{
    /// <summary>Executes the file operations described by <paramref name="request"/>.</summary>
    Task<WriteResult> ExecuteAsync(WritePlanRequest request, CancellationToken ct = default);
}

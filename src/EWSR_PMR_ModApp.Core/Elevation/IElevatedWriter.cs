namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Abstraction over the two file-write strategies:
/// <list type="bullet">
///   <item><see cref="InProcessWriter"/> — used only when request-aware <see cref="WriteAccessPolicy"/> permits direct writes.</item>
///   <item><see cref="HelperProcessWriter"/> — spawns <c>PMR CM.Helper.exe</c> with <c>runas</c> for Program Files or inaccessible existing targets.</item>
/// </list>
/// </summary>
public interface IElevatedWriter
{
    /// <summary>Executes the file operations described by <paramref name="request"/>.</summary>
    Task<WriteResult> ExecuteAsync(WritePlanRequest request, CancellationToken ct = default);
}

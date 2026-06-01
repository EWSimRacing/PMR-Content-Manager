namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>Records a per-file error from a <see cref="WriteResult"/>.</summary>
public sealed class FileOperationError
{
    /// <summary>Relative path of the file that failed (or <c>"*"</c> for a top-level exception).</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Human-readable error description.</summary>
    public string Message { get; init; } = string.Empty;
}

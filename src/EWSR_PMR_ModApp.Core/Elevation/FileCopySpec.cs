namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>Describes a single source → target file copy operation inside a <see cref="WritePlanRequest"/>.</summary>
public sealed class FileCopySpec
{
    /// <summary>
    /// Absolute source path. Must be under <c>%APPDATA%\EWSR_PMR_ModApp\</c>
    /// (staging area or mod payload cache).
    /// </summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>
    /// Relative target path under <see cref="WritePlanRequest.DataRoot"/>
    /// (e.g. <c>vehicles/car_a/livery.dds</c>). Must not be rooted or contain <c>..</c>.
    /// </summary>
    public string RelativeTargetPath { get; init; } = string.Empty;
}

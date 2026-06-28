using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Elevation;

public sealed class FileTargetSpec
{
    public string RelativeTargetPath { get; init; } = string.Empty;

    public TargetRoot TargetRoot { get; init; } = TargetRoot.Data;
}

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>Discriminates the type of write operation the elevated helper should perform.</summary>
public enum WritePlanOperation
{
    /// <summary>Back up originals, then copy mod files into the data root.</summary>
    Install,

    /// <summary>Restore backed-up originals and delete any brand-new mod files.</summary>
    Uninstall,

    /// <summary>Copy cached payload files back into the data root (no backup — originals already saved).</summary>
    Reapply
}

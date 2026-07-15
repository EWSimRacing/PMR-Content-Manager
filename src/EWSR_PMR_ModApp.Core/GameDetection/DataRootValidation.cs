namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>
/// Detailed validation result for a candidate data root path.
/// Provides actionable feedback when a user-selected path doesn't pass validation.
/// </summary>
public sealed record DataRootValidation(
    bool   IsValid,
    string Path,
    bool   PathExists,
    IReadOnlyList<string> FoundSubfolders,
    IReadOnlyList<string> MissingSubfolders)
{
    /// <summary>Human-readable explanation of why validation failed (null when valid).</summary>
    public string? Reason
    {
        get
        {
            if (IsValid) return null;
            if (!PathExists)
                return $"The folder '{Path}' does not exist on disk.";
            if (FoundSubfolders.Count == 0)
                return $"The folder exists but does not contain any expected game sub-folders " +
                       $"(looking for: {string.Join(", ", MissingSubfolders)}). " +
                       "Make sure you're pointing to the 'data' folder inside the game directory.";
            return "Unknown validation failure.";
        }
    }
}

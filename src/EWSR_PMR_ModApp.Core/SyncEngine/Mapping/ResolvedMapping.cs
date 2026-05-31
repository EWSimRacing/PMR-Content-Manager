using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>The user's resolution of an <see cref="AmbiguousMapping"/>.</summary>
public sealed class ResolvedMapping
{
    /// <summary>The ambiguous zip entry being resolved.</summary>
    public required ZipEntryInfo ZipEntry { get; init; }

    /// <summary>
    /// Relative path under the data root chosen by the user,
    /// or <c>null</c> if the user chose to skip this file.
    /// </summary>
    public string? ChosenRelativeTargetPath { get; init; }

    /// <summary>True if the user decided to skip installing this file.</summary>
    public bool Skip { get; init; }
}

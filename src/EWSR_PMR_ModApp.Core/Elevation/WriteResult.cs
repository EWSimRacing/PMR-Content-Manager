namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Result written by the elevated helper to <c>{requestPath}.result.json</c> before it exits.
/// </summary>
public sealed class WriteResult
{
    /// <summary><c>true</c> when all operations completed without error.</summary>
    public bool Success { get; init; }

    /// <summary>Top-level error message, populated when <see cref="Success"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Number of files successfully copied into the data root.</summary>
    public int FilesCopied { get; init; }

    /// <summary>Number of files deleted from the data root.</summary>
    public int FilesDeleted { get; init; }

    /// <summary>Number of original game files backed up before overwriting.</summary>
    public int FilesBackedUp { get; init; }

    /// <summary>Per-file errors when some operations failed but others succeeded.</summary>
    public IReadOnlyList<FileOperationError> Errors { get; init; } = [];
}

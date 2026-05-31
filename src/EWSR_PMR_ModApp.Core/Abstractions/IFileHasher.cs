namespace EWSR_PMR_ModApp.Core.Abstractions;

/// <summary>
/// Computes a content hash for an on-disk file.
/// Injectable so unit tests can supply deterministic fake hashes without touching disk.
/// </summary>
public interface IFileHasher
{
    /// <summary>Returns a lower-case SHA-256 hex digest for the file at <paramref name="filePath"/>.</summary>
    string ComputeHash(string filePath);
}

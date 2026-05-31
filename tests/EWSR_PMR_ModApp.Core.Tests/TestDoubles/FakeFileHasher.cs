using EWSR_PMR_ModApp.Core.Abstractions;

namespace EWSR_PMR_ModApp.Core.Tests.TestDoubles;

/// <summary>
/// Test double for <see cref="IFileHasher"/> — returns pre-seeded hashes keyed by absolute file path.
/// Allows unit tests to control per-file hash values without touching disk.
/// </summary>
public sealed class FakeFileHasher : IFileHasher
{
    private readonly Dictionary<string, string> _hashes;
    private readonly string _defaultHash;

    /// <param name="hashes">Path-to-hash map (case-insensitive). Falls back to <paramref name="defaultHash"/> for unregistered paths.</param>
    /// <param name="defaultHash">Hash returned for any path not in the map.</param>
    public FakeFileHasher(
        Dictionary<string, string>? hashes = null,
        string defaultHash = "fakehash0000000000000000000000000000000000000000000000000000000000")
    {
        _hashes      = new Dictionary<string, string>(hashes ?? new(), StringComparer.OrdinalIgnoreCase);
        _defaultHash = defaultHash;
    }

    public string ComputeHash(string filePath) =>
        _hashes.TryGetValue(filePath, out var h) ? h : _defaultHash;
}

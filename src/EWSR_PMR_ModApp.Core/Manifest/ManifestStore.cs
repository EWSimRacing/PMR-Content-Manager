using System.Text.Json;
using System.Text.Json.Serialization;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.Core.Manifest;

/// <summary>
/// JSON-backed manifest store.
/// A <see cref="SemaphoreSlim"/> ensures all public methods are safe for concurrent callers.
/// </summary>
public sealed class ManifestStore : IManifestStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters    = { new JsonStringEnumConverter() }
    };

    private readonly IFileSystem    _fs;
    private readonly string         _manifestPath;
    private readonly SemaphoreSlim  _lock = new(1, 1);

    /// <summary>Production constructor — uses the canonical app-data manifest path.</summary>
    public ManifestStore(IFileSystem fileSystem)
        : this(fileSystem, AppPaths.ManifestFile) { }

    /// <summary>Test constructor — allows pointing at an arbitrary path.</summary>
    public ManifestStore(IFileSystem fileSystem, string overrideManifestPath)
    {
        _fs           = fileSystem;
        _manifestPath = overrideManifestPath;
    }

    public async Task<AppManifest> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try   { return LoadInternal(); }
        finally { _lock.Release(); }
    }

    public async Task SaveAsync(AppManifest manifest, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try   { SaveInternal(manifest); }
        finally { _lock.Release(); }
    }

    public async Task AddOrUpdateModAsync(ModEntry entry, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var manifest = LoadInternal();
            manifest.Mods[entry.ModId] = entry;
            SaveInternal(manifest);
        }
        finally { _lock.Release(); }
    }

    public async Task RemoveModAsync(string modId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var manifest = LoadInternal();
            manifest.Mods.Remove(modId);
            SaveInternal(manifest);
        }
        finally { _lock.Release(); }
    }

    public async Task<bool> IsFileOwnedByModAsync(
        string relativeTargetPath, CancellationToken ct = default)
    {
        var manifest   = await LoadAsync(ct).ConfigureAwait(false);
        string normed  = Normalize(relativeTargetPath);
        return manifest.Mods.Values.Any(m =>
            m.Files.Any(f => Normalize(f.RelativeTargetPath) == normed));
    }

    public async Task<IReadOnlyList<(string ExistingModId, string CandidateModId, string RelativePath)>>
        DetectConflictsAsync(ModEntry candidate, CancellationToken ct = default)
    {
        var manifest = await LoadAsync(ct).ConfigureAwait(false);
        var candidatePaths = candidate.Files
            .Select(f => $"{f.TargetRoot}:{Normalize(f.RelativeTargetPath)}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var conflicts = new List<(string, string, string)>();
        foreach (var mod in manifest.Mods.Values)
        {
            if (mod.ModId == candidate.ModId) continue;
            foreach (var file in mod.Files)
            {
                if (candidatePaths.Contains($"{file.TargetRoot}:{Normalize(file.RelativeTargetPath)}"))
                    conflicts.Add((mod.ModId, candidate.ModId, file.RelativeTargetPath));
            }
        }
        return conflicts;
    }

    // -------------------------------------------------------------------------
    // Internal helpers (lock must already be held by the caller)
    // -------------------------------------------------------------------------

    private AppManifest LoadInternal()
    {
        if (!_fs.FileExists(_manifestPath))
            return new AppManifest();

        string json = _fs.ReadAllText(_manifestPath);
        var manifest = JsonSerializer.Deserialize<AppManifest>(json, JsonOptions);
        return manifest ?? new AppManifest();
        // TODO: Add schema migration here when SchemaVersion > 1.
    }

    private void SaveInternal(AppManifest manifest)
    {
        _fs.CreateDirectory(Path.GetDirectoryName(_manifestPath)!);
        _fs.WriteAllText(_manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
}

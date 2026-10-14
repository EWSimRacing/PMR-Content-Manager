using EWSR_PMR_ModApp.Core.Manifest;
using EWSR_PMR_ModApp.Core.Elevation;
using EWSR_PMR_ModApp.Core.ZipHandling;

namespace EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

/// <summary>
/// Pure, deterministic mapping resolver.  No disk I/O — takes inputs, returns a plan.
///
/// Strategy order (per decisions.md):
///   1. modinfo.json — when present, authoritative; no heuristics run.
///   2. Path-overlay  — zip preserves folder structure relative to the data root.
///   3. Filename-index fallback — flat zips; match by filename against the data index.
/// </summary>
public sealed class MappingResolver : IMappingResolver
{
    public MappingPlan Resolve(
        IReadOnlyList<ZipEntryInfo> zipEntries,
        string dataRoot,
        IReadOnlyList<string> dataFileIndex,
        ModInfo? modInfo = null)
    {
        foreach (var entry in zipEntries)
            if (!PathValidator.IsSafeRelativePath(entry.FullNameInZip))
                throw new InvalidDataException($"Unsafe archive path rejected: {entry.FullNameInZip}");
        if (zipEntries.Count == 0)
            return new MappingPlan { Mapped = [], Ambiguous = [], Unmatched = [], Collisions = [] };

        // ── Strategy 1: modinfo.json ──────────────────────────────────────────
        if (modInfo is not null && (modInfo.Files.Count > 0 || (modInfo.SchemaVersion >= 2 && modInfo.GameRootFiles.Count > 0)))
        {
            var explicitPlan = ResolveViaModInfo(zipEntries, modInfo, dataRoot, dataFileIndex);
            if (modInfo.SchemaVersion < 2)
                return ApplyCollisionDetection(explicitPlan);

            var remainingEntries = explicitPlan.Unmatched;
            if (remainingEntries.Count == 0)
                return ApplyCollisionDetection(explicitPlan);

            var heuristicPlan = TryPathOverlay(remainingEntries, dataRoot, dataFileIndex)
                ?? ResolveViaFilenameIndex(remainingEntries, dataFileIndex);

            return ApplyCollisionDetection(new MappingPlan
            {
                Mapped     = explicitPlan.Mapped.Concat(heuristicPlan.Mapped).ToList(),
                Ambiguous  = explicitPlan.Ambiguous.Concat(heuristicPlan.Ambiguous).ToList(),
                Unmatched  = heuristicPlan.Unmatched,
                Collisions = []
            });
        }

        // ── Strategy 2: path-overlay ─────────────────────────────────────────
        var overlayPlan = TryPathOverlay(zipEntries, dataRoot, dataFileIndex);
        if (overlayPlan is not null)
            return ApplyCollisionDetection(overlayPlan);

        // ── Strategy 3: filename-index fallback ──────────────────────────────
        return ApplyCollisionDetection(ResolveViaFilenameIndex(zipEntries, dataFileIndex));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Strategy 1 — modinfo.json
    // ─────────────────────────────────────────────────────────────────────────

    private static MappingPlan ResolveViaModInfo(
        IReadOnlyList<ZipEntryInfo> zipEntries, ModInfo modInfo, string dataRoot, IReadOnlyList<string> dataFileIndex)
    {
        var mapped    = new List<FileMappingResult>();
        var unmatched = new List<ZipEntryInfo>();
        var ambiguous = new List<AmbiguousMapping>();

        foreach (var entry in zipEntries)
        {
            if (IsModInfoFile(entry)) continue;

            if (TryResolveExplicitTarget(modInfo.Files, entry, out string? target))
            {
                if (string.Equals(target, "install", StringComparison.OrdinalIgnoreCase))
                {
                    var automatic = TryPathOverlay([entry], dataRoot, dataFileIndex)
                        ?? ResolveViaFilenameIndex([entry], dataFileIndex);
                    mapped.AddRange(automatic.Mapped);
                    unmatched.AddRange(automatic.Unmatched);
                    ambiguous.AddRange(automatic.Ambiguous);
                    continue;
                }
                ValidateExplicitTarget(target);
                mapped.Add(new FileMappingResult
                {
                    ZipEntry           = entry,
                    RelativeTargetPath = Normalize(target),
                    MappingMethod      = MappingMethod.ModInfo
                });
            }
            else if (modInfo.SchemaVersion >= 2 && TryResolveExplicitTarget(modInfo.GameRootFiles, entry, out target))
            {
                ValidateExplicitTarget(target);
                mapped.Add(new FileMappingResult
                {
                    ZipEntry           = entry,
                    RelativeTargetPath = Normalize(target),
                    MappingMethod      = MappingMethod.ModInfo,
                    TargetRoot         = TargetRoot.Game
                });
            }
            else
            {
                unmatched.Add(entry);
            }
        }

        return new MappingPlan { Mapped = mapped, Ambiguous = ambiguous, Unmatched = unmatched, Collisions = [] };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Strategy 2 — path-overlay
    // ─────────────────────────────────────────────────────────────────────────

    private static MappingPlan? TryPathOverlay(
        IReadOnlyList<ZipEntryInfo> zipEntries,
        string dataRoot,
        IReadOnlyList<string> dataFileIndex)
    {
        bool IsKnownDir(string dir) => Directory.Exists(Path.Combine(dataRoot, dir))
            || dataFileIndex.Any(f => f.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase));
        var prefixes = zipEntries.Where(e => GetTopLevelDir(e.FullNameInZip) is { } dir
                && !IsKnownDir(dir) && !string.Equals(dir, "shared", StringComparison.OrdinalIgnoreCase))
            .Select(e => PackagePaths.TryDataPath(e.FullNameInZip, out var prefix, out _) ? prefix : null)
            .Where(p => p is not null && !string.Equals(p, "data/", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string? wrapper = prefixes.Count == 1 ? prefixes[0] : null;
        // Unknown directory layouts must not be silently stripped alongside a wrapper.
        if (wrapper is not null && zipEntries.Any(e => GetTopLevelDir(e.FullNameInZip) is not null
            && !e.FullNameInZip.StartsWith(wrapper, StringComparison.OrdinalIgnoreCase)
            && !e.FullNameInZip.StartsWith("data/", StringComparison.OrdinalIgnoreCase)))
            wrapper = null;

        var mapped = new List<FileMappingResult>();
        var remaining = new List<ZipEntryInfo>();
        foreach (var entry in zipEntries)
        {
            string? relative = null;
            bool anchored = PackagePaths.TryDataPath(entry.FullNameInZip, out var prefix, out var dataRelative);
            if (anchored && string.Equals(prefix, "data/", StringComparison.OrdinalIgnoreCase))
                relative = dataRelative;
            else if (GetTopLevelDir(entry.FullNameInZip) is { } dir
                && !string.Equals(dir, "shared", StringComparison.OrdinalIgnoreCase)
                && IsKnownDir(dir))
                relative = entry.FullNameInZip;
            else if (anchored && string.Equals(prefix, wrapper, StringComparison.OrdinalIgnoreCase))
                relative = dataRelative;

            if (relative is null) remaining.Add(entry);
            else mapped.Add(new FileMappingResult
            {
                ZipEntry = entry, RelativeTargetPath = Normalize(relative), MappingMethod = MappingMethod.PathOverlay
            });
        }
        if (mapped.Count == 0) return null;
        var fallback = ResolveViaFilenameIndex(remaining, dataFileIndex);
        return new MappingPlan
        {
            Mapped = mapped.Concat(fallback.Mapped).ToList(), Ambiguous = fallback.Ambiguous,
            Unmatched = fallback.Unmatched, Collisions = []
        };
    }

    private static bool TryResolveExplicitTarget(
        Dictionary<string, string> map,
        ZipEntryInfo entry,
        out string target)
    {
        if (map.TryGetValue(entry.FullNameInZip, out string? byName)
            || map.TryGetValue(entry.FileName, out byName))
        {
            target = byName;
            return true;
        }

        target = string.Empty;
        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Strategy 3 — filename-index fallback
    // ─────────────────────────────────────────────────────────────────────────

    private static MappingPlan ResolveViaFilenameIndex(
        IReadOnlyList<ZipEntryInfo> zipEntries,
        IReadOnlyList<string> dataFileIndex)
    {
        // filename → list of relative paths in the data root that carry that filename.
        var index = dataFileIndex
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var mapped    = new List<FileMappingResult>();
        var ambiguous = new List<AmbiguousMapping>();
        var unmatched = new List<ZipEntryInfo>();

        foreach (var entry in zipEntries)
        {
            if (IsModInfoFile(entry)) continue;
            if (IsTopLevelSharedPath(entry.FullNameInZip))
            {
                unmatched.Add(entry);
                continue;
            }

            if (!index.TryGetValue(entry.FileName, out var candidates) || candidates.Count == 0)
            {
                // No match on disk — this is a new file; surface for user review.
                unmatched.Add(entry);
                continue;
            }

            if (candidates.Count == 1)
            {
                mapped.Add(new FileMappingResult
                {
                    ZipEntry           = entry,
                    RelativeTargetPath = Normalize(candidates[0]),
                    MappingMethod      = MappingMethod.FilenameMatch
                });
                continue;
            }

            // Multiple candidates — score by right-aligned path segment overlap.
            var scored = candidates
                .Select(c => (Path: c, Score: PathSuffixScore(entry.FullNameInZip, c)))
                .OrderByDescending(x => x.Score)
                .ToList();

            // Auto-resolve if the top candidate is clearly better.
            const int DisambiguationThreshold = 2;
            if (scored[0].Score - scored[1].Score >= DisambiguationThreshold)
            {
                mapped.Add(new FileMappingResult
                {
                    ZipEntry           = entry,
                    RelativeTargetPath = Normalize(scored[0].Path),
                    MappingMethod      = MappingMethod.FilenameMatch
                });
            }
            else
            {
                ambiguous.Add(new AmbiguousMapping
                {
                    ZipEntry       = entry,
                    CandidatePaths = scored.Select(s => Normalize(s.Path)).ToList(),
                    Reason         = $"'{entry.FileName}' matches {candidates.Count} files in the data directory."
                });
            }
        }

        return new MappingPlan { Mapped = mapped, Ambiguous = ambiguous, Unmatched = unmatched, Collisions = [] };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string? GetTopLevelDir(string fullNameInZip)
    {
        string n = fullNameInZip.Replace('\\', '/').TrimStart('/');
        int slash = n.IndexOf('/');
        return slash > 0 ? n[..slash] : null;
    }

    /// <summary>
    /// Counts matching path segments from the right (suffix match).
    /// Higher = better candidate for the filename-index auto-resolver.
    /// </summary>
    private static int PathSuffixScore(string zipPath, string dataRelativePath)
    {
        string[] z = zipPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant().Split('/');
        string[] d = dataRelativePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant().Split('/');
        int score = 0;
        int len   = Math.Min(z.Length, d.Length);
        for (int i = 1; i <= len; i++)
        {
            if (z[^i] == d[^i]) score++;
            else break;
        }
        return score;
    }

    private static bool IsModInfoFile(ZipEntryInfo entry) =>
        !entry.FullNameInZip.Contains('/') && !entry.FullNameInZip.Contains('\\')
        && string.Equals(entry.FileName, "modinfo.json", StringComparison.OrdinalIgnoreCase);

    private static bool IsTopLevelSharedPath(string fullNameInZip) =>
        string.Equals(GetTopLevelDir(fullNameInZip), "shared", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');

    private static void ValidateExplicitTarget(string target)
    {
        if (!PathValidator.IsSafeRelativePath(target))
            throw new InvalidDataException($"Unsafe modinfo target rejected: {target}");
    }

    /// <summary>
    /// Post-pass: groups <see cref="MappingPlan.Mapped"/> entries by their
    /// <see cref="FileMappingResult.RelativeTargetPath"/> (case-insensitive).
    /// Any group with more than one entry is a collision — removed from Mapped and
    /// placed in <see cref="MappingPlan.Collisions"/> so neither silently overwrites
    /// the other.
    /// </summary>
    private static MappingPlan ApplyCollisionDetection(MappingPlan plan)
    {
        var groups = plan.Mapped
            .GroupBy(m => (m.TargetRoot, m.RelativeTargetPath), new MappingCollisionKeyComparer())
            .ToList();

        var collisions = groups
            .Where(g => g.Count() > 1)
            .Select(g => new CollisionMapping
            {
                RelativeTargetPath = g.Key.RelativeTargetPath,
                Entries            = g.ToList()
            })
            .ToList();

        if (collisions.Count == 0)
            return new MappingPlan
            {
                Mapped     = plan.Mapped,
                Ambiguous  = plan.Ambiguous,
                Unmatched  = plan.Unmatched,
                Collisions = []
            };

        var collidingPaths = collisions
            .SelectMany(c => c.Entries.Select(e => (e.TargetRoot, e.RelativeTargetPath)))
            .ToHashSet(new MappingCollisionKeyComparer());

        var safeMapped = plan.Mapped
            .Where(m => !collidingPaths.Contains((m.TargetRoot, m.RelativeTargetPath)))
            .ToList();

        return new MappingPlan
        {
            Mapped     = safeMapped,
            Ambiguous  = plan.Ambiguous,
            Unmatched  = plan.Unmatched,
            Collisions = collisions
        };
    }

    private sealed class MappingCollisionKeyComparer : IEqualityComparer<(TargetRoot TargetRoot, string RelativeTargetPath)>
    {
        public bool Equals(
            (TargetRoot TargetRoot, string RelativeTargetPath) x,
            (TargetRoot TargetRoot, string RelativeTargetPath) y) =>
            x.TargetRoot == y.TargetRoot
            && string.Equals(x.RelativeTargetPath, y.RelativeTargetPath, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((TargetRoot TargetRoot, string RelativeTargetPath) obj) =>
            HashCode.Combine(obj.TargetRoot, StringComparer.OrdinalIgnoreCase.GetHashCode(obj.RelativeTargetPath));
    }
}

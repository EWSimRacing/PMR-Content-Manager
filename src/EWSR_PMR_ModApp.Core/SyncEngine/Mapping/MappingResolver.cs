using EWSR_PMR_ModApp.Core.Manifest;
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
        if (zipEntries.Count == 0)
            return new MappingPlan { Mapped = [], Ambiguous = [], Unmatched = [], Collisions = [] };

        // ── Strategy 1: modinfo.json ──────────────────────────────────────────
        if (modInfo is { Files.Count: > 0 })
            return ApplyCollisionDetection(ResolveViaModInfo(zipEntries, modInfo));

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
        IReadOnlyList<ZipEntryInfo> zipEntries, ModInfo modInfo)
    {
        var mapped    = new List<FileMappingResult>();
        var unmatched = new List<ZipEntryInfo>();

        foreach (var entry in zipEntries)
        {
            if (IsModInfoFile(entry.FileName)) continue;

            // modinfo.Files key may be a bare filename or a full zip path.
            if (modInfo.Files.TryGetValue(entry.FileName, out string? target)
                || modInfo.Files.TryGetValue(entry.FullNameInZip, out target))
            {
                mapped.Add(new FileMappingResult
                {
                    ZipEntry           = entry,
                    RelativeTargetPath = Normalize(target),
                    MappingMethod      = MappingMethod.ModInfo
                });
            }
            else
            {
                unmatched.Add(entry);
            }
        }

        return new MappingPlan { Mapped = mapped, Ambiguous = [], Unmatched = unmatched, Collisions = [] };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Strategy 2 — path-overlay
    // ─────────────────────────────────────────────────────────────────────────

    private static MappingPlan? TryPathOverlay(
        IReadOnlyList<ZipEntryInfo> zipEntries,
        string dataRoot,
        IReadOnlyList<string> dataFileIndex)
    {
        var topLevelDirs = zipEntries
            .Select(e => GetTopLevelDir(e.FullNameInZip))
            .Where(d => d is not null)
            .Select(d => d!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Variant A: Zip root is exactly "data/" — strip it and overlay the remainder.
        bool hasDataRoot = topLevelDirs.Count == 1
            && string.Equals(topLevelDirs[0], "data", StringComparison.OrdinalIgnoreCase);

        if (hasDataRoot)
            return BuildOverlayPlan(zipEntries, StripLeadingSegment, MappingMethod.PathOverlay);

        // Variant B: Every top-level zip directory matches a known child of the data root.
        if (topLevelDirs.Count > 0 && topLevelDirs.All(dir =>
                Directory.Exists(Path.Combine(dataRoot, dir))
                || dataFileIndex.Any(f =>
                    f.StartsWith(dir + "/", StringComparison.OrdinalIgnoreCase))))
        {
            return BuildOverlayPlan(zipEntries, IdentityTransform, MappingMethod.PathOverlay);
        }

        return null; // Neither variant matched; fall through to filename-index.
    }

    private static MappingPlan BuildOverlayPlan(
        IReadOnlyList<ZipEntryInfo> zipEntries,
        Func<string, string> pathTransform,
        MappingMethod method)
    {
        var mapped    = new List<FileMappingResult>();
        var unmatched = new List<ZipEntryInfo>();

        foreach (var entry in zipEntries)
        {
            if (IsModInfoFile(entry.FileName)) continue;

            string relative = Normalize(pathTransform(entry.FullNameInZip));
            if (string.IsNullOrEmpty(relative))
            {
                unmatched.Add(entry);
                continue;
            }

            mapped.Add(new FileMappingResult
            {
                ZipEntry           = entry,
                RelativeTargetPath = relative,
                MappingMethod      = method
            });
        }

        return new MappingPlan { Mapped = mapped, Ambiguous = [], Unmatched = unmatched, Collisions = [] };
    }

    private static string StripLeadingSegment(string zipPath)
    {
        // "data/vehicles/car_a/livery.dds"  →  "vehicles/car_a/livery.dds"
        int slash = zipPath.IndexOf('/');
        return slash >= 0 ? zipPath[(slash + 1)..] : string.Empty;
    }

    private static string IdentityTransform(string zipPath) => zipPath;

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
            if (IsModInfoFile(entry.FileName)) continue;

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

    private static bool IsModInfoFile(string fileName) =>
        string.Equals(fileName, "modinfo.json", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        path.Replace('\\', '/').TrimStart('/');

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
            .GroupBy(m => m.RelativeTargetPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var collisions = groups
            .Where(g => g.Count() > 1)
            .Select(g => new CollisionMapping
            {
                RelativeTargetPath = g.Key,
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
            .Select(c => c.RelativeTargetPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var safeMapped = plan.Mapped
            .Where(m => !collidingPaths.Contains(m.RelativeTargetPath))
            .ToList();

        return new MappingPlan
        {
            Mapped     = safeMapped,
            Ambiguous  = plan.Ambiguous,
            Unmatched  = plan.Unmatched,
            Collisions = collisions
        };
    }
}

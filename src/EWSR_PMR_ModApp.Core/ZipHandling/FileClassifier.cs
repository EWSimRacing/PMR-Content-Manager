using System.Text.RegularExpressions;

namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Classifies each zip entry into a <see cref="SkipCategory"/> based on extension,
/// path structure, and optional <see cref="ModInfo"/> overrides.
/// </summary>
public static class FileClassifier
{
    private static readonly HashSet<string> UnsafeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".bat", ".cmd", ".ps1", ".sh", ".vbs", ".msi", ".reg"
    };

    private static readonly HashSet<string> DisplayOnlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".txt", ".pdf"
    };

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    private static readonly HashSet<string> GameDataExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xml", ".hadron", ".tweakers", ".i3d", ".dds", ".ini", ".cfg", ".bin", ".lut", ".json"
    };

    private static readonly HashSet<string> PackagingArtifactExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bak", ".log", ".tmp", ".cache", ".zip", ".rar", ".7z"
    };

    private static readonly HashSet<string> PackagingArtifactFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "thumbs.db", ".ds_store"
    };

    private static readonly HashSet<string> DisplayOnlyFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "preview", "images", "screenshots"
    };

    /// <summary>
    /// Classifies a single zip entry. Returns the <see cref="SkipCategory"/> and, when not
    /// <see cref="SkipCategory.Install"/>, a human-readable reason string (via <paramref name="reason"/>).
    /// </summary>
    public static SkipCategory Classify(ZipEntryInfo entry, ModInfo? modInfo, out string? reason)
    {
        string zipPath = entry.FullNameInZip;
        string fileName = entry.FileName;
        string ext = Path.GetExtension(fileName);

        // 1. modinfo.json SkipFiles glob patterns → UserExcluded
        if (modInfo?.SkipFiles is { Count: > 0 })
        {
            foreach (var pattern in modInfo.SkipFiles)
            {
                if (MatchesGlob(zipPath, pattern))
                {
                    reason = $"Matched modinfo skip rule: {pattern}";
                    return SkipCategory.UserExcluded;
                }
            }
        }

        // 2. modinfo.json DisplayFiles override → DisplayOnly
        if (modInfo?.DisplayFiles is { Count: > 0 }
            && modInfo.DisplayFiles.ContainsKey(zipPath))
        {
            var info = modInfo.DisplayFiles[zipPath];
            reason = $"Declared display-only in modinfo.json (type: {info.Type ?? "other"})";
            return SkipCategory.DisplayOnly;
        }

        // 3. Unsafe extensions — always blocked
        if (IsUnsafe(ext))
        {
            reason = $"Executable or system file type ({ext}) — never auto-installed";
            return SkipCategory.UnsafeFile;
        }

        // 4. Packaging artifacts
        if (IsPackagingArtifact(zipPath, fileName))
        {
            reason = "Packaging artifact — always skipped";
            return SkipCategory.NoPathMatch;
        }

        // 5. modinfo.json at zip root → MetaFile
        if (string.Equals(fileName, "modinfo.json", StringComparison.OrdinalIgnoreCase)
            && IsAtZipRoot(zipPath))
        {
            reason = "Mod metadata file — parsed, not installed";
            return SkipCategory.MetaFile;
        }

        // 5b. modinfo.json explicit Files mapping → Install (wins before extension policy)
        if (modInfo?.Files is { Count: > 0 } && modInfo.Files.ContainsKey(zipPath))
        {
            reason = null;
            return SkipCategory.Install;
        }

        // 6. Documentation extensions (.md, .txt, .pdf)
        if (DisplayOnlyExtensions.Contains(ext))
        {
            // Inside data/ is unusual for docs — mark NoPathMatch
            if (IsInsideDataPath(zipPath))
            {
                reason = $"Documentation file ({ext}) found inside data/ path — not a valid game file";
                return SkipCategory.NoPathMatch;
            }
            reason = $"Documentation file ({ext}) — display-only";
            return SkipCategory.DisplayOnly;
        }

        // 7. Image files — conditional
        if (ImageExtensions.Contains(ext))
        {
            if (IsInsideDataPath(zipPath))
            {
                reason = null;
                return SkipCategory.Install;
            }
            if (IsDisplayOnlyLocation(zipPath))
            {
                reason = $"Image at display-only location — shown in UI, not installed";
                return SkipCategory.DisplayOnly;
            }
            reason = $"Image file ({ext}) not in a known game texture path";
            return SkipCategory.NoPathMatch;
        }

        // 8. Game data formats (includes .json not already handled as MetaFile)
        if (GameDataExtensions.Contains(ext))
        {
            if (IsInsideDataPath(zipPath))
            {
                reason = null;
                return SkipCategory.Install;
            }
            reason = $"Game data file ({ext}) is not inside a data/ path";
            return SkipCategory.NoPathMatch;
        }

        // 9. Everything else
        reason = "No known mapping for this file type or path";
        return SkipCategory.NoPathMatch;
    }

    /// <summary>
    /// Convenience overload that discards the reason string.
    /// </summary>
    public static SkipCategory Classify(ZipEntryInfo entry, ModInfo? modInfo)
        => Classify(entry, modInfo, out _);

    /// <summary>
    /// Returns <see langword="true"/> if the entry is at the zip root (no directory segment).
    /// </summary>
    private static bool IsAtZipRoot(string zipPath)
        => !zipPath.TrimStart('/').Contains('/');

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="zipPath"/> is at the zip root or inside
    /// a folder conventionally used for preview images.
    /// </summary>
    public static bool IsDisplayOnlyLocation(string zipPath)
    {
        if (IsAtZipRoot(zipPath)) return true;

        // First path segment
        int slash = zipPath.IndexOf('/');
        if (slash > 0)
        {
            string segment = zipPath[..slash];
            return DisplayOnlyFolders.Contains(segment);
        }
        return false;
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="zipPath"/> starts with <c>data/</c>
    /// (case-insensitive).
    /// </summary>
    public static bool IsInsideDataPath(string zipPath)
        => zipPath.StartsWith("data/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns <see langword="true"/> for file extensions that are never safe to auto-install.
    /// </summary>
    public static bool IsUnsafe(string extension)
        => UnsafeExtensions.Contains(extension);

    /// <summary>
    /// Returns <see langword="true"/> if the entry is a known packaging artifact
    /// (by extension, filename, or path prefix).
    /// </summary>
    public static bool IsPackagingArtifact(string zipPath, string fileName)
    {
        if (PackagingArtifactExtensions.Contains(Path.GetExtension(fileName)))
            return true;

        if (PackagingArtifactFileNames.Contains(fileName))
            return true;

        // __MACOSX/** and .git/** prefixes
        if (zipPath.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (zipPath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    /// <summary>
    /// Matches a <paramref name="path"/> against a simple glob <paramref name="pattern"/>.
    /// Supports <c>*</c> (any chars within one segment) and <c>?</c> (single char).
    /// A leading <c>*</c> or <c>**</c> matches across segments (used for <c>*.bak</c>,
    /// <c>__MACOSX/*</c>, etc.).
    /// </summary>
    public static bool MatchesGlob(string path, string pattern)
    {
        // Convert glob to regex
        string escaped = Regex.Escape(pattern)
            .Replace(@"\*\*", ".*")   // ** → any chars including /
            .Replace(@"\*", "[^/]*")  // * → any chars except /
            .Replace(@"\?", ".");     // ? → single char

        // If pattern has no path separator, match against filename only
        bool hasSlash = pattern.Contains('/');
        string subject = hasSlash ? path : Path.GetFileName(path);

        return Regex.IsMatch(subject, $"^{escaped}$", RegexOptions.IgnoreCase);
    }
}

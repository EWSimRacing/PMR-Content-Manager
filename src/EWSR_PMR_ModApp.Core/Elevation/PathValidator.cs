using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// Security validation helpers used by both the elevated Helper process and <see cref="InProcessWriter"/>
/// before performing any file operation.
/// </summary>
public static class PathValidator
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="relativePath"/> resolves to a location
    /// that is strictly inside <paramref name="dataRoot"/>.
    /// </summary>
    /// <remarks>
    /// Rejects:
    /// <list type="bullet">
    ///   <item>Rooted (absolute) paths.</item>
    ///   <item>Paths containing <c>..</c> traversal that would escape the root.</item>
    /// </list>
    /// Uses <see cref="Path.GetFullPath"/> normalisation to catch encoded or multi-separator tricks.
    /// </remarks>
    public static bool IsUnderDataRoot(string dataRoot, string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return false;
        if (Path.IsPathRooted(relativePath))    return false;

        string normalizedRoot = Path.GetFullPath(dataRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        string combined = Path.GetFullPath(Path.Combine(dataRoot, relativePath));
        return combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="sourcePath"/> is located inside
    /// <c>%APPDATA%\EWSR_PMR_ModApp\</c> (the app's staging area and payload cache).
    /// </summary>
    /// <param name="sourcePath">Absolute path of the source file to validate.</param>
    /// <param name="appDataRoot">
    /// The app's AppData root directory — pass <see cref="AppPaths.AppDataRoot"/>.
    /// Accepts this as a parameter so the helper can call it without duplicating the path logic.
    /// </param>
    public static bool IsAllowedSource(string sourcePath, string appDataRoot)
    {
        if (string.IsNullOrEmpty(sourcePath)) return false;

        string normalizedRoot   = Path.GetFullPath(appDataRoot)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string normalizedSource = Path.GetFullPath(sourcePath);

        return normalizedSource.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
    }
}

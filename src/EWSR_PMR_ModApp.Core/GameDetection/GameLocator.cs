using System.Text.RegularExpressions;
using Microsoft.Win32;
using EWSR_PMR_ModApp.Core.Abstractions;

namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>
/// Implements <see cref="IGameLocator"/> with a four-tier fallback strategy:
/// PMR junction → user config → default path → Steam detection.
/// </summary>
/// <remarks>
/// PMR creates an NTFS junction at <c>%LOCALAPPDATA%\PMR_data</c> pointing to the game's
/// actual <c>data\</c> directory. The game engine reads from this junction path, so it is
/// the most reliable way to locate the data root regardless of install location.
/// </remarks>
public sealed class GameLocator : IGameLocator
{
    private const string DefaultDataRoot   = @"C:\Program Files\Project Motor Racing\data";
    private const string GameFolderName    = "Project Motor Racing";

    /// <summary>
    /// PMR creates this junction at <c>%LOCALAPPDATA%\PMR_data</c> pointing to the actual
    /// game data directory. The game engine reads data through this path.
    /// </summary>
    private static readonly string PmrJunctionPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PMR_data");

    // Sub-folders that must exist under the data root for it to be considered valid.
    private static readonly string[] KnownDataSubfolders =
        ["vehicles", "tracks", "configs", "sounds"];

    private readonly IFileSystem _fs;

    public GameLocator(IFileSystem fileSystem) => _fs = fileSystem;

    // -------------------------------------------------------------------------

    public async Task<GameLocatorResult> LocateAsync(
        string? userConfiguredPath = null,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        // (a) PMR junction — the game engine reads from %LOCALAPPDATA%\PMR_data which is a
        //     junction pointing to the real data directory. This is the most reliable source
        //     because it matches exactly where the game reads from.
        string? junctionTarget = TryResolveJunction();
        if (junctionTarget is not null && ValidateDataRoot(junctionTarget))
        {
            var result = Found(junctionTarget, LocationSource.JunctionResolved);

            // If the user configured a DIFFERENT path, warn them.
            if (!string.IsNullOrWhiteSpace(userConfiguredPath)
                && !PathsAreEquivalent(userConfiguredPath, junctionTarget))
            {
                return result with
                {
                    Warning = $"Your configured path '{userConfiguredPath}' differs from where the game " +
                              $"actually reads data ({junctionTarget}). Using the game's active data path."
                };
            }

            return result;
        }

        // (b) User-configured path — if the junction is missing, fall back to manual config.
        if (!string.IsNullOrWhiteSpace(userConfiguredPath))
        {
            if (ValidateDataRoot(userConfiguredPath))
                return Found(userConfiguredPath, LocationSource.UserConfigured);

            // Try appending \data in case user pointed to the game root.
            string withData = Path.Combine(userConfiguredPath, "data");
            if (ValidateDataRoot(withData))
                return Found(withData, LocationSource.UserConfigured);

            var detail = ValidateDataRootDetailed(userConfiguredPath);
            return GameLocatorResult.NotFound(
                detail.Reason ?? $"User-configured path '{userConfiguredPath}' is not a valid data folder.");
        }

        // (c) Hard-coded default.
        if (ValidateDataRoot(DefaultDataRoot))
            return Found(DefaultDataRoot, LocationSource.DefaultPath);

        // (d) Steam detection — best-effort, never throws.
        string? steamPath = await TryDetectViaSteamAsync(ct).ConfigureAwait(false);
        if (steamPath is not null && ValidateDataRoot(steamPath))
            return Found(steamPath, LocationSource.SteamDetected);

        return GameLocatorResult.NotFound(
            "Game data folder could not be detected automatically. Please set the path in Settings.");
    }

    public bool ValidateDataRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !_fs.DirectoryExists(path))
            return false;

        // At least one known sub-folder must be present to confirm this is the right directory.
        return KnownDataSubfolders.Any(sub => _fs.DirectoryExists(Path.Combine(path, sub)));
    }

    public DataRootValidation ValidateDataRootDetailed(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new DataRootValidation(false, path ?? "", false, [], KnownDataSubfolders);

        bool exists = _fs.DirectoryExists(path);
        if (!exists)
            return new DataRootValidation(false, path, false, [], KnownDataSubfolders);

        var found = KnownDataSubfolders
            .Where(sub => _fs.DirectoryExists(Path.Combine(path, sub)))
            .ToList();
        var missing = KnownDataSubfolders
            .Where(sub => !_fs.DirectoryExists(Path.Combine(path, sub)))
            .ToList();

        return new DataRootValidation(found.Count > 0, path, true, found, missing);
    }

    public bool CanWriteDataRoot(string dataRoot) => _fs.CanWriteDirectory(dataRoot);

    private static GameLocatorResult Found(string dataRoot, LocationSource source) =>
        new(true, dataRoot, Directory.GetParent(dataRoot)?.FullName, source);

    // -------------------------------------------------------------------------
    // PMR junction resolution
    // -------------------------------------------------------------------------

    private string? TryResolveJunction()
    {
        try
        {
            // First check if the junction path exists and is a reparse point.
            string? target = _fs.ResolveJunctionTarget(PmrJunctionPath);
            if (target is not null)
                return target;

            // Fall back: even if it's not a reparse point, PMR_data could be a real directory
            // (e.g. user copied data there). Check if it's a valid data root directly.
            if (_fs.DirectoryExists(PmrJunctionPath))
                return PmrJunctionPath;
        }
        catch
        {
            // Junction resolution is best-effort.
        }

        return null;
    }

    private static bool PathsAreEquivalent(string path1, string path2)
    {
        string a = Path.GetFullPath(path1).TrimEnd(Path.DirectorySeparatorChar);
        string b = Path.GetFullPath(path2).TrimEnd(Path.DirectorySeparatorChar);
        return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Steam detection helpers
    // -------------------------------------------------------------------------

    private Task<string?> TryDetectViaSteamAsync(CancellationToken ct) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                string? steamDir = GetSteamInstallDir();
                if (steamDir is null) return null;

                foreach (string libPath in EnumerateSteamLibraries(steamDir))
                {
                    ct.ThrowIfCancellationRequested();
                    string candidate = Path.Combine(
                        libPath, "steamapps", "common", GameFolderName, "data");
                    if (_fs.DirectoryExists(candidate))
                        return candidate;
                }
            }
            catch
            {
                // Steam detection is best-effort; swallow all errors.
            }
            return null;
        }, ct);

    private static string? GetSteamInstallDir()
    {
        // Try 64-bit registry node, then WoW6432Node for 32-bit Steam installs.
        return ReadRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath")
            ?? ReadRegistryValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
    }

    private static string? ReadRegistryValue(string keyPath, string valueName)
    {
        try { return Registry.GetValue(keyPath, valueName, null) as string; }
        catch { return null; }
    }

    private IEnumerable<string> EnumerateSteamLibraries(string steamDir)
    {
        // The primary library is always inside the Steam install directory.
        yield return steamDir;

        string vdfPath = Path.Combine(steamDir, "steamapps", "libraryfolders.vdf");
        if (!_fs.FileExists(vdfPath)) yield break;

        string vdf = _fs.ReadAllText(vdfPath);

        // Parse all "path" entries from the VDF with a simple regex.
        // VDF format:  "path"    "D:\\SteamLibrary"
        foreach (Match m in Regex.Matches(vdf, @"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase))
        {
            string libPath = m.Groups[1].Value.Replace(@"\\", @"\");
            if (_fs.DirectoryExists(libPath))
                yield return libPath;
        }
    }
}

using System.Text.RegularExpressions;
using Microsoft.Win32;
using EWSR_PMR_ModApp.Core.Abstractions;

namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>
/// Implements <see cref="IGameLocator"/> with a three-tier fallback strategy:
/// user config → default path → Steam detection.
/// </summary>
public sealed class GameLocator : IGameLocator
{
    private const string DefaultDataRoot   = @"C:\Program Files\Project Motor Racing\data";
    private const string GameFolderName    = "Project Motor Racing";

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

        // (a) User-configured path has highest priority.
        if (!string.IsNullOrWhiteSpace(userConfiguredPath))
        {
            return ValidateDataRoot(userConfiguredPath)
                ? Found(userConfiguredPath, LocationSource.UserConfigured)
                : GameLocatorResult.NotFound(
                    $"User-configured path '{userConfiguredPath}' does not exist or is not a valid data folder.");
        }

        // (b) Hard-coded default.
        if (ValidateDataRoot(DefaultDataRoot))
            return Found(DefaultDataRoot, LocationSource.DefaultPath);

        // (c) Steam detection — best-effort, never throws.
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

    public bool CanWriteDataRoot(string dataRoot) => _fs.CanWriteDirectory(dataRoot);

    private static GameLocatorResult Found(string dataRoot, LocationSource source) =>
        new(true, dataRoot, Directory.GetParent(dataRoot)?.FullName, source);

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

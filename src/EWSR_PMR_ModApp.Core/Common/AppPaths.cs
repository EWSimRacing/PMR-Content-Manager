namespace EWSR_PMR_ModApp.Core.Common;

/// <summary>
/// Central registry of all persistent storage locations used by the application.
/// Everything lives under <c>%APPDATA%\EWSR_PMR_ModApp\</c>.
/// </summary>
public static class AppPaths
{
    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EWSR_PMR_ModApp");

    /// <summary>Master manifest file: <c>%APPDATA%\EWSR_PMR_ModApp\manifest.json</c></summary>
    public static string ManifestFile => Path.Combine(AppDataRoot, "manifest.json");

    /// <summary>Original-file backups: <c>%APPDATA%\EWSR_PMR_ModApp\backups\{modId}\</c></summary>
    public static string BackupsRoot => Path.Combine(AppDataRoot, "backups");

    /// <summary>Cached mod payloads for re-apply: <c>%APPDATA%\EWSR_PMR_ModApp\mods\{modId}\</c></summary>
    public static string ModsCache => Path.Combine(AppDataRoot, "mods");

    /// <summary>Transient staging area for zip extraction: <c>%APPDATA%\EWSR_PMR_ModApp\staging\</c></summary>
    public static string StagingRoot => Path.Combine(AppDataRoot, "staging");

    public static string BackupDirForMod(string modId)  => Path.Combine(BackupsRoot, modId);
    public static string PayloadDirForMod(string modId) => Path.Combine(ModsCache,   modId);

    /// <summary>Cached hook scripts for a mod: <c>%APPDATA%\EWSR_PMR_ModApp\scripts\{modId}\</c></summary>
    public static string ScriptsDirForMod(string modId) => Path.Combine(AppDataRoot, "scripts", modId);

    public static string StagingDirForSession(string sessionId) =>
        Path.Combine(StagingRoot, sessionId);
}

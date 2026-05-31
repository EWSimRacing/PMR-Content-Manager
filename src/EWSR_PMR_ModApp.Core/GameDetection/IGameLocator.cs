namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>
/// Resolves the Project Motor Racing <c>data</c> root directory.
/// </summary>
public interface IGameLocator
{
    /// <summary>
    /// Attempts to resolve the data root using this priority order:
    /// <list type="number">
    ///   <item>(a) <paramref name="userConfiguredPath"/> — if provided and valid.</item>
    ///   <item>(b) The default path <c>C:\Program Files\Project Motor Racing\data</c>.</item>
    ///   <item>(c) Steam library detection via libraryfolders.vdf and Windows Registry (best-effort).</item>
    /// </list>
    /// Returns a result with <c>Found = false</c> when none of the above succeed, prompting the
    /// caller to ask the user to select the path manually.
    /// </summary>
    Task<GameLocatorResult> LocateAsync(
        string? userConfiguredPath = null,
        CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="path"/> exists on disk and contains at least one
    /// expected sub-folder (e.g. <c>vehicles</c>, <c>tracks</c>).
    /// </summary>
    bool ValidateDataRoot(string path);

    /// <summary>
    /// Returns <c>true</c> if the calling process has write access to <paramref name="dataRoot"/>.
    /// NOTE: Program Files is ACL-protected — admin elevation is typically required at runtime.
    /// </summary>
    bool CanWriteDataRoot(string dataRoot);
}

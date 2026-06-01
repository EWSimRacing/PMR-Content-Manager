namespace EWSR_PMR_ModApp.Core.ZipHandling;

public sealed record InstallResult(
    bool Success,
    IReadOnlyList<ZipEntryInfo> Installed,
    IReadOnlyList<SkippedFile> Skipped,
    IReadOnlyList<string> Warnings
);

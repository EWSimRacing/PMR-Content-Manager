namespace EWSR_PMR_ModApp.Core.ZipHandling;

public sealed record SkippedFile(
    string PathInZip,
    SkipCategory Category,
    string Reason
);

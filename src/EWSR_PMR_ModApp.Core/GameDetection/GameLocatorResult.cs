namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>Result returned by <see cref="IGameLocator.LocateAsync"/>.</summary>
public sealed record GameLocatorResult(
    bool           Found,
    string?        DataRoot,
    string?        GameRoot,
    LocationSource Source,
    string?        FailureReason = null)
{
    public static GameLocatorResult NotFound(string reason) =>
        new(false, null, null, LocationSource.NotFound, reason);
}

namespace EWSR_PMR_ModApp.Core.GameDetection;

/// <summary>Result returned by <see cref="IGameLocator.LocateAsync"/>.</summary>
public sealed record GameLocatorResult(
    bool           Found,
    string?        DataRoot,
    LocationSource Source,
    string?        FailureReason = null)
{
    public static GameLocatorResult NotFound(string reason) =>
        new(false, null, LocationSource.NotFound, reason);
}

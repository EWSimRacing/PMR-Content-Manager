namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>A package cannot be installed through the managed game-file copy workflow.</summary>
public sealed class UnsupportedModPackageException : InvalidOperationException
{
    public UnsupportedModPackageException(string message) : base(message) { }
}

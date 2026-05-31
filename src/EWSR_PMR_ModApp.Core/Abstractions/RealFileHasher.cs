using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.Core.Abstractions;

/// <summary>
/// Production implementation of <see cref="IFileHasher"/> that delegates to
/// <see cref="HashHelper.ComputeFileHash"/>.
/// </summary>
public sealed class RealFileHasher : IFileHasher
{
    public string ComputeHash(string filePath) => HashHelper.ComputeFileHash(filePath);
}

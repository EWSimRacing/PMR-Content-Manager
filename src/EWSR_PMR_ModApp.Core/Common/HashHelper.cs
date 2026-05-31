using System.Security.Cryptography;

namespace EWSR_PMR_ModApp.Core.Common;

/// <summary>SHA-256 hashing utilities used by the sync engine and manifest.</summary>
public static class HashHelper
{
    /// <summary>Computes a lower-case SHA-256 hex digest for the file at <paramref name="filePath"/>.</summary>
    public static string ComputeFileHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        return ComputeStreamHash(stream);
    }

    /// <summary>Computes a lower-case SHA-256 hex digest for the given stream (does not reset position).</summary>
    public static string ComputeStreamHash(Stream stream)
    {
        byte[] bytes = SHA256.HashData(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Computes a lower-case SHA-256 hex digest for a byte array.</summary>
    public static string ComputeBytesHash(byte[] data)
    {
        byte[] bytes = SHA256.HashData(data);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

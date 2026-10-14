namespace EWSR_PMR_ModApp.Core.ZipHandling;

public static class PackagePaths
{
    /// <summary>Only bounded wrappers with an explicit data/ anchor are inferred.</summary>
    public static bool TryDataPath(string path, out string prefix, out string relative)
    {
        string[] segments = path.Replace('\\', '/').Split('/');
        int data = Array.FindIndex(segments, s => string.Equals(s, "data", StringComparison.OrdinalIgnoreCase));
        if (data >= 0 && data <= 3 && data < segments.Length - 1)
        {
            prefix = string.Join("/", segments.Take(data + 1)) + "/";
            relative = string.Join("/", segments.Skip(data + 1));
            return true;
        }
        prefix = relative = string.Empty;
        return false;
    }
}

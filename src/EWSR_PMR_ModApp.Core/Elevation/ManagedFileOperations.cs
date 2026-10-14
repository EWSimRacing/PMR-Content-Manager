namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>Temporarily handles only the ReadOnly attribute; never changes access-control rules.</summary>
public static class ManagedFileOperations
{
    public static void Copy(string source, string destination, bool overwrite, bool restoreSourceAttributes = false)
    {
        bool exists = File.Exists(destination);
        if (exists && !overwrite)
        {
            File.Copy(source, destination, false);
            return;
        }
        FileAttributes desired = exists && !restoreSourceAttributes
            ? File.GetAttributes(destination) : File.GetAttributes(source);
        FileAttributes? original = exists ? File.GetAttributes(destination) : null;
        Exception? copyError = null;
        try
        {
            if (original is { } attributes && (attributes & FileAttributes.ReadOnly) != 0)
                File.SetAttributes(destination, attributes & ~FileAttributes.ReadOnly);
            if (exists)
            {
                // Open the existing file, preserving its ACL and non-ReadOnly attributes.
                // CopyFile's hidden-destination rules must not lead to clearing unrelated flags.
                using var input = File.OpenRead(source);
                using var output = File.Open(destination, FileMode.Open, FileAccess.Write, FileShare.None);
                output.SetLength(0);
                input.CopyTo(output);
            }
            else File.Copy(source, destination, overwrite);
            if (exists) File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(source));
            if (File.GetAttributes(destination) != desired) File.SetAttributes(destination, desired);
        }
        catch (Exception ex)
        {
            copyError = ex;
            throw;
        }
        finally
        {
            if (copyError is not null && original is { } attributes && File.Exists(destination))
            {
                try
                {
                    if (File.GetAttributes(destination) != attributes) File.SetAttributes(destination, attributes);
                }
                catch (Exception restoreError)
                {
                    throw new IOException($"{copyError.Message}; could not restore original file attributes: {restoreError.Message}",
                        new AggregateException(copyError, restoreError));
                }
            }
        }
    }

    public static void Delete(string path)
    {
        if (!File.Exists(path)) return;
        var original = File.GetAttributes(path);
        try
        {
            if ((original & FileAttributes.ReadOnly) != 0) File.SetAttributes(path, original & ~FileAttributes.ReadOnly);
            File.Delete(path);
        }
        finally
        {
            if (File.Exists(path) && File.GetAttributes(path) != original) File.SetAttributes(path, original);
        }
    }

    public static void DeleteDirectory(string path, bool recursive)
    {
        if (recursive && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                    DeleteDirectory(entry, (attributes & FileAttributes.ReparsePoint) == 0);
                else if ((attributes & FileAttributes.ReparsePoint) != 0)
                    File.Delete(entry);
                else
                    Delete(entry);
            }
        Directory.Delete(path, recursive: false);
    }
}

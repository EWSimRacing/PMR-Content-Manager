namespace EWSR_PMR_ModApp.Core.Abstractions;

/// <summary>
/// Production implementation of <see cref="IFileSystem"/> that delegates to <c>System.IO</c>.
/// </summary>
public sealed class RealFileSystem : IFileSystem
{
    public bool FileExists(string path)      => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath, bool overwrite)
    {
        // NOTE: Writes to C:\Program Files require admin elevation at runtime.
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite);
    }

    public void DeleteFile(string path) => File.Delete(path);

    public void DeleteDirectory(string path, bool recursive) =>
        Directory.Delete(path, recursive);

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents) =>
        File.WriteAllText(path, contents);

    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        Directory.EnumerateFiles(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateDirectories(string path) =>
        Directory.EnumerateDirectories(path);

    public Stream OpenRead(string path)   => File.OpenRead(path);
    public Stream CreateFile(string path) => File.Create(path);

    public bool CanWriteDirectory(string path)
    {
        if (!Directory.Exists(path)) return false;
        string probe = Path.Combine(path, $".write_probe_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

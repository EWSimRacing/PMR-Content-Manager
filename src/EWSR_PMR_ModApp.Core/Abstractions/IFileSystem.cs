namespace EWSR_PMR_ModApp.Core.Abstractions;

/// <summary>
/// Abstracts file-system operations so Core logic can be unit-tested without touching disk.
/// </summary>
public interface IFileSystem
{
    bool FileExists(string path);
    bool DirectoryExists(string path);
    void CreateDirectory(string path);

    /// <summary>
    /// Copies a file, creating the destination directory if it doesn't exist.
    /// NOTE: Writing to C:\Program Files requires admin elevation at runtime —
    /// callers must ensure sufficient permissions before copying to game-data paths.
    /// </summary>
    void CopyFile(string sourcePath, string destinationPath, bool overwrite);

    void DeleteFile(string path);
    void DeleteDirectory(string path, bool recursive);

    string ReadAllText(string path);
    void WriteAllText(string path, string contents);

    IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
    IEnumerable<string> EnumerateDirectories(string path);

    Stream OpenRead(string path);
    Stream CreateFile(string path);

    /// <summary>
    /// Probes write access by attempting a transient file write.
    /// Returns false if the directory does not exist or the write is denied.
    /// </summary>
    bool CanWriteDirectory(string path);
}

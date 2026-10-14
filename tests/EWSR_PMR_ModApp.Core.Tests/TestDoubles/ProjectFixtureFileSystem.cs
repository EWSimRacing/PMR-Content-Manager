using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.Core.Tests.TestDoubles;

/// <summary>Real disk I/O with app state redirected into an isolated project-local fixture.</summary>
public sealed class ProjectFixtureFileSystem(string fixtureRoot) : IFileSystem
{
    private readonly RealFileSystem _real = new();
    private readonly string _state = Path.Combine(fixtureRoot, "app-state");
    public string Resolve(string path) =>
        path.Equals(AppPaths.AppDataRoot, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith(AppPaths.AppDataRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(_state, Path.GetRelativePath(AppPaths.AppDataRoot, path))
            : path;
    private string Original(string path) =>
        path.StartsWith(_state + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(AppPaths.AppDataRoot, Path.GetRelativePath(_state, path)) : path;
    public bool FileExists(string path) => _real.FileExists(Resolve(path));
    public bool DirectoryExists(string path) => _real.DirectoryExists(Resolve(path));
    public void CreateDirectory(string path) => _real.CreateDirectory(Resolve(path));
    public void CopyFile(string sourcePath, string destinationPath, bool overwrite, bool restoreSourceAttributes = false) =>
        _real.CopyFile(Resolve(sourcePath), Resolve(destinationPath), overwrite, restoreSourceAttributes);
    public FileAttributes GetFileAttributes(string path) => _real.GetFileAttributes(Resolve(path));
    public void SetFileAttributes(string path, FileAttributes attributes) => _real.SetFileAttributes(Resolve(path), attributes);
    public void DeleteFile(string path) => _real.DeleteFile(Resolve(path));
    public void DeleteDirectory(string path, bool recursive) => _real.DeleteDirectory(Resolve(path), recursive);
    public string ReadAllText(string path) => _real.ReadAllText(Resolve(path));
    public void WriteAllText(string path, string contents) => _real.WriteAllText(Resolve(path), contents);
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
        _real.EnumerateFiles(Resolve(path), searchPattern, searchOption).Select(Original);
    public IEnumerable<string> EnumerateDirectories(string path) => _real.EnumerateDirectories(Resolve(path)).Select(Original);
    public Stream OpenRead(string path) => _real.OpenRead(Resolve(path));
    public Stream CreateFile(string path) => _real.CreateFile(Resolve(path));
    public bool CanWriteDirectory(string path) => false;
    public string? ResolveJunctionTarget(string path) => _real.ResolveJunctionTarget(Resolve(path));
}

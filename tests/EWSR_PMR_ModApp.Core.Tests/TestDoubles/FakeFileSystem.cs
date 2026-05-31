using EWSR_PMR_ModApp.Core.Abstractions;

namespace EWSR_PMR_ModApp.Core.Tests.TestDoubles;

/// <summary>
/// In-memory <see cref="IFileSystem"/> for unit tests.
/// Thread-safety is intentionally minimal — single-threaded test usage assumed.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    // file path → text content
    private readonly Dictionary<string, string> _textFiles =
        new(StringComparer.OrdinalIgnoreCase);

    // known directories (created explicitly or auto-inferred when a file is added)
    private readonly HashSet<string> _directories =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly bool _canWrite;

    public FakeFileSystem(bool canWrite = true) => _canWrite = canWrite;

    // ── Setup helpers used by tests ───────────────────────────────────────────

    /// <summary>Adds a directory to the in-memory tree, also adding all ancestor directories.</summary>
    public FakeFileSystem AddDirectory(string path)
    {
        for (string? cur = Norm(path); cur != null && !_directories.Contains(cur); cur = Parent(cur))
            _directories.Add(cur);
        return this;
    }

    /// <summary>Adds a text file and auto-creates its ancestor directories.</summary>
    public FakeFileSystem AddFile(string path, string content = "")
    {
        _textFiles[Norm(path)] = content;
        AddDirectory(Path.GetDirectoryName(Norm(path)) ?? Norm(path));
        return this;
    }

    // ── IFileSystem ───────────────────────────────────────────────────────────

    public bool FileExists(string path)      => _textFiles.ContainsKey(Norm(path));
    public bool DirectoryExists(string path) => _directories.Contains(Norm(path));

    public void CreateDirectory(string path) => AddDirectory(path);

    public void CopyFile(string src, string dst, bool overwrite)
    {
        string content = _textFiles.TryGetValue(Norm(src), out var c) ? c : string.Empty;
        _textFiles[Norm(dst)] = content;
        AddDirectory(Path.GetDirectoryName(Norm(dst)) ?? Norm(dst));
    }

    public void DeleteFile(string path) => _textFiles.Remove(Norm(path));

    public void DeleteDirectory(string path, bool recursive)
    {
        string np = Norm(path);
        _directories.RemoveWhere(d => d.StartsWith(np, StringComparison.OrdinalIgnoreCase));
        if (recursive)
            foreach (var k in _textFiles.Keys
                .Where(k => k.StartsWith(np, StringComparison.OrdinalIgnoreCase)).ToList())
                _textFiles.Remove(k);
    }

    public string ReadAllText(string path)
    {
        if (_textFiles.TryGetValue(Norm(path), out var c)) return c;
        throw new FileNotFoundException($"FakeFileSystem: '{path}' not found.");
    }

    public void WriteAllText(string path, string contents)
    {
        _textFiles[Norm(path)] = contents;
        AddDirectory(Path.GetDirectoryName(Norm(path)) ?? Norm(path));
    }

    public IEnumerable<string> EnumerateFiles(
        string path, string searchPattern, SearchOption searchOption)
    {
        string np = Norm(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return _textFiles.Keys
            .Where(k => k.StartsWith(np, StringComparison.OrdinalIgnoreCase)
                        && (searchOption == SearchOption.AllDirectories
                            || !k[np.Length..].Contains(Path.DirectorySeparatorChar)));
    }

    public IEnumerable<string> EnumerateDirectories(string path)
    {
        string np = Norm(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return _directories
            .Where(d => d.StartsWith(np, StringComparison.OrdinalIgnoreCase)
                        && !d[np.Length..].Contains(Path.DirectorySeparatorChar)
                        && d.Length > np.Length);
    }

    public Stream OpenRead(string path)
    {
        string text = ReadAllText(path);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }

    public Stream CreateFile(string path)
    {
        var ms = new TrackingMemoryStream(content =>
        {
            _textFiles[Norm(path)] = System.Text.Encoding.UTF8.GetString(content);
        });
        return ms;
    }

    public bool CanWriteDirectory(string path) => _canWrite && DirectoryExists(path);

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Norm(string path) =>
        Path.GetFullPath(path.Replace('/', Path.DirectorySeparatorChar));

    private static string? Parent(string normedPath)
    {
        string? p = Path.GetDirectoryName(normedPath);
        return (p == null || p == normedPath) ? null : p;
    }

    /// <summary>A MemoryStream that fires a callback with the written bytes when disposed.</summary>
    private sealed class TrackingMemoryStream : MemoryStream
    {
        private readonly Action<byte[]> _onDispose;
        public TrackingMemoryStream(Action<byte[]> onDispose) => _onDispose = onDispose;
        protected override void Dispose(bool disposing)
        {
            if (disposing) _onDispose(ToArray());
            base.Dispose(disposing);
        }
    }
}

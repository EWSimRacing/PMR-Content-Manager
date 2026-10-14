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
    private readonly Dictionary<string, byte[]> _binaryFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileAttributes> _attributes = new(StringComparer.OrdinalIgnoreCase);

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
        _binaryFiles.Remove(Norm(path));
        _attributes.Remove(Norm(path));
        AddDirectory(Path.GetDirectoryName(Norm(path)) ?? Norm(path));
        return this;
    }

    public FakeFileSystem AddBytes(string path, byte[] content)
    {
        AddFile(path);
        _binaryFiles[Norm(path)] = content.ToArray();
        return this;
    }

    // ── IFileSystem ───────────────────────────────────────────────────────────

    public bool FileExists(string path)      => _textFiles.ContainsKey(Norm(path));
    public bool DirectoryExists(string path) => _directories.Contains(Norm(path));

    public void CreateDirectory(string path) => AddDirectory(path);

    public void CopyFile(string src, string dst, bool overwrite, bool restoreSourceAttributes = false)
    {
        var attributes = FileExists(dst) && !restoreSourceAttributes ? GetFileAttributes(dst) : GetFileAttributes(src);
        string content = _textFiles.TryGetValue(Norm(src), out var c) ? c : string.Empty;
        _textFiles[Norm(dst)] = content;
        _binaryFiles.Remove(Norm(dst));
        if (_binaryFiles.TryGetValue(Norm(src), out var bytes))
            _binaryFiles[Norm(dst)] = bytes.ToArray();
        AddDirectory(Path.GetDirectoryName(Norm(dst)) ?? Norm(dst));
        SetFileAttributes(dst, attributes);
    }
    public FileAttributes GetFileAttributes(string path) => _attributes.GetValueOrDefault(Norm(path), FileAttributes.Archive);
    public void SetFileAttributes(string path, FileAttributes attributes) => _attributes[Norm(path)] = attributes;

    public void DeleteFile(string path)
    {
        _textFiles.Remove(Norm(path));
        _binaryFiles.Remove(Norm(path));
        _attributes.Remove(Norm(path));
    }

    public void DeleteDirectory(string path, bool recursive)
    {
        string np = Norm(path);
        _directories.RemoveWhere(d => d.StartsWith(np, StringComparison.OrdinalIgnoreCase));
        if (recursive)
        {
            foreach (var k in _textFiles.Keys
                .Where(k => k.StartsWith(np, StringComparison.OrdinalIgnoreCase)).ToList())
                _textFiles.Remove(k);
            foreach (var k in _binaryFiles.Keys.Where(k => k.StartsWith(np, StringComparison.OrdinalIgnoreCase)).ToList())
                _binaryFiles.Remove(k);
            foreach (var k in _attributes.Keys.Where(k => k.StartsWith(np, StringComparison.OrdinalIgnoreCase)).ToList())
                _attributes.Remove(k);
        }
    }

    public string ReadAllText(string path)
    {
        if (_binaryFiles.TryGetValue(Norm(path), out var bytes))
        {
            using var reader = new StreamReader(new MemoryStream(bytes));
            return reader.ReadToEnd();
        }
        if (_textFiles.TryGetValue(Norm(path), out var c)) return c;
        throw new FileNotFoundException($"FakeFileSystem: '{path}' not found.");
    }

    public void WriteAllText(string path, string contents)
    {
        _textFiles[Norm(path)] = contents;
        _binaryFiles.Remove(Norm(path));
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
        if (_binaryFiles.TryGetValue(Norm(path), out var bytes)) return new MemoryStream(bytes, writable: false);
        string text = ReadAllText(path);
        return new MemoryStream(System.Text.Encoding.UTF8.GetBytes(text));
    }

    public Stream CreateFile(string path)
    {
        var ms = new TrackingMemoryStream(content =>
        {
            _textFiles[Norm(path)] = System.Text.Encoding.UTF8.GetString(content);
            _binaryFiles[Norm(path)] = content.ToArray();
        });
        return ms;
    }

    public bool CanWriteDirectory(string path) => _canWrite && DirectoryExists(path);

    private readonly Dictionary<string, string> _junctions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a fake junction mapping for testing.</summary>
    public void AddJunction(string junctionPath, string targetPath)
    {
        _junctions[Norm(junctionPath)] = Norm(targetPath);
        AddDirectory(junctionPath);
    }

    public string? ResolveJunctionTarget(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        return _junctions.TryGetValue(Norm(path), out var target) ? target : null;
    }

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

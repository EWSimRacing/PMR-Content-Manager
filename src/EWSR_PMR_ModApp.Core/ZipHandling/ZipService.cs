using System.IO.Compression;
using System.Text.Json;
using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;

namespace EWSR_PMR_ModApp.Core.ZipHandling;

/// <summary>
/// Concrete implementation of <see cref="IZipService"/> using <c>System.IO.Compression</c>.
/// </summary>
public sealed class ZipService : IZipService
{
    private readonly IFileSystem _fs;

    public ZipService(IFileSystem fileSystem) => _fs = fileSystem;

    public Task<bool> ValidateIntegrityAsync(string zipPath, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!_fs.FileExists(zipPath)) return false;
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                var buffer = new byte[81920];
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();
                    // Reading through each entry forces CRC verification.
                    using var stream = entry.Open();
                    while (stream.Read(buffer) > 0)
                        ct.ThrowIfCancellationRequested();
                }
                return true;
            }
            catch (InvalidDataException) { return false; }
        }, ct);

    public async Task<ZipStagingResult> StageAsync(
        string zipPath,
        IProgress<int>? progress = null,
        CancellationToken ct = default)
    {
        string sessionId  = Guid.NewGuid().ToString("N");
        string stagingDir = AppPaths.StagingDirForSession(sessionId);
        _fs.CreateDirectory(stagingDir);

        string  zipHash  = HashHelper.ComputeFileHash(zipPath);
        var     entries  = new List<ZipEntryInfo>();
        ModInfo? modInfo = null;

        await Task.Run(() =>
        {
            using var archive = ZipFile.OpenRead(zipPath);
            int total = archive.Entries.Count;
            int done  = 0;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();

                // Skip directory markers.
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
                {
                    done++;
                    continue;
                }

                string relativeName = entry.FullName.Replace('\\', '/');
                string destPath = Path.Combine(
                    stagingDir,
                    relativeName.Replace('/', Path.DirectorySeparatorChar));

                _fs.CreateDirectory(Path.GetDirectoryName(destPath)!);

                using (var src  = entry.Open())
                using (var dest = _fs.CreateFile(destPath))
                    src.CopyTo(dest);

                entries.Add(new ZipEntryInfo
                {
                    FullNameInZip    = relativeName,
                    StagedFilePath   = destPath,
                    UncompressedSize = entry.Length
                });

                // Parse modinfo.json if it lives at the zip root (no sub-directory).
                if (string.Equals(entry.Name, "modinfo.json", StringComparison.OrdinalIgnoreCase)
                    && !relativeName.TrimStart('/').Contains('/'))
                {
                    try
                    {
                        string json = File.ReadAllText(destPath);
                        modInfo = JsonSerializer.Deserialize<ModInfo>(json,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch { /* Malformed modinfo — ignore and fall back to heuristics. */ }
                }

                done++;
                progress?.Report((int)((double)done / total * 100));
            }
        }, ct).ConfigureAwait(false);

        // Classify each entry using FileClassifier.
        var installEntries    = new List<ZipEntryInfo>();
        var displayFiles      = new List<ZipEntryInfo>();
        var hookScriptEntries = new List<ZipEntryInfo>();
        var skippedFiles      = new List<SkippedFile>();

        foreach (var e in entries)
        {
            var category = FileClassifier.Classify(e, modInfo, out var reason);
            e.Category   = category;
            e.SkipReason = reason;

            switch (category)
            {
                case SkipCategory.Install:
                case SkipCategory.AmbiguousPending:
                    installEntries.Add(e);
                    break;
                case SkipCategory.DisplayOnly:
                    displayFiles.Add(e);
                    break;
                case SkipCategory.HookScript:
                    hookScriptEntries.Add(e);
                    break;
                default:
                    skippedFiles.Add(new SkippedFile(e.FullNameInZip, category, reason ?? string.Empty));
                    break;
            }
        }

        return new ZipStagingResult
        {
            StagingDirectory = stagingDir,
            Entries          = installEntries,
            DisplayFiles     = displayFiles,
            HookScripts      = hookScriptEntries,
            SkippedFiles     = skippedFiles,
            ModInfo          = modInfo,
            ZipHash          = zipHash
        };
    }

    public void CleanupStaging(string stagingDirectory)
    {
        if (_fs.DirectoryExists(stagingDirectory))
            _fs.DeleteDirectory(stagingDirectory, recursive: true);
    }
}

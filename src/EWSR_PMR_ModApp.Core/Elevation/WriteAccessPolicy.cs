using EWSR_PMR_ModApp.Core.Abstractions;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.SyncEngine.Mapping;

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>Directory creation permission does not prove permission to overwrite existing game files.</summary>
public static class WriteAccessPolicy
{
    public static bool RequiresElevation(WritePlanRequest request)
    {
        string gameRoot = string.IsNullOrWhiteSpace(request.GameRoot)
            ? Directory.GetParent(request.DataRoot)?.FullName ?? request.DataRoot : request.GameRoot;
        if (IsProtectedRoot(request.DataRoot) || IsProtectedRoot(gameRoot)) return true;

        var targets = (request.FilesToCopy ?? []).Select(f => new FileTargetSpec
        {
            RelativeTargetPath = f.RelativeTargetPath, TargetRoot = f.TargetRoot
        }).Concat(request.FilesToDelete ?? []).Concat(request.FilesToBackup ?? []).ToList();
        if (request.Operation == WritePlanOperation.Uninstall)
        {
            string backup = AppPaths.BackupDirForMod(request.ModId);
            if (Directory.Exists(backup))
                foreach (string file in Directory.EnumerateFiles(backup, "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(backup, file).Replace('\\', '/');
                    var root = TargetRoot.Data;
                    if (relative.StartsWith("__game__/", StringComparison.OrdinalIgnoreCase))
                    {
                        root = TargetRoot.Game;
                        relative = relative["__game__/".Length..];
                    }
                    else if (relative.StartsWith("__data__/", StringComparison.OrdinalIgnoreCase))
                        relative = relative["__data__/".Length..];
                    targets.Add(new FileTargetSpec { RelativeTargetPath = relative, TargetRoot = root });
                }
        }

        foreach (var target in targets)
        {
            string root = target.TargetRoot == TargetRoot.Game ? gameRoot : request.DataRoot;
            bool safe = target.TargetRoot == TargetRoot.Game
                ? PathValidator.IsAllowedGameRootTarget(gameRoot, request.DataRoot, target.RelativeTargetPath)
                : PathValidator.IsUnderDataRoot(root, target.RelativeTargetPath);
            if (!safe) throw new InvalidDataException($"Unsafe target rejected: {target.RelativeTargetPath}");
            string path = Path.Combine(root, target.RelativeTargetPath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(path))
            {
                try
                {
                    // Open, never truncate or write: checks the actual target ACL rather than its parent.
                    using var handle = File.Open(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                }
                catch (UnauthorizedAccessException) { return true; }
                // Sharing violations are not fixed by elevation; the writer reports/rolls them back.
                catch (IOException) { }
            }
            string? parent = Path.GetDirectoryName(path);
            while (parent is not null && !Directory.Exists(parent)) parent = Path.GetDirectoryName(parent);
            if (parent is null || !new RealFileSystem().CanWriteDirectory(parent)) return true;
        }
        return false;
    }

    public static bool IsProtectedRoot(string path) =>
        new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Any(root => string.Equals(Path.GetFullPath(path).TrimEnd('\\'), Path.GetFullPath(root).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                || Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
}

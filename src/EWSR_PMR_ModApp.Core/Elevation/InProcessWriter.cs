namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// <see cref="IElevatedWriter"/> implementation that executes file operations directly in the
/// calling process.  Used when <c>IGameLocator.CanWriteDataRoot</c> returns <c>true</c>
/// (game directory is writable without elevation).
/// </summary>
/// <remarks>
/// Validates every path via <see cref="PathValidator"/> before forwarding to
/// <see cref="WritePlanExecutor"/> — the same validation that runs in the Helper process.
/// </remarks>
public sealed class InProcessWriter : IElevatedWriter
{
    public Task<WriteResult> ExecuteAsync(WritePlanRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var validation = ValidateRequest(request);
        if (validation is not null)
            return Task.FromResult(new WriteResult { Success = false, ErrorMessage = validation });

        var result = WritePlanExecutor.Execute(request);
        return Task.FromResult(result);
    }

    private static string? ValidateRequest(WritePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DataRoot))
            return "DataRoot is required.";
        if (string.IsNullOrWhiteSpace(request.ModId))
            return "ModId is required.";

        string appDataRoot = Common.AppPaths.AppDataRoot;
        string gameRoot = ResolveGameRoot(request);

        if (request.FilesToCopy is not null)
        {
            foreach (var spec in request.FilesToCopy)
            {
                if (!IsSafeTarget(request.DataRoot, gameRoot, spec.TargetRoot, spec.RelativeTargetPath))
                    return $"Unsafe target path rejected: '{spec.RelativeTargetPath}'";
                if (!PathValidator.IsAllowedSource(spec.SourcePath, appDataRoot))
                    return $"Source path not in app data: '{spec.SourcePath}'";
            }
        }

        if (request.FilesToBackup is not null)
        {
            foreach (var target in request.FilesToBackup)
                if (!IsSafeTarget(request.DataRoot, gameRoot, target.TargetRoot, target.RelativeTargetPath))
                    return $"Unsafe backup path rejected: '{target.RelativeTargetPath}'";
        }

        if (request.FilesToDelete is not null)
        {
            foreach (var target in request.FilesToDelete)
                if (!IsSafeTarget(request.DataRoot, gameRoot, target.TargetRoot, target.RelativeTargetPath))
                    return $"Unsafe delete path rejected: '{target.RelativeTargetPath}'";
        }

        return null;
    }

    private static string ResolveGameRoot(WritePlanRequest request) =>
        !string.IsNullOrWhiteSpace(request.GameRoot)
            ? request.GameRoot
            : Directory.GetParent(request.DataRoot)?.FullName ?? string.Empty;

    private static bool IsSafeTarget(
        string dataRoot,
        string gameRoot,
        SyncEngine.Mapping.TargetRoot targetRoot,
        string relativePath) =>
        targetRoot == SyncEngine.Mapping.TargetRoot.Game
            ? PathValidator.IsAllowedGameRootTarget(gameRoot, dataRoot, relativePath)
            : PathValidator.IsUnderDataRoot(dataRoot, relativePath);
}

using System.Text.Json;
using EWSR_PMR_ModApp.Core.Common;
using EWSR_PMR_ModApp.Core.Elevation;

namespace EWSR_PMR_ModApp.Helper;

/// <summary>
/// Elevated helper process entry point.
/// Usage: EWSR_PMR_ModApp.Helper.exe "&lt;requestFilePath&gt;"
///
/// The helper:
///   1. Validates the request file is in an allowed location (%TEMP% or AppData).
///   2. Deserialises the <see cref="WritePlanRequest"/>.
///   3. Validates every path via <see cref="PathValidator"/>.
///   4. Executes the write plan via <see cref="WritePlanExecutor"/>.
///   5. Writes the <see cref="WriteResult"/> to {requestPath}.result.json.
///   6. Appends an audit log entry to %APPDATA%\EWSR_PMR_ModApp\helper.log.
///   7. Exits 0 on success, 1 on any failure.
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        if (args.Length < 1 || string.IsNullOrWhiteSpace(args[0]))
        {
            Console.Error.WriteLine("Usage: EWSR_PMR_ModApp.Helper.exe <requestFilePath>");
            return 1;
        }

        string requestPath = args[0].Trim('"');
        string resultPath  = requestPath + ".result.json";

        // ── 1. Validate request file location ─────────────────────────────────
        if (!IsAllowedRequestLocation(requestPath))
        {
            WriteResult(resultPath, new WriteResult
            {
                Success      = false,
                ErrorMessage = $"Request file location rejected for security: '{requestPath}'. Must be under %TEMP% or AppData."
            });
            AppendAuditLog("REJECTED", "unknown", $"Request file location rejected: {requestPath}");
            return 1;
        }

        if (!File.Exists(requestPath))
        {
            WriteResult(resultPath, new WriteResult
            {
                Success      = false,
                ErrorMessage = $"Request file not found: '{requestPath}'"
            });
            return 1;
        }

        // ── 2. Deserialise ────────────────────────────────────────────────────
        WritePlanRequest? request;
        try
        {
            string json = File.ReadAllText(requestPath);
            request = JsonSerializer.Deserialize<WritePlanRequest>(json, s_jsonOptions);
        }
        catch (Exception ex)
        {
            WriteResult(resultPath, new WriteResult
            {
                Success      = false,
                ErrorMessage = $"Failed to deserialise request: {ex.Message}"
            });
            AppendAuditLog("ERROR", "unknown", $"Deserialisation failed: {ex.Message}");
            return 1;
        }

        if (request is null)
        {
            WriteResult(resultPath, new WriteResult { Success = false, ErrorMessage = "Request was null after deserialisation." });
            return 1;
        }

        // ── 3. Validate every path ────────────────────────────────────────────
        string? validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            WriteResult(resultPath, new WriteResult { Success = false, ErrorMessage = validationError });
            AppendAuditLog("REJECTED", request.Operation.ToString(), validationError);
            return 1;
        }

        // ── 4. Execute ────────────────────────────────────────────────────────
        var logLines = new List<string>();
        WriteResult result = WritePlanExecutor.Execute(request, line => logLines.Add(line));

        // ── 5. Write result ───────────────────────────────────────────────────
        WriteResult(resultPath, result);

        // ── 6. Audit log ──────────────────────────────────────────────────────
        string outcome = result.Success ? "SUCCESS" : "FAILURE";
        string detail  = result.Success
            ? $"copied={result.FilesCopied} deleted={result.FilesDeleted} backedUp={result.FilesBackedUp}"
            : result.ErrorMessage ?? result.Errors.FirstOrDefault()?.Message ?? "unknown error";

        AppendAuditLog(outcome, request.Operation.ToString(), detail);
        foreach (var line in logLines)
            AppendAuditLog("  FILE", request.Operation.ToString(), line.TrimStart());

        // ── 7. Exit ───────────────────────────────────────────────────────────
        return result.Success ? 0 : 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsAllowedRequestLocation(string requestFilePath)
    {
        try
        {
            string normalized = Path.GetFullPath(requestFilePath);

            // Allow %TEMP%
            string temp           = Path.GetFullPath(Path.GetTempPath())
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            // Allow %APPDATA%\EWSR_PMR_ModApp\
            string appData        = Path.GetFullPath(AppPaths.AppDataRoot)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            return normalized.StartsWith(temp,    StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(appData, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? ValidateRequest(WritePlanRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.DataRoot))
            return "DataRoot is missing or empty.";
        if (string.IsNullOrWhiteSpace(request.ModId))
            return "ModId is missing or empty.";

        string appDataRoot = AppPaths.AppDataRoot;

        // Validate source paths for copy operations.
        if (request.FilesToCopy is not null)
        {
            foreach (var spec in request.FilesToCopy)
            {
                if (!PathValidator.IsUnderDataRoot(request.DataRoot, spec.RelativeTargetPath))
                    return $"Unsafe target path rejected: '{spec.RelativeTargetPath}'";

                if (!PathValidator.IsAllowedSource(spec.SourcePath, appDataRoot))
                    return $"Source path not under AppData: '{spec.SourcePath}'";
            }
        }

        // Validate relative paths for backup.
        if (request.FilesToBackup is not null)
        {
            foreach (var rel in request.FilesToBackup)
                if (!PathValidator.IsUnderDataRoot(request.DataRoot, rel))
                    return $"Unsafe backup path rejected: '{rel}'";
        }

        // Validate relative paths for delete.
        if (request.FilesToDelete is not null)
        {
            foreach (var rel in request.FilesToDelete)
                if (!PathValidator.IsUnderDataRoot(request.DataRoot, rel))
                    return $"Unsafe delete path rejected: '{rel}'";
        }

        return null;
    }

    private static void WriteResult(string resultPath, WriteResult result)
    {
        try
        {
            File.WriteAllText(resultPath, JsonSerializer.Serialize(result, s_jsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to write result file: {ex.Message}");
        }
    }

    private static void AppendAuditLog(string level, string operation, string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.AppDataRoot);
            string logPath = Path.Combine(AppPaths.AppDataRoot, "helper.log");
            string line    = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z [{level}] {operation}: {message}";
            File.AppendAllText(logPath, line + Environment.NewLine);
        }
        catch
        {
            // Audit log failure must never crash the helper.
        }
    }
}

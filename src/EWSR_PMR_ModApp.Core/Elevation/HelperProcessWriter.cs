using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>
/// <see cref="IElevatedWriter"/> implementation that delegates file writes to the elevated
/// <c>EWSR_PMR_ModApp.Helper.exe</c> process via a temp-file JSON request/response pair.
/// </summary>
/// <remarks>
/// Flow:
/// <list type="number">
///   <item>Serialise <see cref="WritePlanRequest"/> to a temp JSON file.</item>
///   <item>Spawn Helper.exe via <c>ProcessStartInfo { Verb = "runas" }</c> — triggers UAC prompt.</item>
///   <item>Await helper exit.</item>
///   <item>Deserialise <c>{requestPath}.result.json</c>.</item>
///   <item>Delete both temp files.</item>
/// </list>
/// UAC cancel (native error 1223) is caught and surfaced as a non-throwing <see cref="WriteResult"/>.
/// </remarks>
public sealed class HelperProcessWriter : IElevatedWriter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = false
    };

    public async Task<WriteResult> ExecuteAsync(WritePlanRequest request, CancellationToken ct = default)
    {
        string requestPath = Path.Combine(
            Path.GetTempPath(),
            $"ewsr_helper_{Guid.NewGuid():N}.json");
        string resultPath = requestPath + ".result.json";

        try
        {
            // Write the request to a temp file.
            await File.WriteAllTextAsync(
                requestPath,
                JsonSerializer.Serialize(request, s_jsonOptions),
                ct).ConfigureAwait(false);

            var psi = new ProcessStartInfo
            {
                FileName        = Path.Combine(AppContext.BaseDirectory, "PMR CM.Helper.exe"),
                Arguments       = $"\"{requestPath}\"",
                Verb            = "runas",
                UseShellExecute = true,
                CreateNoWindow  = true
            };

            try
            {
                using var proc = Process.Start(psi)
                    ?? throw new InvalidOperationException("Failed to start helper process.");

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // UAC cancelled by user.
                return new WriteResult { Success = false, ErrorMessage = "Elevation cancelled by user." };
            }

            // Read the result written by the helper.
            if (!File.Exists(resultPath))
            {
                return new WriteResult
                {
                    Success      = false,
                    ErrorMessage = "Helper process did not produce a result file. It may have crashed."
                };
            }

            string json = await File.ReadAllTextAsync(resultPath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<WriteResult>(json, s_jsonOptions)
                ?? new WriteResult { Success = false, ErrorMessage = "Helper result file was empty or invalid." };
        }
        finally
        {
            TryDelete(requestPath);
            TryDelete(resultPath);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* Best-effort cleanup — do not throw. */ }
    }
}

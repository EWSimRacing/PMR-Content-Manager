using System.ComponentModel;
using System.Diagnostics;

namespace EWSR_PMR_ModApp.Core.SyncEngine;

/// <summary>
/// Runs a PowerShell mod hook script, optionally elevated via UAC.
/// <list type="bullet">
///   <item>Elevated hooks: spawned via <c>runas</c> with a visible console so the user can
///         see progress; only the exit code is checked.</item>
///   <item>Non-elevated hooks: stdout/stderr are captured and returned in
///         <see cref="HookResult.Output"/>.</item>
/// </list>
/// Both paths pass <c>-DataRoot</c> and <c>-ModId</c> as named parameters to the script.
/// </summary>
public sealed class PowerShellHookRunner : IHookRunner
{
    public async Task<HookResult> RunAsync(
        string scriptPath,
        string dataRoot,
        string modId,
        bool   requiresElevation,
        CancellationToken ct = default)
    {
        string psArgs = $"-ExecutionPolicy Bypass -NonInteractive -NoProfile" +
                        $" -File \"{scriptPath}\"" +
                        $" -DataRoot \"{dataRoot}\"" +
                        $" -ModId \"{modId}\"";

        ProcessStartInfo psi;

        if (requiresElevation)
        {
            // Elevated path: show a console so the user can see script progress.
            // We cannot redirect stdout/stderr from an elevated child process, so we
            // check exit code only.
            psi = new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = psArgs,
                Verb            = "runas",
                UseShellExecute = true,
                CreateNoWindow  = false
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName               = "powershell.exe",
                Arguments              = psArgs,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };
        }

        try
        {
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start hook process.");

            string? output = null;

            if (!requiresElevation)
            {
                // Read both streams concurrently to prevent buffer-deadlock.
                var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
                var stderrTask = proc.StandardError.ReadToEndAsync(ct);

                await proc.WaitForExitAsync(ct).ConfigureAwait(false);

                var stdout = (await stdoutTask.ConfigureAwait(false)).Trim();
                var stderr = (await stderrTask.ConfigureAwait(false)).Trim();

                output = string.IsNullOrEmpty(stderr)
                    ? stdout
                    : string.IsNullOrEmpty(stdout) ? stderr : $"{stdout}\n{stderr}";
            }
            else
            {
                await proc.WaitForExitAsync(ct).ConfigureAwait(false);
            }

            return proc.ExitCode == 0
                ? HookResult.Ok(proc.ExitCode, output)
                : HookResult.Fail(
                    $"Script exited with code {proc.ExitCode}." +
                    (output is not null ? $"\n{output}" : string.Empty),
                    proc.ExitCode);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return HookResult.Fail("Elevation was cancelled by the user.");
        }
        catch (Exception ex)
        {
            return HookResult.Fail($"Failed to launch hook process: {ex.Message}");
        }
    }
}

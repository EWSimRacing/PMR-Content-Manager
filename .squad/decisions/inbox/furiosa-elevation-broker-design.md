# Decision: Elevation Broker Architecture

**Date:** 2026-05-31T20:21:24-04:00  
**Author:** Furiosa (Lead)  
**Status:** Approved by Elliott — ready for implementation

---

## Problem

WPF's OLE-based drag-drop cannot receive files from non-elevated Explorer when the app runs elevated (UIPI blocks the COM channel — this is unfixable via `ChangeWindowMessageFilterEx`). Elliott verified: drag-drop works non-admin, fails as admin. The current "Restart as Administrator" flow therefore breaks the primary UX.

## Decision

**Non-elevated UI + elevated helper process.**

The UI process (`EWSR_PMR_ModApp.UI.exe`) remains `asInvoker` and **never** runs elevated. All interactive work (drag-drop, dialogs, ambiguous-mapping resolution) stays in the UI. Privileged file writes to `C:\Program Files\Project Motor Racing\data` are delegated to a short-lived **helper process** (`EWSR_PMR_ModApp.Helper.exe`) that has a `requireAdministrator` manifest and runs only the write operations.

---

## 1. Helper Process Shape

### New project: `EWSR_PMR_ModApp.Helper`

- Target: `net10.0-windows`, console application (no window).
- Manifest: `requireAdministrator` execution level.
- References: `EWSR_PMR_ModApp.Core` only.
- Output: placed alongside `EWSR_PMR_ModApp.UI.exe` in the publish output.

### Responsibility Boundary

The helper is **write-only and non-interactive**:

| Responsibility | Owner |
|----------------|-------|
| Validate zip integrity | UI |
| Stage (extract) zip | UI |
| Index game files | UI |
| Resolve mapping plan | UI |
| Prompt user for ambiguous mappings | UI |
| Build resolved write-plan | UI |
| **Backup originals** | **Helper** |
| **Copy files to data root** | **Helper** |
| **Delete files (uninstall new-files)** | **Helper** |
| **Update manifest.json** | UI (AppData, non-elevated) |
| Cache mod payload (AppData) | UI |

The UI sends the helper a fully-resolved, non-interactive **WritePlan**. The helper executes it and returns a **WriteResult**. No callbacks, no prompts — pure file I/O.

---

## 2. IPC Mechanism

### Choice: Temp JSON request file + stdout JSON response

**Why:**
- **Simple:** UI writes a temp JSON file, passes its path as a command-line arg, helper writes JSON result to stdout.
- **Secure:** Request file is in the user's temp directory (ACL protected per-user); helper validates every path before acting.
- **Testable:** Helper can be invoked from tests with a JSON file; no sockets/pipes to mock.
- **Progress:** Limited to coarse updates (the helper is short-lived — typically < 5s). Detailed progress stays in the UI during staging/mapping. Helper can emit single-line JSON progress events to stdout before the final result if needed (one JSON object per line, UI reads line-by-line).

**Alternatives considered:**
- Named pipe: more complex, adds async pipe-handling code, overkill for a short-lived process.
- stdin/stdout streaming: viable, but passing a file path is simpler for debugging/logging.

### Request DTO: `WritePlanRequest`

```csharp
namespace EWSR_PMR_ModApp.Core.Elevation;

/// <summary>Request payload sent from UI to the elevated Helper.</summary>
public sealed class WritePlanRequest
{
    /// <summary>Discriminator for operation type.</summary>
    public required WritePlanOperation Operation { get; init; }

    /// <summary>Absolute path to the game data root (e.g., C:\Program Files\...\data).</summary>
    public required string DataRoot { get; init; }

    /// <summary>Mod identifier (used for backup directory naming).</summary>
    public required string ModId { get; init; }

    /// <summary>Files to copy (Install/Reapply). Each entry is source→target.</summary>
    public IReadOnlyList<FileCopySpec>? FilesToCopy { get; init; }

    /// <summary>Relative paths under DataRoot to delete (Uninstall new-files only).</summary>
    public IReadOnlyList<string>? FilesToDelete { get; init; }

    /// <summary>Relative paths under DataRoot to back up before overwriting.</summary>
    public IReadOnlyList<string>? FilesToBackup { get; init; }
}

public enum WritePlanOperation { Install, Uninstall, Reapply }

public sealed class FileCopySpec
{
    /// <summary>Absolute source path (staged file or cached payload file).</summary>
    public required string SourcePath { get; init; }

    /// <summary>Relative target path under DataRoot.</summary>
    public required string RelativeTargetPath { get; init; }
}
```

### Response DTO: `WriteResult`

```csharp
namespace EWSR_PMR_ModApp.Core.Elevation;

public sealed class WriteResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int FilesCopied { get; init; }
    public int FilesDeleted { get; init; }
    public int FilesBackedUp { get; init; }
    public IReadOnlyList<FileOperationError> Errors { get; init; } = [];
}

public sealed class FileOperationError
{
    public required string RelativePath { get; init; }
    public required string Message { get; init; }
}
```

### Progress (optional, low-priority)

If progress is desired, the helper can emit newline-delimited JSON to stdout:
```json
{"type":"progress","phase":"Backing up","percent":10}
{"type":"progress","phase":"Copying files","percent":55}
{"type":"result","success":true,"filesCopied":12,...}
```
The UI reads lines, parses, and updates the progress bar. If this adds complexity, skip it — helper operations are fast.

---

## 3. When to Elevate

```
UI startup → IGameLocator.CanWriteDataRoot(dataRoot)
  ├─ true  → run SyncEngine in-process (no helper, no UAC)
  └─ false → on write operations, spawn Helper with runas
```

### Spawn Logic (in UI)

```csharp
var psi = new ProcessStartInfo
{
    FileName        = Path.Combine(AppContext.BaseDirectory, "EWSR_PMR_ModApp.Helper.exe"),
    Arguments       = $"\"{requestFilePath}\"",
    Verb            = "runas",
    UseShellExecute = true,
    CreateNoWindow  = true,
    RedirectStandardOutput = false // UseShellExecute precludes this
};

try
{
    using var proc = Process.Start(psi);
    await proc!.WaitForExitAsync();
    // Read result from a temp response file (helper writes it before exiting)
}
catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
{
    // UAC cancelled — surface message to user, abort operation
}
```

**Note:** `UseShellExecute = true` is required for `Verb = "runas"`, but prevents stdout redirection. Two options:
1. Helper writes result to a temp response file (path passed as second arg or derived from request path).
2. Use named pipe (adds complexity).

**Decision:** Use temp response file. Helper writes `{requestPath}.result.json` before exiting.

### Batching

One UAC prompt per user-initiated operation (one Install/Uninstall/Reapply click). If the user drops multiple zips, the UI can batch them into a single write-plan with multiple mods — one UAC prompt total.

---

## 4. Operations Routed Through Helper

| Operation | Privileged Steps |
|-----------|------------------|
| **Install** | Backup originals → Copy mod files |
| **Uninstall** | Restore backups → Delete new-files |
| **Reapply** | Copy cached payload files (no backup needed — originals already backed up) |

All three use the same `WritePlanRequest` with different `Operation` values. The helper interprets the operation and executes the appropriate file ops.

---

## 5. Security Requirements

The helper runs as **Administrator** and writes files based on a request from a medium-integrity process. Mitigations:

### 5.1 Path Validation (CRITICAL)

Before any file operation, the helper **MUST** validate:

```csharp
static bool IsUnderDataRoot(string dataRoot, string relativePath)
{
    // Reject absolute paths, .., and paths that escape
    if (Path.IsPathRooted(relativePath)) return false;
    string combined = Path.GetFullPath(Path.Combine(dataRoot, relativePath));
    string normalizedRoot = Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar) 
                            + Path.DirectorySeparatorChar;
    return combined.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
}
```

- Reject any `RelativeTargetPath` that is rooted or contains `..` traversal.
- Reject any `SourcePath` that is not under `%APPDATA%\EWSR_PMR_ModApp\` (staging or payload cache).
- If validation fails → abort with error, no files written.

### 5.2 Request File Location

- Request file **MUST** be in `%TEMP%` or `%APPDATA%\EWSR_PMR_ModApp\` (user-protected directories).
- Helper rejects requests from other locations.

### 5.3 DataRoot Whitelist (defense-in-depth)

- The helper can optionally require `DataRoot` to match a known pattern (e.g., contains `Project Motor Racing\data`). Prevents malicious request from writing to arbitrary locations.

### 5.4 Audit Logging

- Helper logs all operations to `%APPDATA%\EWSR_PMR_ModApp\helper.log` with timestamps. Aids debugging and provides an audit trail.

---

## 6. Core Refactoring Required

### 6.1 Split SyncEngine into Plan + Execute

Current `InstallAsync` does everything. Refactor into:

```csharp
// Step 1: Pure, non-elevated — returns a plan
public Task<InstallPlan> PrepareInstallAsync(
    string zipPath,
    string dataRoot,
    string modName,
    Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
    IProgress<SyncProgress>? progress = null,
    CancellationToken ct = default);

// Step 2: Elevated — executes file writes (called in-process or via helper)
public Task<InstallResult> ExecuteInstallAsync(
    InstallPlan plan,
    IProgress<SyncProgress>? progress = null,
    CancellationToken ct = default);
```

**`InstallPlan`** contains:
- `ModId`, `ModName`, `DataRoot`
- `FilesToBackup` (relative paths)
- `FilesToCopy` (staged source → relative target)
- `Warnings` (accumulated during prepare)
- `StagedModInfo` (for manifest creation post-install)

### 6.2 IElevatedWriter Abstraction

```csharp
public interface IElevatedWriter
{
    Task<WriteResult> ExecuteAsync(WritePlanRequest request, CancellationToken ct);
}

// In-process implementation (when CanWriteDataRoot is true)
public class InProcessWriter : IElevatedWriter { ... }

// Out-of-process implementation (spawns Helper)
public class HelperProcessWriter : IElevatedWriter { ... }
```

The UI injects `IElevatedWriter` into `SyncEngine` (or a new `InstallOrchestrator`). Based on `CanWriteDataRoot`, the DI container provides the appropriate implementation.

### 6.3 Manifest Update Stays in UI

Manifest is in `%APPDATA%` — no elevation needed. After helper returns success, UI calls `_manifestStore.AddOrUpdateModAsync(...)`.

---

## 7. Task Decomposition

### Phase 1: Core + Helper (Nux)

| Task ID | Description | Depends On |
|---------|-------------|------------|
| **N1** | Create DTOs: `WritePlanRequest`, `WriteResult`, `FileCopySpec`, `FileOperationError`, `WritePlanOperation` in `Core/Elevation/` | — |
| **N2** | Add security validation helpers: `PathValidator.IsUnderDataRoot()`, `PathValidator.IsAllowedSource()` in `Core/Elevation/` | — |
| **N3** | Create `EWSR_PMR_ModApp.Helper` project with `requireAdministrator` manifest, wire into solution | N1 |
| **N4** | Implement helper `Program.Main`: parse args, deserialize request, validate paths, execute file ops, write response JSON | N1, N2, N3 |
| **N5** | Refactor `SyncEngine.InstallAsync` → `PrepareInstallAsync` + `ExecuteInstallAsync` | — |
| **N6** | Define `IElevatedWriter` interface + `InProcessWriter` implementation | N1 |
| **N7** | Implement `HelperProcessWriter` (spawns Helper.exe, handles UAC cancel) | N1, N3, N6 |
| **N8** | Apply same prepare/execute split to `UninstallAsync` and `ReapplyRevertedModsAsync` | N5 |

### Phase 2: UI Wiring (Slit)

| Task ID | Description | Depends On |
|---------|-------------|------------|
| **S1** | Remove `NeedsElevation` property, `RestartElevatedCommand`, and admin banner from `MainViewModel` / `MainWindow.xaml` | — |
| **S2** | Update DI registration: inject `IElevatedWriter` (choose impl based on `CanWriteDataRoot` at startup) | N6, N7 |
| **S3** | Update `InstallZipsAsync` to call `PrepareInstallAsync` then `IElevatedWriter.ExecuteAsync` | N5, N7 |
| **S4** | Handle UAC-cancel gracefully (show "Operation cancelled" message, don't crash) | S3 |
| **S5** | Wire Uninstall and Reapply through the same broker pattern | N8, S3 |
| **S6** | (Optional) Implement progress streaming from helper stdout | S3 |

### Phase 3: Tests (Wez)

| Task ID | Description | Depends On |
|---------|-------------|------------|
| **W1** | Unit tests for `PathValidator` (traversal attacks, edge cases) | N2 |
| **W2** | Unit tests for `WritePlanRequest` / `WriteResult` serialization round-trip | N1 |
| **W3** | Integration test: Helper.exe with a mock request file (use a temp data root, no real elevation needed) | N4 |
| **W4** | Update existing SyncEngine tests to use `PrepareInstallAsync` + `InProcessWriter` | N5, N6 |

### Ordering Summary

```
N1, N2 (parallel) → N3 → N4 (helper exe complete)
                 ↘ N5 → N6 → N7 (broker complete)
                      ↘ N8

S1 (can start immediately)
S2, S3, S4, S5 depend on Nux completing N5–N7

W1, W2 can start after N1, N2
W3 after N4
W4 after N5, N6
```

---

## 8. Packaging Note

**Action item (Furiosa/Nux):** Ensure `EWSR_PMR_ModApp.Helper.exe` is output to the same directory as `EWSR_PMR_ModApp.UI.exe`.

- In `EWSR_PMR_ModApp.Helper.csproj`, set `<OutputPath>` to match UI's output, OR
- In the solution, add a post-build step or `<ProjectReference>` with `ReferenceOutputAssembly="false"` and `OutputItemType="Content"` to copy the helper.

The UI locates the helper via:
```csharp
Path.Combine(AppContext.BaseDirectory, "EWSR_PMR_ModApp.Helper.exe")
```

---

## Summary

- **UI stays non-elevated** — drag-drop works.
- **Helper.exe** is a minimal, elevated, non-interactive console app that executes pre-validated write plans.
- **IPC:** Temp JSON request file → helper exe (runas) → temp JSON response file.
- **Security:** All paths validated before any write; sources must be in AppData; targets must be under DataRoot.
- **Refactor:** SyncEngine splits into Prepare (pure) + Execute (writes); IElevatedWriter abstracts in-process vs. helper execution.
- **One UAC prompt** per user action (install/uninstall/reapply).

This design keeps the UX simple (drop a zip, get one UAC prompt if needed, done) while respecting UIPI and Windows security boundaries.

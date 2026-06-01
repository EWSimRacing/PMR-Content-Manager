# History: Nux — Core Dev

## Learnings

### 2026-05-31T20:25:00-04:00: Elevation Broker — Phase 1 (N1–N8)

**What was built:**

| Task | Files |
|------|-------|
| N1 DTOs | `Core/Elevation/WritePlanOperation.cs`, `FileCopySpec.cs`, `FileOperationError.cs`, `WritePlanRequest.cs`, `WriteResult.cs` |
| N2 Security | `Core/Elevation/PathValidator.cs` |
| N3 Helper project | `src/EWSR_PMR_ModApp.Helper/` — csproj, app.manifest, added to slnx |
| N4 Helper Program | `src/EWSR_PMR_ModApp.Helper/Program.cs` |
| N5 Install split | `Core/SyncEngine/InstallPlan.cs`; `SyncEngine.cs` + `ISyncEngine.cs` refactored |
| N6 IElevatedWriter | `Core/Elevation/IElevatedWriter.cs`, `WritePlanExecutor.cs`, `InProcessWriter.cs` |
| N7 HelperProcessWriter | `Core/Elevation/HelperProcessWriter.cs` |
| N8 Uninstall+Reapply split | `Core/SyncEngine/UninstallPlan.cs`, `ReapplyPlan.cs`; SyncEngine updated |

**DTO serialization approach:** All DTO properties use `init` accessors with default empty values (no `required` keyword) to ensure STJ can round-trip them without source-gen or special options. Callers populate all fields before serializing.

**Prepare/Execute split:**
- `InstallAsync`, `UninstallAsync`, `ReapplyRevertedModsAsync` remain as thin wrappers — existing callers/tests unchanged.
- Each operation has `PrepareXxxAsync` (pure, no disk writes) and `ExecuteXxxAsync` (writes).
- `InstallPlan` carries `StagingDirectory`; callers MUST call `CleanupInstallPlan(plan)` to delete staging. The thin wrapper does this automatically in a `finally`.
- If `PrepareInstallAsync` throws, it self-cleans the staging dir.
- `ISyncEngine` gains: `PrepareInstallAsync`, `ExecuteInstallAsync`, `CleanupInstallPlan`, `PrepareUninstallAsync`, `ExecuteUninstallAsync`, `PrepareReapplyAsync`, `ExecuteReapplyAsync`.

**Shared WritePlanExecutor (N6 key design):**
- `WritePlanExecutor` (static, in Core) is called by BOTH `InProcessWriter` AND `EWSR_PMR_ModApp.Helper/Program.cs`.
- Uses real `System.IO` (not `IFileSystem`) — both callers operate against real files.
- Backup format identical to `BackupService`: `AppPaths.BackupDirForMod(modId)/{relativeTargetPath}`.
- Accepts optional `Action<string> logLine` callback for per-file audit logging.

**IElevatedWriter — how the UI picks the implementation (Slit must know):**
```
IGameLocator.CanWriteDataRoot(dataRoot)
  ├─ true  → inject InProcessWriter  (no UAC, direct file ops)
  └─ false → inject HelperProcessWriter (writes request JSON → spawns Helper.exe runas → reads .result.json)
```
`IElevatedWriter` is NOT injected into `SyncEngine` (deviation from Furiosa suggestion — see decision doc). Slit injects it into the orchestration layer: call `PrepareInstallAsync` → build `WritePlanRequest` from `InstallPlan` → call `IElevatedWriter.ExecuteAsync` → then update manifest via `IManifestStore`.

**Exact ISyncEngine signatures (Slit/Wez must know):**
```csharp
// Install
Task<InstallPlan> PrepareInstallAsync(string zipPath, string dataRoot, string modName,
    Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>> confirmAmbiguous,
    IProgress<SyncProgress>? progress = null, CancellationToken ct = default);
Task<InstallResult> ExecuteInstallAsync(InstallPlan plan,
    IProgress<SyncProgress>? progress = null, CancellationToken ct = default);
void CleanupInstallPlan(InstallPlan plan);

// Uninstall
Task<UninstallPlan> PrepareUninstallAsync(string modId, string dataRoot, CancellationToken ct = default);
Task<UninstallResult> ExecuteUninstallAsync(UninstallPlan plan,
    IProgress<SyncProgress>? progress = null, CancellationToken ct = default);

// Reapply
Task<ReapplyPlan> PrepareReapplyAsync(string dataRoot, CancellationToken ct = default);
Task<ReapplyResult> ExecuteReapplyAsync(ReapplyPlan plan,
    IProgress<SyncProgress>? progress = null, CancellationToken ct = default);

// IElevatedWriter (in Core/Elevation/)
Task<WriteResult> ExecuteAsync(WritePlanRequest request, CancellationToken ct = default);
// Implementations: InProcessWriter, HelperProcessWriter
```

**Packaging approach chosen:** `ProjectReference` from the UI csproj to Helper with `ReferenceOutputAssembly="false"`, `OutputItemType="Content"`, `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`. Result: `EWSR_PMR_ModApp.Helper.exe` + `.dll` + `.runtimeconfig.json` + `.deps.json` all land in UI's bin output. Verified in `bin\Debug\net10.0-windows\`.

**Helper.exe location:** `Path.Combine(AppContext.BaseDirectory, "EWSR_PMR_ModApp.Helper.exe")` — used in `HelperProcessWriter`.

**UAC cancel:** `Win32Exception.NativeErrorCode == 1223` → `WriteResult { Success=false, ErrorMessage="Elevation cancelled by user." }` — no exception thrown, Slit shows error message.

**Audit log:** `%APPDATA%\EWSR_PMR_ModApp\helper.log` — timestamp, operation, per-file outcome. Never throws.

**Build/test status:** `dotnet build` → 0 errors, 0 warnings. `dotnet test` → 112 passed, 0 failed, 0 skipped.

**What Wez needs to add (Phase 3):**
- `PathValidator` unit tests (traversal attacks, edge cases)
- `WritePlanRequest`/`WriteResult` serialization round-trip tests
- Integration test: `WritePlanExecutor.Execute` with a mock data dir (no elevation)
- Update `InstallWarningsTests` etc. to use `PrepareInstallAsync` + `InProcessWriter` directly

**What Slit needs for Phase 2:**
- Remove `NeedsElevation`/`RestartElevatedCommand`/admin banner from UI
- DI registration: check `CanWriteDataRoot` at startup, register `InProcessWriter` or `HelperProcessWriter`
- Wire `PrepareInstallAsync` → build `WritePlanRequest` → `IElevatedWriter.ExecuteAsync` → manifest update
- Call `CleanupInstallPlan(plan)` in a `finally` after execute
- Handle `WriteResult.Success == false` gracefully (show `ErrorMessage` to user)



**Bug:** `FileClassifier.Classify()` checked `modInfo.SkipFiles` and `modInfo.DisplayFiles` but not `modInfo.Files`. Files with unrecognized extensions that were explicitly mapped in `modinfo.Files` were returning `NoPathMatch` instead of `Install`.

**Fix:** Added check 5b immediately after the MetaFile check and before the extension policy. If `modInfo?.Files.ContainsKey(zipPath)` is true → return `SkipCategory.Install` with `reason = null`.

**Ordering principle:** `modinfo.json` explicit mappings must win over all extension-based policy. The position (post-MetaFile, pre-extension checks) ensures this while still letting `SkipFiles`/unsafe/artifact guards run first.

**Test:** Removed `[Fact(Skip = "...")]` from `ModinfoFiles_ExplicitEntry_IsInstall_EvenIfExtensionWouldBeNoPathMatch` — it now passes as a normal `[Fact]`.

**Test count:** 112 passed, 0 failed, 0 skipped.

### 2026-05-31: ZipHandling Skip Logic Implemented

**What was built:**
- `SkipCategory.cs` — 9-value enum classifying every zip entry disposition
- `SkippedFile.cs` — record(PathInZip, Category, Reason) for reporting
- `InstallResult.cs` — record(Success, Installed, Skipped, Warnings); used by SyncEngine
- `FileClassifier.cs` — static classifier; `Classify(entry, modInfo, out reason)` applies 9-step ordered policy
- `ZipEntryInfo.cs` — extended with `Category` and `SkipReason` mutable properties
- `ModInfo.cs` — expanded to full spec: SchemaVersion, Description, Website, MinGameVersion, Tags, DisplayFiles (with `DisplayFileInfo` nested class), SkipFiles
- `ZipStagingResult.cs` — extended with `DisplayFiles` and `SkippedFiles` buckets
- `ZipService.StageAsync` — now classifies all entries post-extraction; splits into `Entries` (Install+AmbiguousPending), `DisplayFiles`, `SkippedFiles`

**Key implementation notes:**
- Classification order: modinfo.SkipFiles → modinfo.DisplayFiles override → UnsafeFile → PackagingArtifact → MetaFile → DisplayOnly docs → Image (conditional) → GameData (conditional) → NoPathMatch
- `IsInsideDataPath` uses `data/` prefix (case-insensitive); textures inside `data/` are Install even if images
- Docs (.md/.txt/.pdf) inside `data/` get NoPathMatch — unusual placement, not a valid game file
- Glob matching is pure Regex, no NuGet packages: `*` → `[^/]*`, `**` → `.*`, `?` → `.`; patterns without `/` matched against filename only
- `modinfo.json` DisplayFiles keys are checked by exact zip path match before extension classifier runs
- All 60 existing tests remain green after the change

**Build:** `dotnet build src\EWSR_PMR_ModApp.Core\...csproj` → 0 errors, 0 warnings
**Tests:** 60 passed, 0 failed, 0 skipped

### 2026-05-31T19:52:00-04:00: Team Integration
- **Furiosa's design work merged:** Zip Skip Policy decision document captured taxonomy, extension policy, modinfo.json v1 spec, UI surfacing strategy.
- **Session status:** ✓ Policy design + implementation complete; all 60 tests passing.
- **Decision archived:** Both decisions merged into `.squad/decisions.md` (Furiosa + Nux).
- **Next owner:** Slit (UI updates to show skip breakdown in WarningsDialog and mod detail file list).

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- My domain: file sync engine, zip extraction/validation, game path detection, update-revert recovery, backups.
- User: Elliott Williams.

### 2026-05-31: Warning-format — name every skipped/colliding file

**Decision:** `SyncEngine.InstallAsync` now emits one warning row per unmatched file and one warning row per collision (both with zip-relative paths), instead of a single aggregated count.

**Unmatched format:**
```
Skipped (no match in data): {entry.FullNameInZip}
```

**Collision format:**
```
Path collision: '{relativeTargetPath}' — {N} source(s): {comma-list of ZipEntry.FullNameInZip} — none installed.
```

**Key files touched:**
- `src/EWSR_PMR_ModApp.Core/SyncEngine/SyncEngine.cs` — warning construction (~lines 99–109)
- `tests/EWSR_PMR_ModApp.Core.Tests/SyncEngine/InstallWarningsTests.cs` — 4 new tests verifying the format
- `tests/EWSR_PMR_ModApp.Core.Tests/TestDoubles/StubZipService.cs` — new test double for install-path tests

**MappingResolver strategy note for tests:**
Path-overlay triggers when zip top-level dirs match data subdirectories (via real `Directory.Exists` or data-file index prefix check). To reliably exercise `Unmatched` and `Collisions` buckets in tests, use flat zip entries (no `/` in `FullNameInZip`) — `topLevelDirs.Count == 0` forces filename-index unconditionally. For collision tests, use distinct unknown prefixes (`skin_a/`, `skin_b/`) that are absent from both disk and the data index.


### 2026-05-31: Stack Decision & Handoff (via Furiosa)
- **Stack:** C# / .NET 10 + WPF. Solution: `EWSR_PMR_ModApp.slnx` at repo root.
- **Your module:** `src/EWSR_PMR_ModApp.Core/` (net10.0-windows, no UI deps).
- **Next:** Implement GameDetection and Manifest first (alphabetically and dependency-wise), then ZipHandling, SyncEngine, Backup.
- **See:** `docs/ARCHITECTURE.md` for full module spec and risk mitigations.
- **Build:** `dotnet build` from repo root; run with `dotnet run --project src/EWSR_PMR_ModApp.UI`.

### 2026-05-31: Core Foundation Implemented

**Interfaces defined (all in `src/EWSR_PMR_ModApp.Core/`):**

| Interface | Namespace | Responsibility |
|---|---|---|
| `IFileSystem` | `Core.Abstractions` | Abstracts all disk I/O; enables unit testing without touching disk. Production impl: `RealFileSystem`. |
| `IGameLocator` | `Core.GameDetection` | Resolves the PMR `data` root (user config → default path → Steam VDF/registry). Returns `GameLocatorResult`. |
| `IManifestStore` | `Core.Manifest` | Load/save JSON manifest; add/remove mod entries; conflict detection; file-ownership query. |
| `IZipService` | `Core.ZipHandling` | Validate zip integrity (CRC), stage to app-data, enumerate entries, parse optional modinfo.json. |
| `IMappingResolver` | `Core.SyncEngine.Mapping` | **Pure** — maps staged zip entries to data-root target paths. No disk writes. Returns `MappingPlan`. |
| `ISyncEngine` | `Core.SyncEngine` | Orchestrates Install, Uninstall, CheckForRevertedMods, ReapplyRevertedMods. |
| `IBackupService` | `Core.Backup` | BackupFilesAsync, RestoreAsync, RestoreAllAsync, PruneAsync, GetBackupPath. |

**Storage locations (all under `%APPDATA%\EWSR_PMR_ModApp\`):**
- `manifest.json` — master mod manifest (schema version 1)
- `backups\{modId}\{relativeTargetPath}` — pre-install originals for uninstall
- `mods\{modId}\{sourcePathInZip}` — cached mod payload for post-update reapply
- `staging\{sessionGuid}\` — transient extraction area (cleaned up after install)

**Key file paths:**
- `Common/AppPaths.cs` — single source of truth for all storage paths
- `Common/HashHelper.cs` — SHA-256 file/stream/bytes hashing
- `SyncEngine/Mapping/MappingResolver.cs` — hybrid mapping logic (modinfo → path-overlay → filename-index)
- `SyncEngine/SyncEngine.cs` — install orchestrator; uses `TimeProvider` (injected clock for testability)

**What Slit needs to know to bind the UI:**
- **Primary service to bind:** `ISyncEngine` — exposes `InstallAsync`, `UninstallAsync`, `CheckForRevertedModsAsync`, `ReapplyRevertedModsAsync`
- **Ambiguous confirmations:** `ISyncEngine.InstallAsync` takes a `Func<IReadOnlyList<AmbiguousMapping>, Task<IReadOnlyList<ResolvedMapping>>>` callback — Slit implements this as a dialog or inline list picker. `AmbiguousMapping` has `ZipEntry`, `CandidatePaths`, and `Reason` for display.
- **Progress:** `IProgress<SyncProgress>` parameter on most operations. `SyncProgress` has `Phase` (string), `PercentComplete` (0–100), `CurrentFile` (nullable).
- **Game path:** `IGameLocator.LocateAsync(userConfiguredPath?)` returns `GameLocatorResult(Found, DataRoot, Source, FailureReason?)`. If `Found=false`, show path picker and pass user selection back to `LocateAsync`.
- **Write-access check:** `IGameLocator.CanWriteDataRoot(dataRoot)` — if false, prompt for elevation before install.
- **Mod list:** Load from `IManifestStore.LoadAsync()` → `AppManifest.Mods.Values` (each `ModEntry` has `ModId`, `ModName`, `InstallTimestamp`, `Files`).
- **Admin elevation note:** All write paths to `C:\Program Files\...` are marked with `// NOTE: Writes to C:\Program Files require admin elevation at runtime.` — Slit should trigger UAC before calling install/uninstall/reapply.
- **Test project:** `tests/EWSR_PMR_ModApp.Core.Tests/` (xUnit, 6 smoke tests for MappingResolver). Wez owns thorough tests.

### 2026-05-31: Collision detection and IFileHasher

**`MappingPlan.Collisions` bucket shape:**
```csharp
// New type: SyncEngine/Mapping/CollisionMapping.cs
public sealed class CollisionMapping
{
    public required string RelativeTargetPath { get; init; }
    public required IReadOnlyList<FileMappingResult> Entries { get; init; }
}

// MappingPlan now has four buckets (required):
public required IReadOnlyList<CollisionMapping> Collisions { get; init; }
```
Post-pass `ApplyCollisionDetection()` runs after every strategy in `MappingResolver.Resolve()`.  Groups `Mapped` by `RelativeTargetPath` (OrdinalIgnoreCase); any group with count > 1 is moved to `Collisions`, leaving none in `Mapped`.  SyncEngine surfaces each collision as a warning string in `InstallResult.Warnings` — not auto-installed.

**`IFileHasher` interface + SyncEngine injection:**
```csharp
// New: Abstractions/IFileHasher.cs
public interface IFileHasher
{
    string ComputeHash(string filePath);
}

// New: Abstractions/RealFileHasher.cs
public sealed class RealFileHasher : IFileHasher
{
    public string ComputeHash(string filePath) => HashHelper.ComputeFileHash(filePath);
}
```
`SyncEngine` constructor gains `IFileHasher? hasher = null` (optional, defaults to `new RealFileHasher()`).  All four internal `HashHelper.ComputeFileHash(...)` calls replaced with `_hasher.ComputeHash(...)`.  Existing public behaviour is unchanged; Wez can now inject a fake hasher for re-apply unit tests.

- **Mapping strategy (hybrid):**
  1. **Path-overlay (primary, 90% case):** Mod zip preserves folder structure. Strip `data/` prefix if present; else overlay directly if top-level dirs match known children of `data`.
  2. **Filename-index (fallback, 10% case):** Flat zips. Match by name against an index of all files under `data`. Surface ambiguities to user (correct over convenient).
  3. **Optional modinfo.json (authoritative):** If present in zip root, use as mapping source (advanced mod authors).
- **Manifest per-file recording:** relativeTargetPath, sourcePathInZip, mappingMethod, originalFileHash, installedFileHash, isNewFile. Makes installs deterministic and reversible.
- **Admin elevation:** Validate write access to `data` root under Program Files. Prompt for elevation if needed (prevents #1 silent failure).
- **See:** `docs/ARCHITECTURE.md` "File Mapping Strategy" section for detailed specs.

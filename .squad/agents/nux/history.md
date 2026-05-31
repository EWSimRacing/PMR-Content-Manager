# History: Nux — Core Dev

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

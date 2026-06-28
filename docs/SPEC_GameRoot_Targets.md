# Spec: Game-Root Targets (install files outside `data\`, e.g. `shared\starmap.dds`)

Status: Proposed
Author: Research Analyst (@copilot) for Elliott
Repo: EWSimRacing/PMR-Content-Manager
Related: community report (YSIAD_PIR, Discord 2026-06-21); EWSR Night Sky mod (custom starmap)

---

## 1. Problem

PMR loads some assets from the **game root**, *outside* the `data\` folder. The clearest
examples are the night-sky textures:

```
C:\Program Files\Project Motor Racing\shared\moon_diffuse.dds
C:\Program Files\Project Motor Racing\shared\starmap.dds
```

Content Manager today can only install files under the **data root**
(`...\Project Motor Racing\data`). A mod zip that ships `shared/starmap.dds` is therefore
either misrouted or dropped:

- **Misroute:** `MappingResolver` Strategy 2 / Variant B checks `Directory.Exists(dataRoot\shared)`.
  Because **`data\shared` exists**, the file maps to `...\data\shared\starmap.dds` — but the game
  reads from `...\shared\starmap.dss` (the root). The mod silently installs to the wrong folder
  and has **zero in-game effect**.
- **Reject:** Even if mapping produced an out-of-data path, `PathValidator.IsUnderDataRoot`
  rejects anything outside `dataRoot`, and `WritePlanExecutor` resolves every path as
  `Path.Combine(dataRoot, relative)`. There is no code path that can write to the game root.

This is exactly the limitation reported on Discord: *"PMR CM v1.2 uses the Game Data Path...
it does not update modded files that live in the shared folder like moon and starmap."*

The current safe-but-wrong outcome means EWSR's night-sky mods (and any future root-level
asset) cannot be delivered through CM. The only workaround is an out-of-band admin script that
writes into `C:\Program Files` — which we explicitly do **not** want users running.

### Companion question answered (for the docs)
Where does CM keep originals for uninstall? `BackupService` / `WritePlanExecutor.BackupFiles`
copy each original into `%APPDATA%\EWSR_PMR_ModApp\backups\{modId}\{relativeTargetPath}`
**before** overwriting; uninstall (`RestoreBackups`) copies them back. Originals are **not**
stored inside the game folder.

---

## 2. Current architecture (ground truth)

Single anchor — everything is relative to `DataRoot`:

| Concern | File | Behavior |
|---|---|---|
| Locate game | `GameDetection/GameLocator.cs` | Returns only `DataRoot` (`...\PMR\data`); validates via known data subfolders. No concept of game root. |
| Map zip -> target | `SyncEngine/Mapping/MappingResolver.cs` | Produces `RelativeTargetPath` relative to data root. Variant B misroutes `shared/`. |
| Record install | `Manifest/InstalledFileEntry.cs` | `RelativeTargetPath` documented as "relative under the game **data** root". |
| Plan write | `Elevation/WritePlanRequest.cs`, `FileCopySpec.cs` | Carry `DataRoot` + relative paths only. |
| Execute write | `Elevation/WritePlanExecutor.cs` | `Path.Combine(dataRoot, relative)` for copy/backup/delete/restore. |
| Validate path | `Elevation/PathValidator.cs` | `IsUnderDataRoot` **rejects** rooted paths and anything escaping `dataRoot`. |
| Backup/restore | `Backup/BackupService.cs` | Backups under `%APPDATA%\...\backups\{modId}\{relative}`, restored to `dataRoot`. |
| modinfo schema | `ZipHandling/ModInfo.cs` | `Files: zipPath -> dataRoot-relative target` (or `"install"`). |

The security model is intentionally strict: **the only writable destination is inside `data\`,
and the only allowed source is inside `%APPDATA%\EWSR_PMR_ModApp\`.** Any game-root feature must
preserve that strictness, not loosen it into "write anywhere under Program Files".

---

## 3. Proposed design

Introduce a **TargetRoot** concept threaded end-to-end, plus a **tight allowlist** of game-root
subfolders. Default deny; only explicitly-allowed root subpaths (initially just `shared/`) may be
written outside `data\`.

### 3.1 New concept: `TargetRoot`
```csharp
public enum TargetRoot { Data, Game }   // Data = ...\PMR\data ; Game = ...\PMR
```
Add `TargetRoot` (default `Data`) to:
- `FileMappingResult`
- `InstalledFileEntry`
- `FileCopySpec`

`RelativeTargetPath` stays relative, but is now interpreted against the chosen root.

### 3.2 GameLocator exposes the game root
`DataRoot` is always `{GameRoot}\data`. Add `GameRoot` (the parent of `DataRoot`) to
`GameLocatorResult` and surface it through settings. No new detection logic — derive
`GameRoot = Directory.GetParent(DataRoot)`.

### 3.3 Allowlist (security gate — the crux)
A single source of truth, e.g. `Elevation/GameRootPolicy.cs`:
```csharp
// Subfolders of the GAME ROOT (not data) that mods may write to.
public static readonly string[] AllowedGameRootDirs = { "shared" };
```
New validator method:
```csharp
public static bool IsAllowedGameRootTarget(string gameRoot, string dataRoot, string relativePath)
// true ONLY when:
//   - relativePath is not rooted and has no escaping '..'
//   - Combine(gameRoot, relativePath) stays under gameRoot
//   - its first segment is in AllowedGameRootDirs (e.g. "shared")
//   - it is NOT under dataRoot (data files use the existing IsUnderDataRoot path)
//   - it does NOT target reserved roots (data, x64, updater, sdk, profileTemplate, *.exe at root)
```
This keeps the boundary as strict as today: CM still refuses to write the game executable,
loaders, or arbitrary root files. Adding a new root capability (beyond `shared`) is a deliberate,
reviewable one-line allowlist change.

### 3.4 modinfo.json extension (schemaVersion 2)
Add an explicit, opt-in map for game-root files. Backward compatible: absent => current behavior.
```json
{
  "schemaVersion": 2,
  "name": "EWSR Night Sky",
  "version": "0.1.0",
  "gameRootFiles": {
    "shared/starmap.dds": "shared/starmap.dds"
  }
}
```
- Key = path in zip; value = game-root-relative target.
- CM validates every target against the allowlist (3.3) and rejects (with a clear UI warning)
  any entry that is not allowed — even if modinfo asks for it. modinfo can request, only the
  allowlist grants.
- `Files` (data-root) and `gameRootFiles` (game-root) are both authoritative when present;
  heuristics still run only for unlisted files.

Heuristic safety fix (independent of modinfo): in `MappingResolver` Variant B, do **not** map a
top-level `shared/` directory into `data\shared` by filename/dir coincidence. `shared` should only
reach the game root via an explicit `gameRootFiles` entry, never via overlay heuristics.

### 3.5 Thread TargetRoot through write/backup
- `WritePlanRequest` gains `GameRoot` alongside `DataRoot`.
- `FileCopySpec` and backup/delete entries carry `TargetRoot`.
- `WritePlanExecutor` resolves the base per entry:
  `var baseRoot = spec.TargetRoot == TargetRoot.Game ? request.GameRoot : request.DataRoot;`
  for copy, **backup**, delete, and restore.
- Backup layout must encode the root so restore targets the right base, e.g.:
  ```
  backups\{modId}\__data__\{relative}
  backups\{modId}\__game__\{relative}
  ```
  Restore picks the base from the `__data__` / `__game__` prefix.

### 3.6 Manifest / update detection
`InstalledFileEntry.TargetRoot` lets reapply/update-detection resolve the live file against the
correct base. Existing hash logic (`OriginalFileHash` / `InstalledFileHash`) is unchanged.

---

## 4. Security considerations (Zoe gate)

1. **Default deny.** No writes outside `data\` unless the first path segment is in
   `AllowedGameRootDirs` (initially only `shared`). modinfo cannot widen this.
2. **No traversal / no rooted paths.** Reuse the `GetFullPath` normalization already in
   `PathValidator`; reject `..` escapes and absolute paths.
3. **Source still locked to AppData.** `IsAllowedSource` unchanged — payloads only come from
   `%APPDATA%\EWSR_PMR_ModApp\`.
4. **Reserved roots blocked.** Explicitly refuse `data` (handled separately), `x64`, `updater`,
   `sdk`, `profileTemplate`, and any file directly at the game root (e.g. the launcher exe).
5. **Elevation unchanged.** Same elevated-helper path; we are only widening the *validated*
   destination set by one allowlisted folder, not changing the trust model.
6. **Uninstall correctness.** Root-installed files must be backed up and restored to the game
   root, never to `data\` (prevents leaving a stale `data\shared\starmap.dds` or failing to
   restore the real one).

---

## 5. Acceptance criteria

- [ ] A zip with `modinfo.json` (schemaVersion 2) + `gameRootFiles: { "shared/starmap.dds": "shared/starmap.dds" }`
      installs the file to `{GameRoot}\shared\starmap.dds`.
- [ ] The original `shared\starmap.dds` is backed up to `backups\{modId}\__game__\shared\starmap.dds`
      before overwrite.
- [ ] Uninstall restores the original to `{GameRoot}\shared\starmap.dds` and removes the mod file
      if it was new.
- [ ] A modinfo requesting a non-allowlisted game-root target (e.g. `x64/launcher.exe`,
      `../foo`, `C:\...`) is rejected with a clear UI warning and writes nothing.
- [ ] Heuristic overlay never routes a top-level `shared/` into `data\shared`.
- [ ] Existing data-root mods behave identically (regression: schemaVersion 1 zips unchanged).
- [ ] Unit tests for `PathValidator.IsAllowedGameRootTarget` (allow `shared/...`; deny rooted,
      `..`, reserved roots, data paths) and for executor backup/restore with mixed roots.

---

## 6. Out of scope

- Arbitrary write-anywhere support. Only an allowlisted set (starting with `shared`).
- New asset authoring. The starmap/moon content is produced in EWSR_PMR_Tools; this spec is only
  about CM being able to install/uninstall root files safely.
- Auto-detecting which mods "should" be root files without modinfo (explicit opt-in only).

---

## 7. Affected files (implementation map)

- `Core/GameDetection/GameLocatorResult.cs` (+`GameRoot`), `GameLocator.cs` (derive parent)
- `Core/SyncEngine/Mapping/MappingResolver.cs` (+`gameRootFiles`, Variant B `shared/` guard)
- `Core/SyncEngine/Mapping/FileMappingResult.cs` (+`TargetRoot`)
- `Core/ZipHandling/ModInfo.cs` (+`GameRootFiles`, bump default `SchemaVersion` handling to 2)
- `Core/Manifest/InstalledFileEntry.cs` (+`TargetRoot`)
- `Core/Elevation/WritePlanRequest.cs` (+`GameRoot`), `FileCopySpec.cs` (+`TargetRoot`)
- `Core/Elevation/PathValidator.cs` (+`IsAllowedGameRootTarget`), new `GameRootPolicy.cs`
- `Core/Elevation/WritePlanExecutor.cs` (per-entry base root; rooted backup layout)
- `Core/Backup/BackupService.cs` (rooted backup layout to match executor)
- `Core/SyncEngine/SyncEngine.cs` / `ModSyncService.cs` (populate `GameRoot`, carry `TargetRoot`)
- `docs/MOD_PACKAGING_GUIDE.md` + `docs/ARCHITECTURE.md` (document schema v2 + `gameRootFiles`)
- Tests under `tests/` for validator + executor + mapping.

---

## 8. Rollout

1. Implement Core changes behind schemaVersion 2; keep v1 path untouched.
2. Land unit tests for the validator allowlist and mixed-root backup/restore first (TDD).
3. Ship EWSR Night Sky as the first `gameRootFiles` mod; verify install/uninstall round-trip.
4. Update packaging docs. Draft PR for Zoe (safety) + Elliott (approval) per repo gate.

# History: Nux — Core Dev

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- My domain: file sync engine, zip extraction/validation, game path detection, update-revert recovery, backups.
- User: Elliott Williams.

## Learnings

### 2026-05-31: Stack Decision & Handoff (via Furiosa)
- **Stack:** C# / .NET 10 + WPF. Solution: `EWSR_PMR_ModApp.slnx` at repo root.
- **Your module:** `src/EWSR_PMR_ModApp.Core/` (net10.0-windows, no UI deps).
- **Next:** Implement GameDetection and Manifest first (alphabetically and dependency-wise), then ZipHandling, SyncEngine, Backup.
- **See:** `docs/ARCHITECTURE.md` for full module spec and risk mitigations.
- **Build:** `dotnet build` from repo root; run with `dotnet run --project src/EWSR_PMR_ModApp.UI`.

### 2026-05-31: File Mapping Strategy — Your SyncEngine Implementation (via Scribe)
- **Install target:** All mod-affected files live under `C:\Program Files\Project Motor Racing\data` (user directive).
- **Mapping strategy (hybrid):**
  1. **Path-overlay (primary, 90% case):** Mod zip preserves folder structure. Strip `data/` prefix if present; else overlay directly if top-level dirs match known children of `data`.
  2. **Filename-index (fallback, 10% case):** Flat zips. Match by name against an index of all files under `data`. Surface ambiguities to user (correct over convenient).
  3. **Optional modinfo.json (authoritative):** If present in zip root, use as mapping source (advanced mod authors).
- **Manifest per-file recording:** relativeTargetPath, sourcePathInZip, mappingMethod, originalFileHash, installedFileHash, isNewFile. Makes installs deterministic and reversible.
- **Admin elevation:** Validate write access to `data` root under Program Files. Prompt for elevation if needed (prevents #1 silent failure).
- **See:** `docs/ARCHITECTURE.md` "File Mapping Strategy" section for detailed specs.

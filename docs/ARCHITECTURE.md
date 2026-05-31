# Architecture — EWSR_PMR_ModApp

## Overview

The app is split into two assemblies:

| Assembly | Role |
|----------|------|
| `EWSR_PMR_ModApp.Core` | All business logic — file I/O, zip handling, game detection, manifest, backup. Zero UI dependencies. |
| `EWSR_PMR_ModApp.UI` | Thin WPF shell — drag-and-drop surface, mod list view, settings page. Calls into Core. |

---

## Core Modules (owned by Nux)

### 1. SyncEngine (`Core/SyncEngine/`)
The orchestrator. Coordinates install, uninstall, and the key "re-apply" flow:
- **Install:** Extract zip → map files to game paths → backup originals → copy mod files → update manifest.
- **Uninstall:** Restore originals from backup → remove manifest entry.
- **Re-apply (update recovery):** On app launch or file-watch trigger, compare manifest hashes against on-disk files. If a game update reverted modded files, re-copy from the stored mod payload.

### 2. ZipHandling (`Core/ZipHandling/`)
- Validate zip integrity before extraction (reject partial/corrupt archives).
- Detect common mod packaging patterns (e.g., root folder inside zip vs. flat files).
- Normalize extracted paths so mods from different authors land in the right place.
- Progress reporting for large archives.

### 3. GameDetection (`Core/GameDetection/`)
- Parse Steam's `libraryfolders.vdf` to find the Project Motor Racing install.
- Fallback: check Windows Registry for Steam install path.
- Fallback: manual path selection (stored in user settings).
- Validate that the detected path actually contains the expected game files.

### 4. Manifest (`Core/Manifest/`)
- JSON-based manifest stored in app data (`%APPDATA%/EWSR_PMR_ModApp/manifest.json`).
- Tracks per-mod: source zip hash, list of installed files, original file hashes, install timestamp.
- Enables conflict detection (two mods touching the same file).
- Supports manifest migration if schema changes between app versions.

### 5. Backup (`Core/Backup/`)
- Before any mod install, back up the original game files that will be overwritten.
- Backups stored in a structured folder under app data with manifest cross-reference.
- Restore-on-uninstall and bulk "restore all originals" for clean slate.
- Prune stale backups when mods are removed.

---

## UI Surface (owned by Slit)

### Main Window
- **Drag-and-drop zone:** Drop a `.zip` to trigger install flow.
- **Mod list:** Shows installed mods with status (active / conflict / reverted).
- **Actions per mod:** Enable, disable, uninstall, view details.

### Settings Page
- Game install path (auto-detected with manual override).
- Backup location preference.
- Auto-reapply on launch toggle.

### Status / Log Panel
- Real-time progress during install/extraction.
- Warning/error display for bad zips or permission issues.

---

## Key Design Decisions

1. **Sync engine is pure logic, no UI.** This allows unit testing of all file operations without WPF.
2. **Manifest is the source of truth.** If the manifest says a file is modded, the engine trusts that over disk state.
3. **Backups are mandatory.** No mod install proceeds without a successful backup of originals.
4. **Fail-safe extraction.** Zips are extracted to a temp staging folder first; only after full validation are files moved to game directories.

---

## Risk Mitigations

| Risk | Mitigation |
|------|-----------|
| Game update reverts mods | Hash-compare on launch → auto re-apply |
| Bad/partial zip | Validate before extraction; reject corrupt archives |
| Wrong game path | Multi-strategy detection + validation of expected files |
| File permission errors | Check write access before install; surface clear error to user |
| Conflicting mods | Manifest tracks per-file ownership; warn on overlap |

---

## File Mapping Strategy

### Decision: Hybrid (option C) — path-overlay primary, filename-index fallback

**Install-target root:** `C:\Program Files\Project Motor Racing\data` (resolved by GameDetection; manual override supported).

#### Primary strategy — mirror zip structure (path overlay)

Most racing-sim mod authors package zips that preserve the relative path under the game's `data` folder. The engine handles two common variants:

1. **Zip contains a `data/` root folder** — strip it and overlay contents onto the data root.
2. **Zip starts with a sub-path that exists under `data/`** (e.g. `vehicles/…`, `tracks/…`) — overlay directly onto data root.

Detection heuristic (in order):
- If the zip's top-level entry is exactly `data/` → strip it, overlay remainder.
- Else, if every top-level directory in the zip matches a known child folder of the `data` root → overlay as-is.
- Else → fall through to filename-index fallback.

#### Fallback — filename-index matching

If the zip is flat (no meaningful folder structure), the engine indexes every file currently under `data` by filename. For each file in the zip:
- If exactly one on-disk file shares that filename → map to it.
- If multiple matches → disambiguate by path similarity (Levenshtein or common-suffix scoring). If still ambiguous → surface to the user for manual confirmation before install.
- If zero matches → treat as a **new file** (see edge cases below).

#### Edge-case handling

| Situation | Behaviour |
|-----------|-----------|
| Flat zip, no folders | Use filename-index fallback |
| Zip root IS `data/` | Strip `data/` prefix, overlay |
| Zip root is a subfolder of `data/` (e.g. `vehicles/`) | Overlay directly |
| File in zip has no match on disk (new file) | Allow install if path overlay is unambiguous; prompt user if filename-index produced it (could be typo or genuinely new content) |
| Two mods map to the same target file | Conflict — warn user, record in manifest, last-write-wins with prior mod's version stored in backup stack |
| `modinfo.json` present in zip | Honour it as authoritative mapping (see future enhancement below) |

#### Install manifest fields (per installed file)

```json
{
  "modId": "uuid-of-mod",
  "sourceZipHash": "SHA256 of the original zip",
  "files": [
    {
      "relativeTargetPath": "vehicles/car_a/livery.dds",
      "sourcePathInZip": "data/vehicles/car_a/livery.dds",
      "mappingMethod": "path-overlay | filename-index | modinfo",
      "originalFileHash": "SHA256 of the game's original file (null if new file)",
      "installedFileHash": "SHA256 of the mod file written to disk",
      "isNewFile": false
    }
  ]
}
```

This makes every install fully deterministic and reversible: uninstall restores originals by hash; re-apply compares `installedFileHash` against on-disk state.

#### Future enhancement — `modinfo.json`

The app should accept (but never require) a small manifest inside the zip:

```json
{
  "name": "My Livery Pack",
  "author": "Someone",
  "version": "1.0",
  "files": {
    "livery.dds": "vehicles/car_a/livery.dds",
    "preview.png": "vehicles/car_a/preview.png"
  }
}
```

When present, `modinfo.json` is treated as the authoritative mapping — no heuristics run. This removes all ambiguity for mod authors who opt in. The app works fully without it ("dumb zips just work").

#### GameDetection implications

- Must resolve `data` root as `{gameInstallPath}\data` and validate its existence on startup.
- Default path: `C:\Program Files\Project Motor Racing\data`.
- Since `Program Files` is ACL-protected, the engine must:
  1. Check write access to `data` at startup.
  2. If denied, prompt user to run the app elevated (or request specific folder permission).
  3. Surface a clear error — never silently fail a file copy.

---

## Work Breakdown

- **Nux (Core dev):** Implements modules 1–5 above, with unit tests.
- **Slit (UI dev):** Implements the WPF UI surface, binds to Core services via dependency injection.
- **Furiosa (Lead):** Reviews, architects, resolves design questions.

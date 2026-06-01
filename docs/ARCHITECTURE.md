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

#### Skip Logic

Not every file in a mod zip should be installed to the game directory. The engine classifies each zip entry into one of the following **skip categories**:

| Category | Code | Description | Logged Message Template |
|----------|------|-------------|------------------------|
| **Install** | `Install` | File maps to a valid game data path and will be written. | (no log — normal flow) |
| **DisplayOnly** | `DisplayOnly` | File is meant for user viewing, not installation (README, preview images). Surfaced in mod detail view. | `Display-only: {path} (reason: {type})` |
| **NoPathMatch** | `NoPathMatch` | File doesn't map to any known location under `data/`. | `Skipped (no match in data): {path}` |
| **MetaFile** | `MetaFile` | File is mod manager metadata (e.g., `modinfo.json` itself). | `Skipped (meta): {path}` |
| **HashMatch** | `HashMatch` | File already exists at target with identical hash — no change needed. | `Skipped (unchanged): {path}` |
| **UserExcluded** | `UserExcluded` | User explicitly blocked this file via exclusion list. | `Skipped (user exclusion): {path}` |
| **AmbiguousPending** | `AmbiguousPending` | File location is ambiguous; held for user confirmation. | `Pending confirmation: {path}` |
| **Collision** | `Collision` | Multiple zip entries resolve to same target — none installed. | `Collision: {path}` |
| **UnsafeFile** | `UnsafeFile` | Executable or system file — never installed automatically. | `Blocked (unsafe): {path}` |

##### Skip Policy by File Extension

| Extension | Default Category | Rationale |
|-----------|-----------------|-----------|
| `.xml`, `.hadron`, `.tweakers`, `.i3d` | `Install` | Core PMR game data formats. |
| `.dds` | `Install` | Texture files — primary mod content. |
| `.png`, `.jpg`, `.jpeg` | Conditional | If path matches known game texture location → `Install`. If at zip root or in a folder named `preview`/`images`/`screenshots` → `DisplayOnly`. Otherwise → `NoPathMatch`. |
| `.md`, `.txt` | `DisplayOnly` | Documentation files. Shown in mod detail view, never installed. |
| `.pdf` | `DisplayOnly` | Documentation. |
| `.json` | Conditional | If named `modinfo.json` at zip root → `MetaFile` (parsed, not installed). Otherwise → `Install` if path maps to data, else `NoPathMatch`. |
| `.exe`, `.dll`, `.bat`, `.cmd`, `.ps1`, `.sh` | `UnsafeFile` | Never installed automatically. Mod manager will not execute arbitrary code. |
| `.log`, `.bak`, `.tmp` | `NoPathMatch` | Packaging artifacts — always skipped. |
| `.zip`, `.rar`, `.7z` | `NoPathMatch` | Nested archives — not unpacked recursively. |

##### Processing Order

1. **Integrity check** — reject corrupt/partial zips before any file analysis.
2. **modinfo.json parse** — if present and valid, use its explicit file mappings.
3. **Extension filter** — apply unsafe/meta/display-only rules.
4. **Path mapping** — run hybrid strategy (path-overlay → filename-index fallback).
5. **Hash comparison** — for files that map to existing game files, compare hashes.
6. **User exclusion check** — apply any user-defined skip rules.
7. **Collision detection** — flag multiple zip entries targeting same path.
8. **Ambiguity resolution** — hold ambiguous mappings for user confirmation.

##### Skip Reporting

All skipped files are logged with their category and path. The engine returns:

```csharp
public record SkippedFile(string PathInZip, SkipCategory Category, string Reason);
public record InstallResult(
    bool Success,
    IReadOnlyList<InstalledFile> Installed,
    IReadOnlyList<SkippedFile> Skipped,
    IReadOnlyList<string> Warnings
);
```

The UI displays:
- **During install:** "Installed X files, skipped Y files" with expandable detail list.
- **Mod detail view:** Full file manifest showing status per entry (installed / display-only / skipped / pending).
- **Log panel:** One line per skipped file with category and reason.

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

#### `modinfo.json` Specification (v1)

The app accepts (but never requires) a manifest file named `modinfo.json` at the zip root. When present and valid, it is the **authoritative source** for file mappings — no heuristics run.

##### Full Schema

```json
{
  "$schema": "https://ewsr.dev/schemas/modinfo-v1.json",
  "schemaVersion": 1,
  "name": "EWSR PMR Realism Overhaul",
  "version": "1.1.0",
  "author": "Elliott Williams",
  "description": "Comprehensive realism tweaks for Project Motor Racing.",
  "website": "https://github.com/ElliottWilliams/EWSR_PMR_Tools",
  "minGameVersion": "1.2.0",
  "tags": ["realism", "physics", "AI", "weather"],
  
  "files": {
    "data/tracks/defs/environment/weather_overcast.xml": "install",
    "data/ai/tweakers/ai_difficulty.tweakers": "install",
    "data/vehicles/_shared/physics/hadron/Tire_AI/tire_grip.hadron": "install"
  },
  
  "displayFiles": {
    "README_PMR_Realism_Overhaul_v1.1.md": {
      "label": "Readme",
      "type": "readme"
    },
    "preview.jpg": {
      "label": "Preview Image",
      "type": "preview"
    },
    "CHANGELOG.md": {
      "label": "Change Log",
      "type": "changelog"
    }
  },
  
  "skipFiles": [
    "*.bak",
    "thumbs.db",
    ".DS_Store",
    "__MACOSX/*"
  ],
  
  "dependencies": [
    {
      "modId": "ewsr-physics-base",
      "minVersion": "1.0.0",
      "optional": false
    }
  ]
}
```

##### Field Reference

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | integer | Yes | Always `1` for this spec. Enables future migrations. |
| `name` | string | Yes | Human-readable mod name (shown in UI). |
| `version` | string | Yes | Semver-style version string. |
| `author` | string | No | Mod author name. |
| `description` | string | No | Short description (≤200 chars recommended). |
| `website` | string | No | URL for mod homepage or repo. |
| `minGameVersion` | string | No | Minimum PMR version required. Engine warns if game is older. |
| `tags` | string[] | No | Categorization tags for future filtering. |
| `files` | object | No | Map of `pathInZip` → `"install"` or target path override. If omitted, heuristics run. |
| `displayFiles` | object | No | Files shown in UI but not installed. Keys are paths in zip. |
| `skipFiles` | string[] | No | Glob patterns for files to skip entirely (not shown, not installed). |
| `dependencies` | object[] | No | Other mods this mod requires. Engine warns if missing. |

##### `files` Object Behavior

- **Key:** Path of file within the zip (relative to zip root).
- **Value:** 
  - `"install"` — use path-overlay/heuristic for target location.
  - A string path — explicit target path under `data/` (overrides heuristics).
  - Omitting a file from `files` when `files` is present means that file uses heuristics.

##### `displayFiles` Object

Files listed here are extracted and made available for viewing in the mod detail UI, but are **never** copied to the game directory.

| `type` Value | UI Treatment |
|--------------|--------------|
| `readme` | Rendered as markdown in a scrollable pane. |
| `preview` | Shown as thumbnail in mod list; full-size in detail view. |
| `changelog` | Shown in version history tab. |
| `license` | Shown in legal/license tab. |
| `other` | Generic "view" link in detail view. |

##### `skipFiles` Patterns

Glob patterns matching these rules are ignored entirely:
- Not installed
- Not shown in display files
- Not counted in any totals
- Logged with `Skipped (modinfo skip rule): {path}`

Common patterns to include:
```json
"skipFiles": ["*.bak", "thumbs.db", ".DS_Store", "__MACOSX/*", "*.log"]
```

##### Validation Rules

1. If `schemaVersion` is missing or > current supported version → warning, fall back to heuristics.
2. If `name` or `version` missing → reject modinfo, fall back to heuristics with warning.
3. If a `files` key path doesn't exist in the zip → warning (typo detection).
4. If a `dependencies` entry is missing and `optional: false` → block install, show error.

When present, `modinfo.json` is treated as the authoritative mapping — no heuristics run for files listed in `files`. Files not listed fall through to heuristic mapping. This removes all ambiguity for mod authors who opt in. The app works fully without it ("dumb zips just work").

See also: [MOD_PACKAGING_GUIDE.md](./MOD_PACKAGING_GUIDE.md) for practical packaging instructions.

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

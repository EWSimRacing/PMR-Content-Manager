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

## Work Breakdown

- **Nux (Core dev):** Implements modules 1–5 above, with unit tests.
- **Slit (UI dev):** Implements the WPF UI surface, binds to Core services via dependency injection.
- **Furiosa (Lead):** Reviews, architects, resolves design questions.

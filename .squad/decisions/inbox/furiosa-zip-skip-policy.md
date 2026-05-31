# Decision: Zip Skip Policy — Complete Taxonomy and Handling

**Date:** 2026-05-31T19:17:41-04:00
**By:** Furiosa
**Requested by:** Elliott Williams

## Context

Elliott raised a design concern: when a mod zip is dropped into the manager, some files will be skipped, but users (including Elliott as a mod author) need to understand:
1. Which files are skipped and why
2. How to package zips so nothing important gets skipped accidentally
3. How skipped files are surfaced in the UI

## Decision

### 1. Skip Category Taxonomy

All files in a mod zip are classified into one of these categories:

| Category | Code | Behavior |
|----------|------|----------|
| `Install` | Normal install | Copied to game `data/` directory |
| `DisplayOnly` | Shown in UI | Extracted for viewing, not installed |
| `NoPathMatch` | Skipped | No mapping to game path found |
| `MetaFile` | Skipped | Mod manager metadata (e.g., `modinfo.json`) |
| `HashMatch` | Skipped | Already installed with identical content |
| `UserExcluded` | Skipped | User explicitly blocked this file |
| `AmbiguousPending` | Held | Requires user confirmation |
| `Collision` | Blocked | Multiple sources → same target |
| `UnsafeFile` | Blocked | Executables — never auto-installed |

### 2. Skip Policy by Extension

**Always Install (if path maps):** `.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`

**DisplayOnly:** `.md`, `.txt`, `.pdf` (documentation)

**Conditional Images:** `.png`, `.jpg` — install if in valid game texture path, display-only if at root/preview folder

**Always Skip:** `.exe`, `.dll`, `.bat`, `.ps1`, `.log`, `.bak`, `.tmp`, nested archives

**Meta:** `modinfo.json` at zip root

### 3. modinfo.json v1 Spec

Full schema defined in ARCHITECTURE.md with:
- Required: `schemaVersion`, `name`, `version`
- Optional: `author`, `description`, `website`, `minGameVersion`, `tags`
- File control: `files`, `displayFiles`, `skipFiles`, `dependencies`

When `modinfo.json` is present and valid, its `files` mapping is authoritative — no heuristics run for listed files.

### 4. UI Surfacing

- **During install:** "Installed X, skipped Y" with expandable detail dropdown
- **Mod detail view:** Full file list with status column (installed / display-only / skipped)
- **Log panel:** One line per skipped file with category and reason
- **InstallResult API:** Returns `IReadOnlyList<SkippedFile>` with path, category, reason

### 5. Documentation

Created `docs/MOD_PACKAGING_GUIDE.md` — practical packaging guide for mod authors (Elliott's reference when building EWSR zips).

## Why

- **Transparency:** Users need to know what happened to every file in their zip
- **Actionability:** Warnings must name specific files (per existing decision)
- **Author guidance:** Elliott builds mods via EWSR_PMR_Tools — he needs clear rules
- **Safety:** Never auto-install executables; always block unsafe files
- **Flexibility:** Display-only preserves README/preview access without polluting game directory

## Impact

- `docs/ARCHITECTURE.md` — updated with Skip Logic section and full modinfo.json spec
- `docs/MOD_PACKAGING_GUIDE.md` — new file for mod authors
- Nux: implement `SkipCategory` enum, `SkippedFile` record, extension filtering in `ZipService`
- Slit: update install result display to show skip breakdown

## Review Needed

Elliott should review:
1. Skip category taxonomy — any missing cases?
2. Extension policy — any PMR file types not covered?
3. modinfo.json spec — any fields needed for EWSR workflow?
4. Packaging guide — clear enough for EWSR_PMR_Tools integration?

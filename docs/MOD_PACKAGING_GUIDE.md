# EWSR Mod Packaging Guide

This guide defines how to structure mod zips for Project Motor Racing so they install cleanly through the EWSR Mod Manager.

## Quick Start — The 90% Case

If your mod follows these rules, it will "just work" with zero configuration:

1. **Mirror the game's folder structure** starting from `data/`
2. **Put documentation at the zip root** (not inside `data/`)
3. **Use standard file extensions** (`.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`)

Example zip structure:
```
MyMod_v1.0.zip
├── README.md                    ← displayed, not installed
├── preview.jpg                  ← displayed, not installed
├── data/
│   ├── tracks/
│   │   └── defs/
│   │       └── environment/
│   │           └── weather_clear.xml
│   ├── ai/
│   │   └── tweakers/
│   │       └── ai_aggression.tweakers
│   └── vehicles/
│       └── _shared/
│           └── physics/
│               └── hadron/
│                   └── Tire_AI/
│                       └── tire_wear.hadron
```

That's it. Drop this zip into the mod manager and everything lands in the right place.

---

## Skip Behavior — What Gets Installed vs. Skipped

The mod manager categorizes every file in your zip:

### ✅ Installed (copied to game)
- Files inside `data/` that match the game's folder structure
- Standard mod formats: `.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`
- `.png`/`.jpg` files inside valid game texture paths

### 📄 Display-Only (shown in UI, not installed)
- `.md`, `.txt`, `.pdf` files — documentation
- Images at zip root or in `preview/`/`images/`/`screenshots/` folders
- Files explicitly marked in `modinfo.json` `displayFiles`

### ⛔ Skipped (ignored entirely)
- Files that don't map to any game path
- Packaging artifacts: `.bak`, `.log`, `.tmp`, `thumbs.db`, `.DS_Store`
- Nested archives: `.zip`, `.rar`, `.7z`
- Executables: `.exe`, `.dll`, `.bat`, `.ps1` (safety — never auto-installed)
- `modinfo.json` itself (parsed as metadata, not installed as game file)

### ⚠️ Held for Confirmation
- Files with ambiguous paths (multiple possible targets)
- Flat zips where filename matching found multiple candidates

---

## Folder Structure Options

The mod manager accepts three packaging styles:

### Option A: Full `data/` prefix (recommended)

Your zip mirrors the exact game path from `data/` down:

```
mod.zip
├── data/
│   └── tracks/
│       └── defs/
│           └── environment/
│               └── weather.xml
```

The engine strips `data/` and installs to: `{game}/data/tracks/defs/environment/weather.xml`

### Option B: Direct subfolder (also works)

Your zip starts with a known `data/` child folder:

```
mod.zip
├── tracks/
│   └── defs/
│       └── environment/
│           └── weather.xml
```

The engine detects `tracks/` is a valid `data/` child and overlays correctly.

### Option C: Flat files (works but not recommended)

```
mod.zip
├── weather.xml
├── ai_behavior.tweakers
```

The engine uses filename matching to find targets. Works for unique filenames, but if multiple game files share a name, you'll be prompted to resolve the ambiguity.

**Recommendation:** Always use Option A or B. Flat zips cause friction.

---

## Documentation Files

Files at the **zip root** with these extensions are treated as documentation:

- `.md` — Markdown readme
- `.txt` — Plain text readme
- `.pdf` — PDF documentation

These are:
- **Shown** in the mod detail view (scrollable, rendered)
- **NOT** copied to the game directory

### Where to Put Your README

✅ **Correct:**
```
mod.zip
├── README.md              ← at zip root
├── data/
│   └── ...
```

❌ **Wrong:**
```
mod.zip
├── data/
│   ├── README.md          ← inside data/ — will be skipped with warning!
│   └── ...
```

If your README is inside `data/`, the engine has no game path to map it to, so it gets skipped entirely. Move it to the zip root or use `modinfo.json` to mark it as display-only.

---

## Preview Images

A preview image helps users identify your mod in the list. The engine auto-detects preview images by:

1. **Name:** Files named `preview.*`, `thumbnail.*`, `cover.*` at zip root
2. **Location:** Images in a `preview/`, `images/`, or `screenshots/` folder
3. **Explicit:** Via `modinfo.json` `displayFiles` with `type: "preview"`

Supported formats: `.jpg`, `.jpeg`, `.png`

### Where to Put Your Preview

✅ **Correct:**
```
mod.zip
├── preview.jpg            ← at zip root
├── README.md
├── data/
│   └── ...
```

Or in a dedicated folder:
```
mod.zip
├── images/
│   └── mod_preview.png    ← in images/ folder
├── data/
│   └── ...
```

---

## The `modinfo.json` Manifest

For full control, include a `modinfo.json` at your zip root. This is **optional** but eliminates all ambiguity.

### Minimal Example

```json
{
  "schemaVersion": 1,
  "name": "EWSR Realism Overhaul",
  "version": "1.1.0"
}
```

Even this minimal manifest helps — the mod manager uses it for display name and version tracking.

### Full Example (EWSR Standard)

```json
{
  "schemaVersion": 1,
  "name": "EWSR PMR Realism Overhaul",
  "version": "1.1.0",
  "author": "Elliott Williams",
  "description": "Comprehensive physics, AI, and weather realism tweaks.",
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
      "label": "Preview",
      "type": "preview"
    }
  },
  
  "skipFiles": [
    "*.bak",
    "thumbs.db",
    ".DS_Store",
    "__MACOSX/*"
  ]
}
```

### Field Reference

| Field | Required | Purpose |
|-------|----------|---------|
| `schemaVersion` | Yes | Always `1`. Enables future migrations. |
| `name` | Yes | Display name in mod manager UI. |
| `version` | Yes | Version string (semver recommended). |
| `author` | No | Your name/handle. |
| `description` | No | Short summary (≤200 chars). |
| `website` | No | Link to homepage or repo. |
| `minGameVersion` | No | Warns user if their game is older. |
| `tags` | No | For future filtering/search. |
| `files` | No | Explicit install mapping. Omit to use heuristics. |
| `displayFiles` | No | Files to show in UI but not install. |
| `skipFiles` | No | Glob patterns for files to ignore completely. |
| `dependencies` | No | Other mods required (future feature). |

### When to Use `files`

Use explicit `files` mapping when:
- Your zip structure doesn't mirror the game exactly
- You want to override where specific files go
- You're distributing multiple variants in one zip

For most mods, omit `files` and let the engine use path-overlay.

---

## Complete EWSR Mod Template

Here's the recommended structure for EWSR mods:

```
EWSR_PMR_Realism_Overhaul_v1.1.zip
│
├── modinfo.json                           ← manifest (optional but recommended)
├── README_PMR_Realism_Overhaul_v1.1.md    ← documentation (displayed)
├── CHANGELOG.md                           ← change history (displayed)
├── preview.jpg                            ← preview image (displayed)
│
├── data/                                  ← game files start here
│   ├── tracks/
│   │   ├── defs/
│   │   │   └── environment/
│   │   │       ├── weather_clear.xml
│   │   │       ├── weather_overcast.xml
│   │   │       └── weather_rain.xml
│   │   └── trees/
│   │       └── STreelibrary/
│   │           └── materials/
│   │               └── tree_bark.i3d
│   │
│   ├── ai/
│   │   └── tweakers/
│   │       ├── ai_difficulty.tweakers
│   │       └── ai_aggression.tweakers
│   │
│   └── vehicles/
│       └── _shared/
│           └── physics/
│               └── hadron/
│                   └── Tire_AI/
│                       ├── tire_grip.hadron
│                       └── tire_wear.hadron
```

### Corresponding `modinfo.json`

```json
{
  "schemaVersion": 1,
  "name": "EWSR PMR Realism Overhaul",
  "version": "1.1.0",
  "author": "Elliott Williams",
  "description": "Physics, AI, and weather realism for PMR.",
  "website": "https://github.com/ElliottWilliams/EWSR_PMR_Tools",
  "minGameVersion": "1.0.0",
  "tags": ["realism", "physics", "AI", "weather", "EWSR"],
  
  "displayFiles": {
    "README_PMR_Realism_Overhaul_v1.1.md": {
      "label": "Readme",
      "type": "readme"
    },
    "CHANGELOG.md": {
      "label": "Change Log", 
      "type": "changelog"
    },
    "preview.jpg": {
      "label": "Preview",
      "type": "preview"
    }
  },
  
  "skipFiles": [
    "*.bak",
    "thumbs.db",
    ".DS_Store",
    "__MACOSX/*",
    "*.log"
  ]
}
```

---

## Common Mistakes

### ❌ README inside `data/`

```
mod.zip
├── data/
│   ├── README.md          ← WRONG — gets skipped
│   └── tracks/...
```

**Fix:** Move README to zip root.

### ❌ Flat zip with common filenames

```
mod.zip
├── config.xml             ← ambiguous — many game files named config.xml
├── settings.xml
```

**Fix:** Include full path structure.

### ❌ Including build artifacts

```
mod.zip
├── data/...
├── build.log              ← pollutes the mod
├── backup.bak
├── .git/                  ← never include VCS folders
```

**Fix:** Add to `skipFiles` or exclude from your build script.

### ❌ Nested archives

```
mod.zip
├── actual_mod.zip         ← won't be unpacked!
```

**Fix:** Flatten the structure. One zip, all files directly inside.

---

## Checklist Before Release

- [ ] All game files are under `data/` with correct folder structure
- [ ] README/docs are at zip root (not inside `data/`)
- [ ] Preview image is at zip root or in `images/` folder
- [ ] No build artifacts (`.log`, `.bak`, `.tmp`)
- [ ] No OS junk (`.DS_Store`, `thumbs.db`, `__MACOSX/`)
- [ ] No VCS folders (`.git/`, `.svn/`)
- [ ] `modinfo.json` has correct `name` and `version`
- [ ] Tested install with the mod manager

---

## Technical Reference

For full technical details, see:
- [ARCHITECTURE.md — Skip Logic](./ARCHITECTURE.md#skip-logic) — category taxonomy and processing order
- [ARCHITECTURE.md — modinfo.json Specification](./ARCHITECTURE.md#modinfojson-specification-v1) — complete schema reference

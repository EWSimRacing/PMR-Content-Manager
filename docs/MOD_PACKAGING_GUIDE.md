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
- Files explicitly listed in `modinfo.json` `gameRootFiles` when the target is allowlisted (currently `shared/`)
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

### Reviewed Cockpit Immersion Audio — native support, never script execution

PMR CM has one narrowly scoped native adapter for the supplied
`Cockpit_Immersion_Audio_PMR_v1.0.3.zip`. Recognition is by the **entire archive's
SHA-256**, not its filename, script names or dynamically interpreted script text:

```
494c21136d015a04766ae317e9f13967fb59718248fcfab28371c76414e5fa12
```

This intentionally supports only the reviewed bytes. A renamed byte-identical copy
works; edited, repacked or different-version archives need separate review.
No `.ps1`, `.bat`, shell command or downloaded code from this package is executed.

CM reads existing `data/vehicles/**/*audioConfig.xml` files (including the shared
default config), requires a valid unnamespaced `audioConfig` root and changes exactly:

| Direct child setting | `value` |
|---|---|
| `interiorReverbMix` | `0.24` |
| `rtmEngineVolume` | `6.2` |
| `rtmExhaustVolume` | `3.2` |
| `rtmTransmissionVolume` | `4.2` |
| `rtmInteriorFXWindVolume` | `0.95` |
| `rtmInteriorFXRattlesVolume` | `-3.0` |

Missing settings/`value` attributes are added. Existing unrelated attributes, elements
and comments remain; the EWSR credit comment is inserted once before the first element.
Existing whitespace and UTF-8/UTF-16 encoding/BOM are retained where possible; XML
serialization may normalize quotes and empty-tag spacing. Other encodings, namespaces,
duplicate target settings, nested setting elements, DTDs, oversized XMLs (over the
4 MiB character limit), and linked/reparse targets are rejected rather than guessed.
One incompatible target aborts preparation before any game writes.

Generated XMLs are staged as normal `data/` assets. The standard write helper checks
their hashes and original baseline hashes before backing up/writing; original backups,
manifest and cached generated payloads support byte-exact uninstall and safe reapply.
Existing ownership conflicts and duplicate installation of the native mod are rejected.

Writer selection is request-aware: Program Files targets always use the elevated helper;
a writable parent directory is not evidence that an existing XML's ACL permits overwrite.
ReadOnly bits are handled only on the specific managed files and restored after writes.
Rollback/uninstall restore snapshot/backup attributes as well as bytes, without ACL or
ownership changes. File locks or genuinely denied permissions still fail with the named
file and existing rollback/recovery reporting.

Reapply only writes a generated file if its current bytes match the recorded original
baseline and its cached payload still matches the installed hash. Missing/changed
baselines or ownership conflicts are held with named warnings, not silently overwritten.
Backups are not refreshed. Under the existing uninstall policy, uninstall restores
the **pre-install** bytes, which may belong to an older game version after an update.
CM does not automatically regenerate a patch against a changed vanilla baseline;
that requires deliberate current-version validation.

### Other standalone script installers are not game-file packages

A ZIP other than the exact native-supported archive containing only apply/restore scripts (`.ps1`, `.bat`, etc.) and documentation
has no game assets for PMR CM to copy. It is rejected as an **Unsupported Mod Package**,
with the blocked filenames listed, before any game writes, backups or manifest changes.
Changing the configured `data` folder does not make such an archive installable.

For example, `Cockpit_Immersion_Audio_PMR_v1.0.3.zip` contains:

```
Apply_Cockpit_Immersion_Audio.ps1
Restore_Cockpit_Immersion_Audio.ps1
Run_Apply_Cockpit_Immersion_Audio.bat
Run_Restore_Cockpit_Immersion_Audio.bat
README.md
CHANGELOG.md
```

Its original apply script edits the installed `data\vehicles\**\*audioConfig.xml` files;
it does not include replacement XML files. The native adapter supplies those
replacements without executing the installer. For any unsupported version, consult
its README or obtain an asset-based ZIP; manual script installation uses backups
outside PMR CM's managed workflow.

Read-only inspection of the reported installation on 2026-10-03 confirmed 72 matching
XML filenames, including these exact game-root-relative destinations:

```
data/vehicles/Acura_ARX-06_2023/data/Acura_ARX-06_2023_audioConfig.xml
data/vehicles/_shared/audio/default_audioConfig.xml
```

The script's recursive filename pattern includes the shared default config, not just
per-car configs. The destinations exist, but their presence cannot supply the missing
replacement contents in the ZIP. These are packaging examples, not a fixed inventory
for every game version.

For conventional CM compatibility, an author can supply modified game assets at their exact
`data/vehicles/...` paths (and document the compatible game version). Adding a `data/`
prefix to scripts or declaring them as file mappings does not bypass executable
protections. Declared hooks are a separate, explicit-consent feature, not a way to
turn a script-only ZIP into a managed game-file installation.

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

An explicit `data/` anchor can also have up to three common enclosing folders, e.g.
`Download/MyMod/v1/data/tracks/defs/environment/weather.xml`. Original ZIP source paths
remain recorded in the manifest. Multiple unrelated wrappers are not silently flattened.
Known direct game folders take precedence over wrapper detection, so
`vehicles/Car/data/Car_audioConfig.xml` keeps its nested game `data` folder.
Rooted/traversal/alternate-stream paths and case-colliding duplicate ZIP entries are rejected
before extraction. Symlink/junction destinations are not written.

### Option B: Direct subfolder (also works)

Your zip starts with a known `data/` child folder:

```
mod.zip
├── tracks/
│   └── defs/
│       └── environment/
│           └── weather.xml
```

The engine detects `tracks/` is a valid `data/` child and overlays correctly. A top-level `shared/` folder is not mapped by this heuristic; use `gameRootFiles` for game-root assets.

### Option C: Flat files (works but not recommended)

```
mod.zip
├── weather.xml
├── ai_behavior.tweakers
```

The engine uses filename matching to find targets. Works for unique filenames, but if multiple game files share a name, you'll be prompted to resolve the ambiguity.

**Recommendation:** Always use Option A or B. Flat zips cause friction.

Known loose/direct asset formats are mapping candidates, not permission to copy to
an arbitrary destination. A filename must match an existing indexed asset, or a direct
folder must be known under the selected `data` root. Mixed `data/` and loose files are
resolved individually. Duplicate destinations are held out of installation; if no
files can safely map, the error lists the unmapped/colliding filenames.

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
| `schemaVersion` | Yes | `1` for data mappings; `2` also enables allowlisted game-root mappings and fallback for unlisted assets. Unsupported versions are rejected. |
| `name` | Yes | Display name in mod manager UI. |
| `version` | Yes | Version string (semver recommended). |
| `author` | No | Your name/handle. |
| `description` | No | Short summary (≤200 chars). |
| `website` | No | Link to homepage or repo. |
| `minGameVersion` | No | Warns user if their game is older. |
| `tags` | No | For future filtering/search. |
| `files` | No | Explicit install mapping. Omit to use heuristics. |
| `gameRootFiles` | No | Schema v2 map for allowlisted game-root files such as `shared/starmap.dds`. |
| `displayFiles` | No | Files to show in UI but not install. |
| `skipFiles` | No | Glob patterns for files to ignore completely. |
| `dependencies` | No | Other mods required (future feature). |

### When to Use `files`

Use explicit `files` mapping when:
- Your zip structure doesn't mirror the game exactly
- You want to override where specific files go
- You're distributing multiple variants in one zip

For most mods, omit `files` and let the engine use path-overlay.

Mapping values are safe relative destination paths, or `"install"` to use normal
mapping heuristics for that listed source (not a literal file named `install`).
Exact full source-path keys take precedence over basename keys. Malformed metadata,
null mappings and unsafe targets are rejected instead of silently falling back.
Executable/script protections still run before ordinary explicit mappings.

### Installing Game-Root Files (`shared/`)

Some PMR assets live beside `data/`, for example `{game}/shared/starmap.dds`. These require `schemaVersion: 2` and an explicit `gameRootFiles` map:

```json
{
  "schemaVersion": 2,
  "name": "Night Sky",
  "version": "1.0.0",
  "gameRootFiles": {
    "shared/starmap.dds": "shared/starmap.dds"
  }
}
```

Only allowlisted game-root folders are accepted. Currently that means `shared/`; targets under `data/`, `x64/`, `updater/`, `sdk/`, `profileTemplate/`, rooted paths, and `..` traversal are rejected.

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

# CHANGELOG

## v1.3 — 2026-06-28

### New Feature — Game-Root Install Targets

PMR CM can now install mod files that live OUTSIDE the `data\` folder — for example the night-sky `shared\starmap.dds`. Previously CM could only write under the Game Data Path; root-level files were silently dropped.

- **Game-root targets** — mods declare `gameRootFiles` in `modinfo.json` (schema 2). Allowed root locations are restricted to a safe allowlist (`shared`).
- **Elevation for root writes** — files going to `C:\Program Files\Project Motor Racing\shared\` are written via the elevated helper.
- **Full backup/restore** — originals are backed up before overwrite and restored on uninstall, same as data files.
- Self-contained single-file build (no .NET runtime required).

---

## v1.2 — 2026-06-19

### New Feature — Mod Lifecycle Hook Scripts

PMR CM can now automatically run PowerShell scripts bundled inside a mod zip at install time and at uninstall time. This is designed for mods that need to do work beyond simple file copying — for example, patching binary AI line cache files after the mod's XML data has been installed.

- **Post-install hook** — runs after all mod files have been copied to the game directory. A confirmation dialog shows the script name, description, and an elevation notice before anything executes. Running the script is always optional — clicking Skip leaves the mod fully installed.
- **Post-uninstall hook** — runs after CM has restored original game files. Same confirmation dialog. Useful for cleanup that CM's file-restore mechanism doesn't cover (e.g. reverting binary patches).
- **UAC elevation** — hooks that require elevation launch via a UAC prompt so they can write to `C:\Program Files\`. Non-elevated hooks have their output captured and shown on failure.
- **Scripts are cached** — hook scripts are stored in `%AppData%\EWSR_PMR_ModApp\scripts\` at install time so the uninstall hook is available even if the original zip is gone.
- **Safe by default** — only scripts explicitly declared in `modinfo.json` are permitted to run. Any executable or script file in a zip that is *not* declared as a hook remains blocked as before.
- **Soft failure** — if a hook script fails or is skipped, the mod install/uninstall completes normally. Game files are always left in a consistent state regardless of hook outcome.

---

## v1.0.1 — 2026-06-15

### Changed

- **Single-player only notice** — a permanent banner on the main screen now makes clear that mods change game files and do not work in online multiplayer (online sessions verify the original vanilla files).
- **Friendlier file-conflict notice** — when a newly installed mod shares files with an already-installed mod, a calm informational dialog now explains in plain language that both mods touch the same files and the most recently installed one is active (no action needed), instead of the previous alarming "warning" wording. Genuine skipped-file warnings keep their own dialog with clearer text.

## v1.0.0 — 2026-06-13

First public release of **PMR CM** (Project Motor Racing Content Manager).

### Features

- **Drag-and-drop mod install** — drop one or more mod `.zip` files onto the app; files are automatically mapped to the correct PMR `data/` subdirectories and installed.
- **Browse to install** — "Browse for .zip…" button as an alternative to drag-and-drop. Supports multi-select.
- **Smart game detection** — locates PMR automatically via Steam registry or the default Program Files path. User-configurable path override in Settings.
- **Automatic backup** — every original game file is backed up to `%APPDATA%\EWSR_PMR_ModApp\backups\` before it is overwritten. No data is ever destroyed.
- **One-click uninstall per mod** — restores original game files from backup; removes the mod from the installed list.
- **Re-check & Reapply** — detects mods that a game update has silently reverted and reapplies them in a single UAC prompt.
- **UAC elevation** — only the file-write step requests admin privileges (via `PMR CM.Helper.exe`). The main UI runs as a normal user.
- **Conflict detection** — warns when two mods both own the same game file; last-write-wins is the resolution (with a visible warning).
- **Ambiguous mapping dialog** — when a zip entry could map to more than one game path, the user is asked to confirm the intended target.
- **File-type whitelist** — only known PMR game-data extensions are installed (`.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`, `.ini`, `.cfg`, `.bin`, `.lut`, `.json`, `.eval`). Unknown files are skipped with a warning; no malicious payloads can be written.
- **Path traversal protection** — Helper validates every destination path is inside the game's `data/` folder; no path-traversal attacks are possible.
- **First-run consent** — one-time dialog informs the user that PMR CM modifies game files and that backups are automatic, before any install is permitted.
- **Game-not-found banner** — if PMR cannot be auto-detected at startup, a visible amber banner explains the situation and links directly to the Settings path picker.
- **Settings panel** — configure the game data path manually; persisted to `%APPDATA%\EWSR_PMR_ModApp\ui-settings.json`.
- **Version displayed in About** — the About section in Settings shows the app version bound from the assembly.
- **Single-file portable distribution** — ships as `PMR CM.exe` + `PMR CM.Helper.exe`; no runtime install or DLL folder required.
- **MIT licence** — open source; contributions welcome.

### Roadmap (post-v1.0)

- **Inno Setup installer** — Start menu shortcut, Add/Remove Programs entry, install to `%LocalAppData%\PMR CM\`. Planned for v1.1.
- **Restore-all button in Settings** — one-click revert of all installed mods to vanilla. Code exists; UI button coming in v1.1.
- **Auto-update check** — startup version check against a `version.json` on GitHub; non-blocking "Update available" banner. v1.1.
- **Code signing** — EV cert to eliminate SmartScreen warnings. Post-v1.0 investment.
- **CI/CD** — GitHub Actions release workflow (`dotnet test` + `dotnet publish` + attach artifact). v1.1.
- **Dedicated About dialog** — full modal with version, GitHub link, licence, and update check button. v1.1.

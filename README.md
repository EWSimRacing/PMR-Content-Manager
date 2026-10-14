# PMR CM — Project Motor Racing Content Manager

**PMR CM** is a Windows mod manager for *Project Motor Racing*. Drop a mod `.zip` onto the app and it installs the files into the right game directories — and re-applies them automatically when a game update reverts your mods.

> **Community tool — not affiliated with or endorsed by the PMR developers.**  
> PMR CM backs up every original file before making any change. You can restore to vanilla at any time.
>
> **Single-player only:** mods change game files and will not work in online multiplayer — those sessions verify the original (vanilla) game files. Use mods for offline / single-player only.

---

## Download

Get the latest release from [GitHub Releases](../../releases) or [Overtake.gg](#).  
Current version: **v1.3.4**

---

## How to Use

1. **Download** `PMR-Content-Manager-v1.3.4-win-x64.zip` from the Releases page.
2. **Extract** the zip anywhere you like (Desktop, Documents, wherever). No installer needed.
3. **Run `PMR CM.exe`**.
   - On first launch, Windows may show **"Windows protected your PC — unrecognised app"**.  
     Click **More info → Run anyway**. This is normal for any unsigned community tool — PMR CM is open source (MIT); you can inspect the code.
4. **First-run consent** — PMR CM will ask once: "PMR CM modifies Project Motor Racing game files…". Click **Continue**.
5. PMR CM will **auto-detect** your PMR installation. If it doesn't find it, an amber banner appears — click **Open Settings** and paste or browse to your PMR `data` folder (usually `C:\Program Files\Project Motor Racing\data`).
6. **Drag a mod `.zip`** onto the drop zone, or click **Browse for .zip…**.
7. PMR CM maps the mod files to the correct game directories, backs up your originals, and installs. A UAC prompt will appear for the write step — this is required because PMR lives in Program Files.
8. Close any post-install warnings dialog to let **INSTALLED MODS** refresh. Click **Uninstall** to restore originals.

The reviewed `Cockpit_Immersion_Audio_PMR_v1.3.4.zip` is supported by a **built-in native
audio transformation**. Drop or browse to the original ZIP: CM recognizes its exact
SHA-256, stages modified car/shared audio XMLs, then uses its normal backups, helper,
manifest and uninstall workflow. **None of its PowerShell or batch scripts run.**
Renamed copies with identical bytes work; edited/repacked archives do not qualify.

Other script-only ZIPs remain unsupported. For conventional packages CM supports
`data/`, up to three enclosing wrapper folders, known direct relative folders,
unique loose asset filenames and explicit `modinfo.json` mappings. Ambiguous filenames
require confirmation; colliding destinations are not installed.
See the [packaging guide](docs/MOD_PACKAGING_GUIDE.md).

Bundled ZIPs are skipped, not recursively installed. A warning about an optional
ZIP does not mean the outer mod's mapped assets failed to install. For example,
TireSense's two `.eval` assets install independently of its bundled Cockpit Immersion
Audio ZIP. Install that audio mod separately using the original supported ZIP if
it is not already listed in **INSTALLED MODS**.

### After a game update

PMR updates may silently overwrite modded files. Click **Re-check & Reapply** (toolbar) to detect and re-apply any reverted mods in one step.

For native Cockpit Immersion Audio, only a byte-identical return to the recorded
pre-install baseline is reapplied. Changed/missing XMLs, altered cached payloads or
ownership conflicts are **held for review**, not overwritten with stale XML.
Backups are retained. Uninstall still restores the original pre-install bytes
(including an older game-version baseline); it does not invent or refresh vanilla
files after an update. Resolve those cases against the current game version before
installing again.

---

## SmartScreen Note

Windows SmartScreen may warn that PMR CM is "unrecognised". This happens with any unsigned application. PMR CM is open-source; you can read every line of code in this repo.

To proceed: click **More info → Run anyway** in the SmartScreen dialog.  
The Helper process (`PMR CM.Helper.exe`) also requests admin (UAC) — this is the component that writes files into the Program Files folder.

---

## How Backups & Restore Work

### Protected files and ReadOnly attributes

The UI stays non-elevated so Explorer drag/drop works. Installs, uninstall and reapply
under Program Files always use the short-lived administrator helper, even if the
`data` directory allows creating new files: an existing game file can have a different,
more restrictive ACL. Other locations are checked against the actual write targets.

For a managed file marked ReadOnly, only that file's ReadOnly bit is temporarily cleared
during copying and restored afterward. Install/reapply retain its destination attributes;
uninstall and rollback restore the original backup/snapshot attributes and bytes.
Backups and recovery snapshots themselves may remain ReadOnly until cleanup.
CM never changes game ACLs, ownership, or directory-wide permissions.

If the helper still reports access denied after UAC approval, report the named file
and whether UAC appeared/was approved. An explicit denied ACL or a locked file is not
silently overridden. Do not run the entire UI as administrator or disable protections globally.

- Before any file is overwritten, PMR CM saves the original to `%APPDATA%\EWSR_PMR_ModApp\backups\`.
- Clicking **Uninstall** on a mod in the list restores all its original files.
- If something goes wrong, your originals are always in that backups folder.

Install and reapply file writes capture separate **pre-operation snapshots**. If a copy
fails, CM stops and restores attempted targets byte-for-byte, deleting files newly
created by that operation. Reapply rolls back to the state immediately before reapply,
not to an older vanilla backup. Existing uninstall backups are never overwritten by retries.

If rollback itself is blocked (for example, a file becomes locked), the failure message
includes both the copy error and recovery errors. **Do not retry or uninstall until
recovery is complete.** Snapshots and `journal.json` are retained under
`%APPDATA%\EWSR_PMR_ModApp\write-recovery\<operation-id>\`: each journal entry records
the target, its pre-operation snapshot (`Backup`), and whether a write was attempted.
After resolving locks/permissions, manually restore attempted targets from their snapshots;
an attempted entry with no snapshot denotes a previously absent file to remove.
Keep the journal and backups until recovery is verified. Recovery is not automatically
replayed; process termination/power loss and the UI's later cache/manifest writes are
not an atomic filesystem transaction.

---

## Making a CM-Compatible Mod Zip

Mod authors: structure your zip so all entries are rooted at `data/` using forward slashes.

```
data/vehicles/physics/my_car.hadron
data/environments/textures/my_texture.dds
data/ui/some_config.xml
```

Inside an accepted `data/` layout, assets are installed unless blocked as executables,
scripts, documentation or packaging artifacts. Loose/direct candidates use known
formats: `.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`, `.ini`, `.cfg`, `.bin`, `.lut`,
`.json`, `.eval`; an unknown extension needs an explicit safe mapping.
Unsafe script/executable extensions cannot be enabled by ordinary file mappings.
Root README/CHANGELOG files are documentation, never game payload.

See [docs/MOD_PACKAGING_GUIDE.md](docs/MOD_PACKAGING_GUIDE.md) for full packaging rules.

---

## Licence

MIT — Copyright 2026 Elliott Williams. See [LICENSE](LICENSE).

---

## For Developers — Building from Source

See [CONTRIBUTING](#contributing) below for build instructions.

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later
- Windows 10 / 11

### Build & Run

```powershell
# Restore dependencies
dotnet restore

# Build
dotnet build

# Run
dotnet run --project src/EWSR_PMR_ModApp.UI

# Tests
dotnet test
```

### Release (portable single-file build)

```powershell
# Add the next version's release notes to CHANGELOG.md, then create the package
.\Release.ps1 -Bump patch

# Publish on GitHub too (commit/push source changes before running)
.\Release.ps1 -Bump patch -PublishToGitHub
```

Output: `artifacts\PMR-Content-Manager-v<version>-win-x64.zip` and its SHA-256
checksum. The package contains self-contained `PMR CM.exe` and `PMR CM.Helper.exe`,
README, changelog and licence; no .NET runtime installation is required.
Use `minor` or `major` instead of `patch` when appropriate. Output paths must be
fresh; existing packages are never deleted or overwritten.

`Release.ps1` validates the solution, synchronizes app/helper versions and packages
the portable build using `publish.ps1`. GitHub publication is opt-in and requires
committed source on `master`, pushed to `origin/master`, and an unused release tag.
Squad working notes are not included in the release source check or package.

### Project Structure

```
src/
  EWSR_PMR_ModApp.Core/       # Engine (game detection, zip, install, backup)
  EWSR_PMR_ModApp.UI/         # WPF desktop app (PMR CM.exe)
  EWSR_PMR_ModApp.Helper/     # Elevated file-writer (PMR CM.Helper.exe)
tests/
  EWSR_PMR_ModApp.Core.Tests/ # 167 unit tests
docs/
  ARCHITECTURE.md
  MOD_PACKAGING_GUIDE.md
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the full module breakdown.

---

## Contributing

Pull requests welcome. Open an issue first for significant changes. All PRs should keep `dotnet test` green (167 tests).

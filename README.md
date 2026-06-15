# PMR CM — Project Motor Racing Content Manager

**PMR CM** is a Windows mod manager for *Project Motor Racing*. Drop a mod `.zip` onto the app and it installs the files into the right game directories — and re-applies them automatically when a game update reverts your mods.

> **Community tool — not affiliated with or endorsed by the PMR developers.**  
> PMR CM backs up every original file before making any change. You can restore to vanilla at any time.
>
> **Single-player only:** mods change game files and will not work in online multiplayer — those sessions verify the original (vanilla) game files. Use mods for offline / single-player only.

---

## Download

Get the latest release from [GitHub Releases](../../releases) or [Overtake.gg](#).  
Current version: **v1.0.0**

---

## How to Use

1. **Download** `PMR-CM-v1.0.0.zip` from the Releases page.
2. **Extract** the zip anywhere you like (Desktop, Documents, wherever). No installer needed.
3. **Run `PMR CM.exe`**.
   - On first launch, Windows may show **"Windows protected your PC — unrecognised app"**.  
     Click **More info → Run anyway**. This is normal for any unsigned community tool — PMR CM is open source (MIT); you can inspect the code.
4. **First-run consent** — PMR CM will ask once: "PMR CM modifies Project Motor Racing game files…". Click **Continue**.
5. PMR CM will **auto-detect** your PMR installation. If it doesn't find it, an amber banner appears — click **Open Settings** and paste or browse to your PMR `data` folder (usually `C:\Program Files\Project Motor Racing\data`).
6. **Drag a mod `.zip`** onto the drop zone, or click **Browse for .zip…**.
7. PMR CM maps the mod files to the correct game directories, backs up your originals, and installs. A UAC prompt will appear for the write step — this is required because PMR lives in Program Files.
8. The mod appears in **INSTALLED MODS**. Click **Uninstall** to restore originals.

### After a game update

PMR updates may silently overwrite modded files. Click **Re-check & Reapply** (toolbar) to detect and re-apply any reverted mods in one step.

---

## SmartScreen Note

Windows SmartScreen may warn that PMR CM is "unrecognised". This happens with any unsigned application. PMR CM is open-source; you can read every line of code in this repo.

To proceed: click **More info → Run anyway** in the SmartScreen dialog.  
The Helper process (`PMR CM.Helper.exe`) also requests admin (UAC) — this is the component that writes files into the Program Files folder.

---

## How Backups & Restore Work

- Before any file is overwritten, PMR CM saves the original to `%APPDATA%\EWSR_PMR_ModApp\backups\`.
- Clicking **Uninstall** on a mod in the list restores all its original files.
- If something goes wrong, your originals are always in that backups folder.

---

## Making a CM-Compatible Mod Zip

Mod authors: structure your zip so all entries are rooted at `data/` using forward slashes.

```
data/vehicles/physics/my_car.hadron
data/environments/textures/my_texture.dds
data/ui/some_config.xml
```

**Allowed file extensions:** `.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`, `.ini`, `.cfg`, `.bin`, `.lut`, `.json`, `.eval`

Files with other extensions are skipped (not installed). You may include a `README.md` or `CHANGELOG.md` at the zip root — PMR CM will display these as info-only and will not try to install them into the game.

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

### Publish (portable single-file build)

```powershell
# Preview what will be built (dry-run, no changes)
.\publish.ps1

# Actually build to dist/
.\publish.ps1 -DryRun:$false
```

Output: `dist\PMR CM.exe` + `dist\PMR CM.Helper.exe`

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


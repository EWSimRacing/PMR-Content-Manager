# Decisions

Canonical decision ledger for EWSR_PMR_ModApp. Append-only. Scribe merges entries from `decisions/inbox/`.

---

### 2026-05-31T15:41:33-04:00: Stack Choice — .NET 10 + WPF

**By:** Furiosa

**What:** The project will use C# / .NET 10 with WPF for the UI and a separate Core class library for all engine logic.

**Why:**
- Windows-only target → WPF gives native drag-and-drop, file dialogs, and system tray support out of the box.
- .NET's `System.IO.Compression` and async file I/O are excellent for the zip/sync workload.
- Single-file publish produces a clean installer story without bundling a browser (vs Electron).
- C# is strongly typed and well-tooled for a solo/small dev — refactoring is safe, NuGet ecosystem is rich.
- Inspired by AMS2 Content Manager which is also .NET/WPF — proven pattern for this domain.

---

### 2026-05-31T15:59:44-04:00: User directive

**By:** Elliott Williams (via Copilot)

**What:** All game files that mods will change live under `C:\Program Files\Project Motor Racing\data`. The install target root is the game's `data` folder. Open design question raised: how to determine which files in an added mod `.zip` map to which files under `data`.

**Why:** User request — captured for team memory. Constrains GameDetection (locate the `data` root) and the zip→game-path mapping logic in the SyncEngine.

---

### 2026-05-31T15:59:44-04:00: File Mapping Strategy — Hybrid Path-Overlay + Filename-Index Fallback

**By:** Furiosa

**What:** Mod zip files are mapped to the game's `data` root using a hybrid strategy: (1) path-overlay when the zip preserves folder structure (strip `data/` prefix if present, else overlay directly if top-level dirs match known children of `data`); (2) filename-index fallback for flat zips (match by name against an index of all files under `data`, surface ambiguities to user). An optional `modinfo.json` inside the zip is treated as authoritative when present. The install manifest records per-file: relativeTargetPath, sourcePathInZip, mappingMethod, originalFileHash, installedFileHash, isNewFile — making installs deterministic and reversible. GameDetection must validate write access to the `data` root under Program Files and prompt for elevation if needed.

**Why:**
- Path-overlay is the 90% case for racing sim mods — authors typically mirror the game's folder tree. It's fast, deterministic, and requires no indexing.
- Filename-index covers the remaining 10% (flat zips from lazy packagers) without requiring user intervention in the happy path.
- Surfacing ambiguous matches to the user avoids silent mis-installation — correctness over convenience.
- The optional `modinfo.json` gives advanced mod authors a zero-ambiguity path without imposing burden on casual modders.
- Recording `mappingMethod` in the manifest means we can audit and debug any install after the fact.
- Program Files write-access check prevents the #1 silent failure mode on modern Windows.

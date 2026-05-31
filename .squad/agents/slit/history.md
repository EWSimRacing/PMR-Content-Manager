# History: Slit — UI Dev

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- My domain: drop-zone UI, installed mods list, install status, settings screen.
- User: Elliott Williams.

## Learnings

### 2026-05-31: Stack Decision & Handoff (via Furiosa)
- **Stack:** C# / .NET 10 + WPF. Solution: `EWSR_PMR_ModApp.slnx` at repo root.
- **Your module:** `src/EWSR_PMR_ModApp.UI/` (WPF shell, depends on Core).
- **Next:** Build drag-and-drop zone, mod list view, settings panel, status/log display. Integrate with Core APIs as they stabilize.
- **See:** `docs/ARCHITECTURE.md` for full module spec and Core API contracts.
- **Build:** `dotnet build` from repo root; run with `dotnet run --project src/EWSR_PMR_ModApp.UI`.

### 2026-05-31: File Mapping Strategy — UI Handoff (via Scribe)
- **Install target:** All mod-affected files under `C:\Program Files\Project Motor Racing\data` (user constraint).
- **UI responsibilities:**
  1. **Ambiguity resolution:** When filename-index fallback produces multiple matches, surface them to user with options to pick correct file(s).
  2. **Elevation prompt:** If SyncEngine detects missing write access to `data` root, UI must prompt for elevation (Windows UAC dialog).
  3. **Manifest display:** Show per-file mapping status: relativeTargetPath, mappingMethod (overlay/filename-index/modinfo), installed file hash (for verification).
- **Nux side:** SyncEngine will call Core APIs to surface ambiguities; UI catches and displays them.
- **See:** `docs/ARCHITECTURE.md` "File Mapping Strategy" section for full details.

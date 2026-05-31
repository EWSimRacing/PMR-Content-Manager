# History: Furiosa — Lead

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- Goal: drop a mod .zip → app installs files into correct game locations; re-applies mods reverted by game updates. Inspired by AMS2 Content Manager.
- Stack: TBD (my call — candidates: .NET/WPF, Electron, Python).
- User: Elliott Williams.

## Learnings

### 2026-05-31: Stack Decision & Scaffold
- **Stack chosen:** C# / .NET 10 + WPF. Rationale: native Windows file I/O, zip support, drag-and-drop, single-file publish, proven pattern (AMS2 Content Manager is .NET/WPF).
- **Solution layout:**
  - `EWSR_PMR_ModApp.sln` at repo root
  - `src/EWSR_PMR_ModApp.Core/` — engine library (net10.0-windows), no UI deps
  - `src/EWSR_PMR_ModApp.UI/` — WPF app shell, references Core
- **Core modules (Nux owns):** SyncEngine, ZipHandling, GameDetection, Manifest, Backup
- **UI surface (Slit owns):** Drag-and-drop zone, mod list, settings, status/log panel
- **Key files:** `docs/ARCHITECTURE.md` has full module breakdown and risk mitigations
- **Build:** `dotnet build` from repo root; run with `dotnet run --project src/EWSR_PMR_ModApp.UI`

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

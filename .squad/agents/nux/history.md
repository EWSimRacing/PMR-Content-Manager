# History: Nux — Core Dev

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- My domain: file sync engine, zip extraction/validation, game path detection, update-revert recovery, backups.
- User: Elliott Williams.

## Learnings

### 2026-05-31: Stack Decision & Handoff (via Furiosa)
- **Stack:** C# / .NET 10 + WPF. Solution: `EWSR_PMR_ModApp.slnx` at repo root.
- **Your module:** `src/EWSR_PMR_ModApp.Core/` (net10.0-windows, no UI deps).
- **Next:** Implement GameDetection and Manifest first (alphabetically and dependency-wise), then ZipHandling, SyncEngine, Backup.
- **See:** `docs/ARCHITECTURE.md` for full module spec and risk mitigations.
- **Build:** `dotnet build` from repo root; run with `dotnet run --project src/EWSR_PMR_ModApp.UI`.

# Decision: IElevatedWriter is not injected into SyncEngine

**Date:** 2026-05-31T20:25:00-04:00
**Author:** Nux
**Status:** Implemented

---

## Furiosa's suggestion

Furiosa's design (§6.2) noted:
> "The UI injects IElevatedWriter into SyncEngine (or a new InstallOrchestrator)."

## What was implemented instead

`IElevatedWriter` is **not** injected into `SyncEngine`. Instead:

- `SyncEngine` retains `IBackupService` + `IFileSystem` as its write path (unchanged for in-process execution).
- `SyncEngine.ExecuteInstallAsync` / `ExecuteUninstallAsync` / `ExecuteReapplyAsync` perform writes via the existing `_backupService` / `_fs` abstractions, which are the correct interfaces for unit-testing.
- `IElevatedWriter` (`InProcessWriter` / `HelperProcessWriter`) is a **separate, parallel abstraction** that the UI/Slit wires up in its orchestration layer — **not** inside `SyncEngine`.

## Rationale

1. **Test compatibility** — All 112 existing unit tests construct `SyncEngine` with `FakeFileSystem` and `NoOpBackupService`. Injecting `IElevatedWriter` would require either a new fake or a nullable default (which would silently fall through in tests). The existing IFileSystem path is already correct for this testing pattern.
2. **Separation of concerns** — `SyncEngine` is an orchestrator for prepare/execute logic. Whether the execute step is in-process or out-of-process is a deployment/DI decision owned by the UI container, not the engine.
3. **Thin wrappers still work** — The `InstallAsync`, `UninstallAsync`, and `ReapplyRevertedModsAsync` thin wrappers call `ExecuteXxxAsync` (in-process path). Slit's Phase 2 can call `PrepareXxxAsync` + `IElevatedWriter.ExecuteAsync` without touching `SyncEngine` internals.
4. **WritePlanExecutor bridges the gap** — The shared executor (`WritePlanExecutor.Execute`) is called by BOTH `InProcessWriter` AND the Helper process, so there is no logic duplication despite `IElevatedWriter` not being in `SyncEngine`.

## Impact on Slit (Phase 2)

Slit's orchestration layer does:
```csharp
var plan = await _syncEngine.PrepareInstallAsync(...);
try {
    var request = BuildWritePlanRequest(plan);   // map InstallPlan → WritePlanRequest
    var result  = await _elevatedWriter.ExecuteAsync(request, ct);
    if (!result.Success) { /* show error */ return; }
    // build InstalledFileEntry list from plan.MappedFiles (hashes computed post-write)
    await _manifestStore.AddOrUpdateModAsync(modEntry, ct);
} finally {
    _syncEngine.CleanupInstallPlan(plan);
}
```

The manifest update (AppData, no elevation needed) stays in the UI — consistent with design §6.3.

# Skill: Writing SyncEngine Install-Path Tests

## Context

Tests for `SyncEngine.InstallAsync` require a full pipeline: `IZipService`, `IMappingResolver`,
`IManifestStore`, `IBackupService`, `IFileSystem`, and `IFileHasher`.  Use the real `MappingResolver`
so the mapping logic is actually exercised; swap everything else with test doubles.

## Required test doubles

| Double | Purpose |
|---|---|
| `StubZipService` | Returns a preset `ZipStagingResult` (entries + staging dir + zip hash) |
| `FakeFileSystem` | In-memory I/O; seed game data files with `AddFile` |
| `FakeFileHasher` | Returns `defaultHash` for any path; override per-path if needed |
| `NoOpBackupService` | Silent no-op for backup/restore |
| `ManifestStore` | Use real impl with a fake manifest path and `FakeFileSystem` |

## Critical: force filename-index strategy for Unmatched/Collision tests

`MappingResolver.TryPathOverlay` fires when **every** top-level zip directory either:
- passes `Directory.Exists(Path.Combine(dataRoot, dir))` (real disk call!), OR
- appears as a prefix in the data file index.

Path-overlay maps ALL entries to `Mapped` — even brand-new files — and never populates
`Unmatched` or `Collisions`.

**To reliably exercise Unmatched/Collision:** use zip entry names with no `/`
(flat zip, e.g. `"brand_new_skin.dds"`).  `GetTopLevelDir` returns `null` for all of them,
so `topLevelDirs.Count == 0` and path-overlay is skipped unconditionally.

For **collision** tests (which need two entries with different paths but the same filename):
use unknown prefixes (`"skin_a/livery.dds"`, `"skin_b/livery.dds"`) that are absent from both
the data index AND very unlikely to exist on disk.  Do NOT use game-known prefixes like
`vehicles/` — those trigger path-overlay.

## Pattern

```csharp
// Flat zip → filename-index forced
var staged = MakeStaging("livery.dds", "brand_new_skin.dds");
var (engine, _) = BuildEngine(staged, ["vehicles/car_a/livery.dds"]);
var result = await engine.InstallAsync("mod.zip", DataRoot, "TestMod", NoAmbiguous);
// "livery.dds" → mapped; "brand_new_skin.dds" → Unmatched → warning

// Collision via unknown subdirs → filename-index forced
var staged = MakeStaging("engine.wav", "skin_a/livery.dds", "skin_b/livery.dds");
var (engine, _) = BuildEngine(staged, ["sounds/engine.wav", "vehicles/car_x/livery.dds"]);
// "skin_a/livery.dds" + "skin_b/livery.dds" both match vehicles/car_x/livery.dds → Collision
```

## Warning format (as of 2026-05-31)

```
// Unmatched — one warning per file:
$"Skipped (no match in data): {entry.FullNameInZip}"

// Collision — one warning per collision group:
$"Path collision: '{relativeTargetPath}' — {N} source(s): {csv of FullNameInZip} — none installed."
```

## Files

- `src/EWSR_PMR_ModApp.Core/SyncEngine/SyncEngine.cs` — warning construction
- `tests/EWSR_PMR_ModApp.Core.Tests/SyncEngine/InstallWarningsTests.cs` — reference tests
- `tests/EWSR_PMR_ModApp.Core.Tests/TestDoubles/StubZipService.cs` — zip service stub

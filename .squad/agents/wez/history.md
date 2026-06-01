# History: Wez — Tester

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- My domain: tests, edge cases (bad zips, missing game dir, conflicts, permissions, post-update re-apply).
- User: Elliott Williams.

## Learnings

### Session: xUnit test suite — Mapping, GameDetection, Manifest, SyncEngine (re-apply)

**Context pivot — reconcile against real Core, not staging stubs**
Nux wrote the Core implementation in parallel while the test suite was being authored against staging interfaces. After discovering the real classes, the approach shifted to use them directly: `MappingResolver`, `GameLocator`, `ManifestStore` from Core; `SyncEngineStub` retained only where `HashHelper.ComputeFileHash` uses static disk I/O that can't be injected.

**FakeFileSystem is the enabler**
`GameLocator` and `ManifestStore` both accept `IFileSystem`. A single `FakeFileSystem` (in-memory `Dictionary` of dirs/files, supporting `EnumerateFiles(AllDirectories)`, `CanWriteDirectory`) is the only test double needed for those two components. Key setup detail: `ValidateDataRoot` requires both the root dir AND at least one of `{vehicles, tracks, configs, sounds}` sub-dir to be present in the fake FS.

**MappingResolver — no IFileSystem, no injection**
`MappingResolver` uses `static Directory.Exists` inside `TryPathOverlay`; the fallback to `dataFileIndex.Any(f => f.StartsWith(dir+"/"))` is what tests exploit. Tests pass a non-existent `dataRoot` so that the static check always returns false and the fallback index path runs reliably.

**ManifestStore test constructor**
`ManifestStore(IFileSystem fs, string overrideManifestPath)` — pass a `FakeFileSystem` and a fake path like `"fake://manifest.json"` so the store writes/reads JSON into the fake FS without touching disk.

**Open QA finding — collision detection missing in MappingResolver**
If two zip entries resolve to the same `RelativeTargetPath`, both appear in `Mapped` without any warning. Test `TwoZipEntries_BothResolvingToSameTarget_CollisionReported_NeitherPlaced` is skipped with an explicit `⚠️` message. Nux needs to add a collision-detection pass and a `Collisions` bucket to `MappingPlan`.

**LocationSource enum has no `Found` value**
The enum is `{UserConfigured, DefaultPath, SteamDetected, NotFound}`. Don't assert against `LocationSource.Found` — use `result.Found` (bool) instead.

**`using Xunit;` is not implicit**
Even with `<ImplicitUsings>enable</ImplicitUsings>` in the csproj, `using Xunit;` is NOT auto-injected in net10.0-windows test projects. Every test file needs it explicitly.

### Session: Test consolidation — single canonical project at tests/ (2026-05-31)

**Final test project location**: `tests/EWSR_PMR_ModApp.Core.Tests` — the single canonical test project, already registered in `EWSR_PMR_ModApp.slnx` under the `/tests/` folder.

**Structure**:
```
tests/EWSR_PMR_ModApp.Core.Tests/
  MappingResolverTests.cs         ← merged (Nux's 6 smoke cases fully covered)
  GameDetection/GameLocatorTests.cs
  Manifest/ManifestStoreTests.cs
  Mapping/.gitkeep
  SyncEngine/ReApplyTests.cs      ← real SyncEngine + FakeFileHasher (SyncEngineStub retired)
  TestDoubles/
    FakeFileSystem.cs
    FakeFileHasher.cs              ← new — injectable IFileHasher for SyncEngine tests
    NoOpZipService.cs              ← new
    NoOpBackupService.cs           ← new
```

**Total test count**: 56 (0 failed, 0 skipped). `dotnet test` at repo root runs the full suite.

**Collision test un-skipped**: `TwoZipEntries_BothResolvingToSameTarget_CollisionReported_NeitherPlaced` now passes against the real `MappingResolver.ApplyCollisionDetection` pass. Asserts both `plan.Mapped` is empty and `plan.Collisions` has 1 group with 2 entries.

**Re-apply tests now use real SyncEngine + fake IFileHasher**: `FakeFileHasher` implements `IFileHasher` with a path→hash dictionary. Tests call `SyncEngine.CheckForRevertedModsAsync` and `ReapplyRevertedModsAsync` directly, verifying `ModUpdateStatus.State` and `ReapplyResult.FilesReapplied`. `SyncEngineStub` and the staging `Contracts/` models have been deleted along with the old `src/EWSR_PMR_ModApp.Tests` project.

**Deleted**: `src/EWSR_PMR_ModApp.Tests` (entire directory) — confirmed not referenced in slnx.

### Session: FileClassifier unit tests (2026-05-31)

**Test file location**: `tests/EWSR_PMR_ModApp.Core.Tests/ZipHandling/FileClassifierTests.cs`

**Test count**: 52 test cases (51 passing, 1 skipped for production bug). Total suite after addition: 112 (111 passed, 1 skipped).

**FileClassifier is a pure static class — no dependencies needed.**
`FileClassifier.Classify(ZipEntryInfo, ModInfo?)` has a convenience overload that discards `out string? reason`. Tests use that overload exclusively; no `out` variable plumbing needed.

**ZipEntryInfo requires `FullNameInZip` and `StagedFilePath` (required inits).**
`FileName` is a computed property (`Path.GetFileName(FullNameInZip)`) — do not set it; derive your fixture from `FullNameInZip` only.

**`[Theory]` + `[InlineData]` is the right tool for extension/path matrix tests.**
FileClassifier's decisions are almost entirely driven by extension and path prefix — a single `[Theory]` with `[InlineData]` covering the interesting variants is cleaner than separate `[Fact]` methods per value.

**Production bug found — FileClassifier does not check `modInfo.Files`.**
Files explicitly listed in `modInfo.Files` (the zip→target mapping dictionary) are NOT classified as Install by `FileClassifier`. The method checks `modInfo.SkipFiles` and `modInfo.DisplayFiles` but not `modInfo.Files`. A file with an unrecognized extension listed in `modInfo.Files` falls through to `NoPathMatch`. Flagged to Nux via `.squad/decisions/inbox/wez-classifier-tests.md`.
Test `ModinfoFiles_ExplicitEntry_IsInstall_EvenIfExtensionWouldBeNoPathMatch` is skipped with an explicit ⚠️ message pending the production fix.

**`DATA/tracks/circuit.xml` (uppercase DATA/) is correctly classified as Install.**
`IsInsideDataPath` uses `StartsWith("data/", OrdinalIgnoreCase)` — the case-insensitive check works for both the path prefix and the extension.

## Orchestration (2026-05-31)

**Scribe merged and logged FileClassifier session:**
- 52 test cases delivered (51 passing, 1 skipped)
- Total suite: 112 tests (111 passed, 1 skipped, 0 failed)
- Decision inbox merged to decisions.md
- Production bug logged: `modInfo.Files` missing from FileClassifier.Classify check


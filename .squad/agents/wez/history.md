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

### Session: Phase 3 elevation-broker tests — W1–W4 (2026-05-31)

**New test count: 52 added. Total suite: 164 (164 passed, 0 failed, 0 skipped).**

**Files created:**
```
tests/EWSR_PMR_ModApp.Core.Tests/Elevation/
  PathValidatorTests.cs    (W1 — 23 tests: 5 Theory + 18 Fact)
  DtoSerializationTests.cs (W2 — 10 tests: 7 Fact + 3 Theory)
  WritePlanExecutorTests.cs (W3 — 11 tests: real System.IO + InProcessWriter security)
tests/EWSR_PMR_ModApp.Core.Tests/SyncEngine/
  SyncEngineSplitTests.cs  (W4 — 8 tests: Prepare→Execute split via FakeFileSystem)
```

**W1 — PathValidator (security-critical):**
`IsUnderDataRoot` tests cover: classic `..\..\Windows\System32\evil.dll` traversal, rooted absolute paths (`C:\Windows\`), sneaky traversal through subdirs (`vehicles\..\..\..`), forward-slash traversal, empty path, single-dot (resolves to root itself → false), trailing separator on dataRoot (edge case), case-insensitivity, and the sibling-prefix bypass (`data` vs `data_evil`). `IsAllowedSource` tests cover: source under staging subdir (accept), source directly in root (accept), Windows/System32 (reject), Desktop (reject), traversal escape (reject), empty string (reject), sibling-name prefix (reject), case-insensitivity (accept). All pure string manipulation — no real filesystem.

**W2 — DTO serialization round-trip:**
Uses `WriteIndented = true` options (matching Helper.Program). All nullable `IReadOnlyList<T>?` fields survive as null (not silently converted to empty lists). Enum `WritePlanOperation` round-trips as numeric value for all 3 cases. `WriteResult.Errors` (non-nullable default `[]`) survives as empty list. Covers `FileCopySpec` and `FileOperationError` standalone round-trips.

**W3 — WritePlanExecutor integration (real System.IO):**
`Directory.CreateTempSubdirectory` used for DataRoot and source dirs; cleaned up via `IDisposable TempScope`. Tests that touch `AppPaths.BackupDirForMod(modId)` (Install with backup, Uninstall) use unique GUID modIds and register the backup dir for cleanup. Covers: Install (no backup), Install (with backup — original backed up, mod file written), backup skip for new files (count=0), Uninstall (backup restored + new file deleted), Reapply (payload copied to DataRoot). InProcessWriter security tests cover: traversal target (`..\..\Windows\`), rooted absolute target (`C:\Windows\evil.dll`), source outside AppData, traversal in FilesToBackup, empty DataRoot, empty ModId — all rejected with `WriteResult.Success=false` before WritePlanExecutor is called.

**W4 — SyncEngine split API:**
All tests use FakeFileSystem + FakeFileHasher (no real disk I/O). Covers: `PrepareInstallAsync` → `InstallPlan` shape (FilesToCopy, FilesToBackup, MappedFiles, Warnings), warnings for unmatched entries in plan, `ExecuteInstallAsync` → file written to FakeFileSystem + modId consistent with manifest, `PrepareUninstallAsync` → `UninstallPlan` shape (BackedUpFileCount, NewFilesToDelete), `ExecuteUninstallAsync` → new file deleted from FakeFileSystem, `PrepareReapplyAsync` → `ReapplyPlan` with correct FilesToCopy for reverted mod, `PrepareReapplyAsync` + `ExecuteReapplyAsync` round-trip → file copied back to FakeFileSystem.

**Nux code quality — no bugs found:** All Phase 1 code passed validation. One semantic note for documentation: `WritePlanExecutor.Execute` uses the `filesBackedUp` variable for the Uninstall operation to hold the restore count, so `WriteResult.FilesBackedUp` means "files restored" in the Uninstall context. The docs say "backed up before overwriting" which is only accurate for Install. No failing test written (behavior is correct, not a security/correctness risk); documented in W3 test comment for future reference.

**Gaps for manual testing (cannot be automated without real elevation):**
- `HelperProcessWriter.ExecuteAsync` end-to-end (spawns Helper.exe with `runas`/UAC prompt)
- Helper.exe request-file-location whitelist (real `%TEMP%` vs arbitrary path)
- Full round-trip from UI → Helper.exe → result file read-back with WriteIndented JSON
- UAC cancel path (`Win32Exception` with `NativeErrorCode=1223`) in `HelperProcessWriter`


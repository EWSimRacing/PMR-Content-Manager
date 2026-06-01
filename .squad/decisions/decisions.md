# Decisions Log

## 2026-05-31

### FileClassifier test suite complete + production bug found

**By:** Wez

**What:**
Created `tests/EWSR_PMR_ModApp.Core.Tests/ZipHandling/FileClassifierTests.cs` — 52 test cases
covering `FileClassifier.Classify(ZipEntryInfo, ModInfo?)`.

**Test count:** 52 cases (51 passing, 1 skipped for production bug).
**Total suite:** 112 tests (111 passed, 1 skipped, 0 failed).

**Coverage:**

| Area | Test count |
|------|-----------|
| UnsafeFile (`.exe`, `.dll`, `.ps1`, `.bat`, upper-case variants) | 8 |
| DisplayOnly — docs (`.md`, `.txt`, `.pdf` at root) | 3 |
| DisplayOnly — images at root | 3 |
| DisplayOnly — images in `preview/` and `images/` folders | 2 |
| Install — images inside `data/` | 3 |
| Install — core game formats (`.xml`, `.hadron`, `.tweakers`, `.i3d`, `.dds`) under `data/` | 5 |
| NoPathMatch — game formats outside `data/` | 3 |
| MetaFile — `modinfo.json` at zip root | 1 |
| `modinfo.json` in subfolder → Install (not MetaFile) | 1 |
| NoPathMatch — packaging artifact extensions (`.log`, `.bak`, `.tmp`) | 3 |
| NoPathMatch — OS artifact filenames (`thumbs.db`, `.DS_Store`) | 2 |
| NoPathMatch — `__MACOSX/` prefix paths | 2 |
| NoPathMatch — nested archives (`.zip`, `.rar`, `.7z`) | 3 |
| modinfo.json `DisplayFiles` override → DisplayOnly | 1 |
| modinfo.json `SkipFiles` glob match → UserExcluded | 1 |
| modinfo.json `SkipFiles` non-match falls through to normal rules | 1 |
| modinfo.json `SkipFiles` evaluated before `DisplayFiles` | 1 |
| modinfo.json `Files` explicit mapping → Install *(SKIPPED — prod bug)* | 1 |
| Edge: `.md` inside `data/` → NoPathMatch | 1 |
| Edge: `.json` inside `data/` → Install | 1 |
| Edge: `.json` at root → NoPathMatch | 1 |
| Case insensitivity — extensions and `data/` path prefix | 6 |

**Production bug flagged to Nux:**
`FileClassifier.Classify` checks `modInfo.SkipFiles` and `modInfo.DisplayFiles` but does **not** check `modInfo.Files`. Files listed in `modInfo.Files` with an unrecognized extension fall through to `NoPathMatch` instead of `Install`. The skipped test `ModinfoFiles_ExplicitEntry_IsInstall_EvenIfExtensionWouldBeNoPathMatch` documents the expected behavior. Nux should add a check after the `DisplayFiles` block:

```csharp
// After DisplayFiles check, before UnsafeExtensions:
if (modInfo?.Files is { Count: > 0 } && modInfo.Files.ContainsKey(zipPath))
{
    reason = null;
    return SkipCategory.Install;
}
```

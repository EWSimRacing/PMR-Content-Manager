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

### 2026-05-31T20:10:18-04:00: Drag-Drop Root Cause Confirmed — RESOLVED

**By:** Slit (UI Dev)

**What:** The persistent drag-drop failure (⊘ cursor, Drop never firing) is fully resolved. Root cause was confirmed by runtime log data; fix is `IsHitTestVisible="False"` on decorative TextBlocks in the drop zone. Diagnostic logging gated behind `#if DEBUG`. Prior UIPI/COM theory was a red herring.

**Why / Details:**

#### Root Cause
Decorative `TextBlock` elements inside the drop zone StackPanel — especially "Drop a mod .zip here" — were hit-testable by default. When users hovered over the center of the drop zone (the natural drop point), the cursor landed on a TextBlock. Because TextBlocks had no `AllowDrop` and no drag handlers, WPF's OLE layer found no valid drop target and showed ⊘. The `Border` behind them had all the right wiring but was never reached by hit-testing.

The runtime log (`%TEMP%\ewsr_dragdrop.log`) proved this: a `Drop` event fired successfully while running **elevated** (`IsElevated=True`), launching a real install. All three `ChangeWindowMessageFilterEx` calls returned `ok=True`. OLE drag-drop from Explorer works fine even at High integrity — UIPI was never the blocker.

#### Fix
`IsHitTestVisible="False"` on the four decorative TextBlocks inside `DropZoneBorder`'s StackPanel. This passes drag hit-tests through to the Border behind them.

#### Status
**RESOLVED** — works both elevated (Restart as Administrator) and non-elevated.

#### Diagnostics
All diagnostic logging gated behind `#if DEBUG` in `MainWindow.xaml.cs`:
- `_logPath`, `_dragOverLogCount`, `Log()`, `DumpDragData()` fields/methods — `#if DEBUG` block
- All `Log()` calls in event handlers — `#if DEBUG` guarded
- `OnSourceInitialized` elevation check + per-call logging — `#if DEBUG`; Release path is three clean `ChangeWindowMessageFilterEx` calls
- `System.IO` and `System.Security.Principal` usings — `#if DEBUG`
- `%TEMP%\ewsr_dragdrop.log` stale file deleted

`ChangeWindowMessageFilterEx` calls retained unconditionally (correct for elevated path; harmless otherwise).

#### What Was NOT the Issue
- UIPI blocking the OLE COM channel (theory was technically sound but did not match the actual failure)
- Missing `AllowDrop` on `DropZoneBorder` (fixed in prior round, correct, but not the primary cause)
- Missing drag handlers on `DropZoneBorder` (fixed in prior round, correct, but not the primary cause)
- `ChangeWindowMessageFilterEx` not being called (it was called correctly from round 2 onward)

#### Files Changed
| File | Change |
|---|---|
| `src/EWSR_PMR_ModApp.UI/MainWindow.xaml.cs` | Diagnostic logging gated behind `#if DEBUG`; Release path cleaned |
| `src/EWSR_PMR_ModApp.UI/MainWindow.xaml` | `IsHitTestVisible="False"` on decorative TextBlocks — **the fix; untouched here** |
| `.squad/agents/slit/history.md` | Confirmed root cause + resolution appended |
| `.squad/skills/wpf-drag-drop/SKILL.md` | #1 gotcha added; confidence bumped |
| `.squad/decisions/inbox/slit-dragdrop-resolved.md` | This file |

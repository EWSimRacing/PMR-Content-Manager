# Session Log: Drag-Drop Resolved

**Timestamp:** 2026-06-01T00:10:18Z

**Category:** Bug Resolution

## Summary

Persistent drag-drop failure (⊘ cursor, Drop never firing) fully resolved. Root cause: decorative TextBlocks in the drop zone were hit-testable by default and intercepted drag-hit-tests before reaching the Border with handlers. Fix: `IsHitTestVisible="False"` on those TextBlocks.

## Details

- **Bug:** Users saw ⊘ cursor over drop zone; drops were rejected even with correct AllowDrop and handlers on the Border.
- **Root cause:** TextBlock elements ("Drop a mod .zip here" label, decorative borders) inside DropZoneBorder had no `IsHitTestVisible="False"`. When cursor hovered over their center, WPF hit-tested the TextBlock first, found no handlers, returned ⊘.
- **Evidence:** Runtime log (`%TEMP%\ewsr_dragdrop.log`) showed Drop event firing successfully while elevated, with ChangeWindowMessageFilterEx returning `ok=True`. OLE drag-drop works fine at High integrity — UIPI was never the blocker.
- **Fix:** Set `IsHitTestVisible="False"` on four decorative TextBlocks.
- **Result:** ⊘ cursor gone; Drop fires correctly; works elevated and non-elevated.

## Code Changes

**File:** `src/EWSR_PMR_ModApp.UI/MainWindow.xaml.cs`

- All diagnostic logging gated behind `#if DEBUG`
- Release path: three clean `ChangeWindowMessageFilterEx` calls, no debug spew
- `_logPath`, `_dragOverLogCount`, `Log()`, `DumpDragData()` → `#if DEBUG`
- All `Log()` calls in handlers → `#if DEBUG`
- `OnSourceInitialized` elevation check + logging → `#if DEBUG`; Release path uncluttered
- Stale `%TEMP%\ewsr_dragdrop.log` deleted

## QA

- 112 tests pass (0 failures, 0 new skips)
- Drag-drop tested elevated and non-elevated — both work
- Release build clean

---

**By:** Scribe

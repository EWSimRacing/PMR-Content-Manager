# History: Scribe

## Seed
- Project: EWSR_PMR_ModApp — a mod manager for Project Motor Racing.
- Role: memory, decisions merge, logs, commits. User: Elliott Williams.

## Learnings

### 2026-05-31T18:43:00-04:00: Drag-Drop UIPI Fix — Documentation & Commit

**Session work:**
1. Merged Slit's decision file (slit-dragdrop-fix.md) into canonical .squad/decisions.md.
2. Documented key learning: When a WPF app runs elevated (as Administrator), Windows User Interface Privilege Isolation (UIPI) silently blocks drag-drop messages (`WM_DROPFILES`, `WM_COPYDATA`, `WM_COPYGLOBALDATA`) sent from non-elevated processes like Explorer. Fix: P/Invoke `ChangeWindowMessageFilterEx` with `MSGFLT_ALLOW` called in `OnSourceInitialized` to open these message channels for the window's HWND. See skill file `.squad/skills/wpf-drag-drop/SKILL.md` for complete checklist and pitfalls.
3. Deleted slit-dragdrop-fix.md from inbox after merge.
4. Committed UI files changed by Slit: `MainWindow.xaml`, `MainWindow.xaml.cs`, skill file, and updated .squad/ documentation.

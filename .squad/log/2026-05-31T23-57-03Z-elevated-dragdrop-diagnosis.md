# Session Log: Elevated Drag-Drop Diagnosis

**Timestamp:** 2026-05-31T23:57:03Z (UTC)  
**Agent:** Slit (UI Dev)  
**Session Topic:** WPF drag-drop elevated failure root cause & mitigation

## Diagnostic Summary

**Problem:** Drag-and-drop of .zip onto app's drop zone shows ⊘ cursor when app is elevated; works fine when non-elevated.

**Root Cause:** UIPI (User Interface Privilege Isolation) blocks OLE COM channel between elevated app (High IL) and non-elevated drag source (Explorer, Medium IL). `ChangeWindowMessageFilterEx` fixes legacy `WM_DROPFILES` only, not WPF's OLE-based path.

**Decision:** Use non-elevated UI (asInvoker) for normal flow; delegate Program Files writes to minimal elevated helper process spawned on-demand. This is the Windows installer standard (MSI, Inno Setup).

## Files Modified

- `MainWindow.xaml.cs`: Added runtime diagnostics logger (`%TEMP%\ewsr_dragdrop.log`), elevation status check, COM handler monitoring
- `MainWindow.xaml`: Hardened drop zone hit-test

## Next Steps

1. Elliott captures diagnostic log data (non-elevated AND elevated runs)
2. Log data confirms/refutes OLE channel hypothesis
3. If confirmed: implement `EWSR_PMR_ModApp.Installer.exe` helper (named-pipe IPC, file ops)

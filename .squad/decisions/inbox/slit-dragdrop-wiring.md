# Decision: Wire drag-drop handlers to DropZoneBorder directly

**Date:** 2026-05-31T19:19:13-04:00
**By:** Slit
**Requested by:** Elliott Williams

## What

Added `DragEnter="Window_DragEnter"`, `DragOver="Window_DragOver"`, `DragLeave="Window_DragLeave"`, `Drop="Window_Drop"` directly to `DropZoneBorder` in `MainWindow.xaml`. Previously these handlers were only wired to the `Window` element; `DropZoneBorder` had `AllowDrop="True"` but no handlers.

No code-behind changes. The existing handler methods are reused exactly as written.

## Why

WPF's OLE drag-drop system targets the **innermost `AllowDrop="True"` element** found via hit-test — here, `DropZoneBorder`. When a drag occurred over the border, WPF routed `DragOver` to `DropZoneBorder` first. Because no handler was attached to `DropZoneBorder`, nothing set `e.Effects = DragDropEffects.Copy`. WPF reported no valid drop effect to the OLE layer, showing the ⊘ cursor and suppressing the `Drop` event entirely — regardless of the Window-level handlers that would have received the bubbled event.

This was introduced (or made worse) by commit ba4c336, which added `AllowDrop="True"` to `DropZoneBorder` without also attaching the handlers, making DropZoneBorder the designated OLE target with no way to confirm the drop.

**Rule captured:** Drag event handlers must be wired to the same element that has `AllowDrop="True"` (i.e., the innermost AllowDrop hit-target), not only to a distant ancestor. Relying on bubbling to a Window ancestor is unreliable for OLE drag-drop effect confirmation.

The UIPI `ChangeWindowMessageFilterEx` fix from ba4c336 is retained — it remains necessary for the elevated/Restart-as-Administrator scenario.

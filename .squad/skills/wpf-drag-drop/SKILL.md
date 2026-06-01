# WPF Drag-and-Drop — Checklist and Pitfalls

## The Four Requirements for a Working Drop Target

### 1. `AllowDrop="True"` on the exact element under the cursor

```xml
<!-- WRONG — AllowDrop only on Window; child elements may not receive events -->
<Window AllowDrop="True" Drop="Window_Drop" DragOver="Window_DragOver">
    <Border x:Name="DropZone">...</Border>  <!-- ← no AllowDrop; may block drops -->
</Window>

<!-- CORRECT — AllowDrop also on the drop-target border itself -->
<Window AllowDrop="True" Drop="Window_Drop" DragOver="Window_DragOver">
    <Border x:Name="DropZone" AllowDrop="True">...</Border>
</Window>
```

A parent having `AllowDrop="True"` is not sufficient. The element actually under the cursor needs it. In WPF, drag events are routed events and do bubble, but having `AllowDrop` on the specific visual target avoids edge cases.

### 2. Non-null `Background` on the drop target (hit-testability)

If the drop-target element has `Background="{x:Null}"` (or no Background at all), it is **not hit-testable** — the cursor passes through it to whatever is behind it. Drag events will never fire on it.

```xml
<!-- WRONG — transparent Background means the element isn't in the hit-test path -->
<Border AllowDrop="True">...</Border>

<!-- CORRECT — even a transparent brush makes it hit-testable -->
<Border AllowDrop="True" Background="Transparent">...</Border>
<!-- or any real brush from your resource dictionary -->
<Border AllowDrop="True" Background="{StaticResource SomeBrush}">...</Border>
```

### 3. `DragOver`/`DragEnter` must set `Effects` and `Handled`

```csharp
private void DropZone_DragOver(object sender, DragEventArgs e)
{
    // MUST check and MUST set Effects — otherwise Windows shows the no-drop cursor
    // and rejects the drop even if you handle the Drop event.
    e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
        ? DragDropEffects.Copy
        : DragDropEffects.None;
    e.Handled = true;   // prevent further routing / default handling
}
```

Failing to set `e.Effects = Copy` in `DragOver` makes Windows show the ⊘ cursor and silently reject the drop — `Drop` never fires.

### 4. `Drop` handler reads `DataFormats.FileDrop`

```csharp
private void DropZone_Drop(object sender, DragEventArgs e)
{
    if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
    var files = (string[])e.Data.GetData(DataFormats.FileDrop);
    var zips  = files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    // route to the same install method the Browse button uses
    _ = _vm.InstallZipsAsync(zips);
    e.Handled = true;
}
```

---

## ⚠️ Handlers-Declared-But-Not-Wired — Silent Drop Failure (EWSR_PMR_ModApp, 2026-05-31)

**This is the second most common WPF drag-drop trap and produces zero error output.**

### What happens

You add `AllowDrop="True"` to your drop-target element (e.g. `DropZoneBorder`) and the handler methods exist in code-behind — but the XAML wires the handlers (`DragOver=`, `Drop=`, etc.) only to the **Window**, not to the element itself:

```xml
<!-- BROKEN: handlers only on Window, not on the AllowDrop element -->
<Window AllowDrop="True" DragOver="Handler" Drop="Handler">
    <Border AllowDrop="True">  <!-- ← no DragOver/Drop here! -->
        ...
    </Border>
</Window>
```

Result:
- `DragOver` is never confirmed with `e.Effects = Copy` for the OLE layer
- ⊘ cursor appears the entire time
- `Drop` never fires
- No errors, no exceptions — complete silence

### Why

WPF's OLE drag-drop system targets the **innermost `AllowDrop="True"` element** in the hit-test path — here, the `Border`. WPF fires `DragOver` routed to that element. Since no handler is attached to the Border, nothing sets `e.Effects = DragDropEffects.Copy`. While the event may theoretically bubble to the Window's handler, the OLE layer makes its ⊘/✓ decision based on the innermost AllowDrop target's response — and sees nothing. Drop is suppressed.

### The Fix

Wire ALL four drag event handlers to the **same element** that has `AllowDrop="True"`:

```xml
<!-- CORRECT: handlers on the AllowDrop element itself -->
<Window AllowDrop="True" DragOver="Handler" Drop="Handler" DragEnter="Handler" DragLeave="Handler">
    <Border AllowDrop="True"
            DragEnter="Handler"
            DragOver="Handler"
            DragLeave="Handler"
            Drop="Handler">
        ...
    </Border>
</Window>
```

Keep the Window-level handlers too — they catch drops on margin/background areas outside the border. The same handler methods work for both.

---

## ⚠️ Elevated App + OLE Drag-Drop — The Real Failure Mode (EWSR_PMR_ModApp, 2026-05-31)

> **This is more severe than UIPI's WM_DROPFILES block and cannot be fixed with `ChangeWindowMessageFilterEx`.**

### `ChangeWindowMessageFilterEx` only fixes WM_DROPFILES — not WPF drag-drop

The `ChangeWindowMessageFilterEx` / three-message trick (`WM_DROPFILES 0x0233`, `WM_COPYDATA 0x004A`, `WM_COPYGLOBALDATA 0x0049`) unblocks the **legacy Win32 shell drag path** (`DragAcceptFiles` / `WM_DROPFILES`). It does NOT affect WPF drag-and-drop.

**WPF drag-and-drop is entirely OLE-based**: it uses `RegisterDragDrop`, `IDropTarget`, and `IDataObject` through COM cross-process marshaling. When your elevated WPF app is at **High integrity** and the drag source (Windows Explorer) is at **Medium integrity**, UIPI blocks the COM channel entirely. There is no public user-mode API to un-block it.

**Symptoms are identical to the WM_DROPFILES block:**
- ⊘ cursor throughout the drag
- `DragEnter`, `DragOver`, `Drop` events never fire
- No error, no exception, complete silence

**How to confirm with the diagnostic log:**
After adding `DragEnter`/`DragOver` file logging, if the log shows nothing while elevated (but shows events when non-elevated), the OLE channel is confirmed blocked.

### The correct fix: Non-Elevated UI + Elevated Helper Process

Do NOT elevate the whole WPF app to accept file drops. Instead:

```
YourApp.exe  (asInvoker, Medium IL)     ← OLE drag-drop from Explorer works ✓
  └─ when file writes to Program Files are needed:
       └─ spawns YourInstaller.exe  (requireAdministrator manifest)
            └─ receives job via named pipe or temp JSON file
            └─ performs file copies / writes
            └─ returns result + errors
            └─ exits immediately
```

- The UI runs at Medium integrity → Explorer OLE drag-drop works
- The helper is elevated only for the write step → UAC prompt appears once per install, not on launch
- This is the same pattern used by Inno Setup, NSIS, and other professional Windows installers

### Why not DragAcceptFiles / WM_DROPFILES instead?

You could register the window for WM_DROPFILES with `DragAcceptFiles(hwnd, true)` and handle `WM_DROPFILES` via a `WndProc` hook. The message filter (`ChangeWindowMessageFilterEx`) DOES unblock this path. However:
- It requires hooking `HwndSource.AddHook` and implementing Win32 WndProc plumbing
- It bypasses WPF's `IDataObject` / `DataFormats.FileDrop` abstractions
- It only unblocks files dropped from non-elevated sources; you still can't drop from another elevated process unless you handle that separately
- It's more code for a worse architecture than the helper-process approach

Use the helper-process approach. It's cleaner and correct.

---

## ✅ #1 Gotcha — Decorative Children Intercept the Drop (EWSR_PMR_ModApp, 2026-05-31 — **CONFIRMED root cause**)

> **This is the most common WPF drop zone trap and is trivially missed.**

### What happens

You have a `Border` drop target with `AllowDrop="True"`, all four event handlers wired, and a non-null `Background`. Drag works in testing but shows ⊘ when users drop over the center of the zone — right where the instructional label is.

The instructional label is a `TextBlock` (or `StackPanel` containing TextBlocks). WPF TextBlocks are **hit-testable by default** even with no `Background`. When the cursor lands on the TextBlock, WPF's hit-test returns the TextBlock as the topmost element. The OLE drag layer checks whether that element has `AllowDrop` — it doesn't — and shows ⊘. The `Border` behind it with all the right wiring is never consulted.

**This is a silent failure:** no error, no exception, the drag just shows ⊘ and Drop never fires. It looks identical to a UIPI block or a misconfigured handler.

### The Fix

```xml
<Border x:Name="DropZoneBorder" AllowDrop="True"
        DragEnter="Handler" DragOver="Handler" DragLeave="Handler" Drop="Handler"
        Background="{StaticResource MantleBrush}">
    <StackPanel>
        <!-- Decorative labels MUST be IsHitTestVisible="False" -->
        <TextBlock Text="📦" IsHitTestVisible="False" />
        <TextBlock Text="Drop a mod .zip here" IsHitTestVisible="False" />
        <TextBlock Text="or" IsHitTestVisible="False" />
        <TextBlock Text="Multiple zips supported" IsHitTestVisible="False" />
        <!-- Interactive elements (buttons) are left hit-testable -->
        <Button Content="Browse..." />
    </StackPanel>
</Border>
```

**Rule:** Every decorative child inside a drop zone (`TextBlock`, `Image`, `Path`, purely-visual `StackPanel`) must have `IsHitTestVisible="False"`. Only interactive children (Buttons, hyperlinks) should remain hit-testable.

### Why `IsHitTestVisible="False"` works

Setting `IsHitTestVisible="False"` removes the element from the WPF hit-test tree entirely. Drag events pass straight through it to the `Border` behind it, which has `AllowDrop` and the handlers.

### Checklist addition

Add to the end of every drop zone implementation review:
- [ ] **All decorative children** (TextBlock, Image, Path, StackPanel with no AllowDrop) inside the drop zone have `IsHitTestVisible="False"`

---

## ✅ Diagnostic Logging Technique for WPF Drag-Drop

When drag-drop stops working and you can't interactively debug (e.g., sandbox environment, remote machine), add file-based logging to every handler:

```csharp
// TODO: Remove before release — active diagnostics
private static readonly string _logPath =
    Path.Combine(Path.GetTempPath(), "yourapp_dragdrop.log");

private int _dragOverLogCount;  // throttle the rapid-fire DragOver

private static void Log(string msg)
{
    try { File.AppendAllText(_logPath,
        $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}"); }
    catch { }  // never throw into UI
}

private static string DumpDragData(DragEventArgs e)
{
    try {
        return $"AllowedEffects={e.AllowedEffects} " +
               $"FileDrop={e.Data.GetDataPresent(DataFormats.FileDrop)} " +
               $"IsZipDrop={IsZipDrop(e)} " +
               $"Formats=[{string.Join(", ", e.Data.GetFormats())}]";
    }
    catch (Exception ex) { return $"(error: {ex.Message})"; }
}

protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;

    using var id = WindowsIdentity.GetCurrent();
    bool isAdmin = new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    Log($"OnSourceInitialized HWND=0x{hwnd:X} IsElevated={isAdmin}");

    bool r1 = ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES, MSGFLT_ALLOW, nint.Zero);
    Log($"  WM_DROPFILES filter: ok={r1} err={Marshal.GetLastWin32Error()}");
    // repeat for WM_COPYDATA, WM_COPYGLOBALDATA...
}

private void Window_DragEnter(object sender, DragEventArgs e)
{
    _dragOverLogCount = 0;
    // ... handler logic ...
    Log($"DragEnter: sender={sender.GetType().Name} {DumpDragData(e)}");
}

private void Window_DragOver(object sender, DragEventArgs e)
{
    // ... handler logic ...
    _dragOverLogCount++;
    if (_dragOverLogCount <= 3 || _dragOverLogCount % 10 == 0)
        Log($"DragOver #{_dragOverLogCount}: sender={sender.GetType().Name} {DumpDragData(e)}");
}
```

**What to look for in the log:**
| Observation | Meaning |
|---|---|
| No `DragEnter`/`DragOver` entries while elevated | OLE channel blocked by UIPI — need helper-process architecture |
| Entries appear, `FileDrop=False` | Data serialization issue — check what IS in `Formats` |
| Entries appear, `IsZipDrop=False` | Not a zip file, or `IsZipDrop` logic bug |
| `DragEnter`/`DragOver` present, no `Drop` | Drop handler not being called — check `e.Effects` in DragOver |
| `ChangeWindowMessageFilterEx ok=False` | Filter call failed — check error code (5=access denied, etc.) |

---

## ⚠️ Elevated App + UIPI = Silent Drag-Drop Failure

**This is the most insidious WPF drag-drop bug and the hardest to find without knowing what to look for.**

### What happens

When a WPF app is running **elevated (as Administrator)**, Windows User Interface Privilege Isolation (UIPI) blocks drag messages sent from a **non-elevated** source like Windows Explorer. The result:

- The drag cursor never changes from ⊘ to the copy cursor
- `DragEnter`, `DragOver`, and `Drop` events never fire on your window
- No error, no exception, no log — complete silence

### Which messages are blocked

| Message            | Value  |
|--------------------|--------|
| `WM_DROPFILES`     | 0x0233 |
| `WM_COPYDATA`      | 0x004A |
| `WM_COPYGLOBALDATA`| 0x0049 |

### The Fix: `ChangeWindowMessageFilterEx`

Call this after the HWND exists (in `OnSourceInitialized` or later):

```csharp
using System.Runtime.InteropServices;
using System.Windows.Interop;

// In your Window class:
[DllImport("user32.dll", SetLastError = true)]
private static extern bool ChangeWindowMessageFilterEx(
    nint hwnd, uint message, uint action, nint changeInfo);

private const uint MSGFLT_ALLOW      = 1;
private const uint WM_DROPFILES      = 0x0233;
private const uint WM_COPYDATA       = 0x004A;
private const uint WM_COPYGLOBALDATA = 0x0049;

protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, nint.Zero);
    ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, nint.Zero);
    ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, nint.Zero);
}
```

**Notes:**
- Call it in `OnSourceInitialized` — the HWND is valid here. The constructor is too early (HWND is null).
- Safe to always call — harmless when running non-elevated, essential when elevated.
- Scoped to the window's HWND — doesn't affect system-wide policy.
- This applies even if your `app.manifest` says `asInvoker` — the user can still relaunch as admin.

---

## Complete Example (EWSR_PMR_ModApp, 2026-05-31)

```xml
<!-- MainWindow.xaml -->
<Window AllowDrop="True"
        Drop="Window_Drop"
        DragEnter="Window_DragEnter"
        DragLeave="Window_DragLeave"
        DragOver="Window_DragOver">
    <Border x:Name="DropZoneBorder"
            AllowDrop="True"
            Background="{StaticResource MantleBrush}">
        <!-- drop zone content -->
    </Border>
</Window>
```

```csharp
// MainWindow.xaml.cs
protected override void OnSourceInitialized(EventArgs e)
{
    base.OnSourceInitialized(e);
    var hwnd = new WindowInteropHelper(this).Handle;
    ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, nint.Zero);
    ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, nint.Zero);
    ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, nint.Zero);
}

private void Window_DragOver(object sender, DragEventArgs e)
{
    e.Effects = IsZipDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
    e.Handled = true;
}

private void Window_Drop(object sender, DragEventArgs e)
{
    _vm.IsDragOver = false;
    if (!IsZipDrop(e)) return;
    var zips = ((string[])e.Data.GetData(DataFormats.FileDrop))
        .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
    _ = _vm.InstallZipsAsync(zips);
    e.Handled = true;
}

private static bool IsZipDrop(DragEventArgs e) =>
    e.Data.GetDataPresent(DataFormats.FileDrop)
    && e.Data.GetData(DataFormats.FileDrop) is string[] files
    && files.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
```

---

## Quick Checklist

- [ ] `AllowDrop="True"` on the **specific** drop target element (not just Window)
- [ ] Drag event handlers (`DragOver`, `DragEnter`, `DragLeave`, `Drop`) wired **to the same element that has `AllowDrop`** — NOT only to a distant ancestor like Window
- [ ] Drop target has a **non-null Background** (hit-testable)
- [ ] `DragOver` sets `e.Effects = Copy` and `e.Handled = true`
- [ ] `DragEnter` sets `e.Effects = Copy` and `e.Handled = true`
- [ ] `Drop` reads `DataFormats.FileDrop` as `string[]`
- [ ] `ChangeWindowMessageFilterEx` called in `OnSourceInitialized` for WM_DROPFILES / WM_COPYDATA / WM_COPYGLOBALDATA
- [ ] **ALL decorative children** (TextBlock, Image, StackPanel) inside the drop zone have `IsHitTestVisible="False"` — **this is the #1 gotcha; the ⊘ cursor most often comes from here, not from UIPI**
- [ ] Browse button and drop path both call the **same** install method
- [ ] **App is NOT elevated** — if it must write to Program Files, use a non-elevated UI + elevated helper process, NOT a fully elevated app

---

> **Confidence: HIGH** — Root cause confirmed by runtime log data on 2026-05-31. `IsHitTestVisible="False"` on decorative children resolved the ⊘ cursor bug completely, both elevated and non-elevated.

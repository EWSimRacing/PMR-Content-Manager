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
- [ ] Drop target has a **non-null Background** (hit-testable)
- [ ] `DragOver` sets `e.Effects = Copy` and `e.Handled = true`
- [ ] `DragEnter` sets `e.Effects = Copy` and `e.Handled = true`
- [ ] `Drop` reads `DataFormats.FileDrop` as `string[]`
- [ ] `ChangeWindowMessageFilterEx` called in `OnSourceInitialized` for WM_DROPFILES / WM_COPYDATA / WM_COPYGLOBALDATA
- [ ] Browse button and drop path both call the **same** install method

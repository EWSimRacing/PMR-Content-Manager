using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EWSR_PMR_ModApp.UI.ViewModels;
#if DEBUG
using System.IO;
using System.Security.Principal;
#endif

namespace EWSR_PMR_ModApp.UI;

/// <summary>
/// Code-behind for MainWindow. Kept thin: only handles drag-and-drop events
/// and passes them to MainViewModel. All business logic lives in the ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

#if DEBUG
    // ── DIAGNOSTIC LOGGING (Debug builds only) ───────────────────────────────
    private static readonly string _logPath =
        Path.Combine(Path.GetTempPath(), "ewsr_dragdrop.log");

    private int _dragOverLogCount;

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(_logPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
        }
        catch { /* never throw into UI */ }
    }

    private static string DumpDragData(DragEventArgs e)
    {
        try
        {
            var hasFileDrop = e.Data.GetDataPresent(DataFormats.FileDrop);
            var formats     = e.Data.GetFormats();
            var formatList  = formats is { Length: > 0 } ? string.Join(", ", formats) : "(none)";
            var isZip       = IsZipDrop(e);
            return $"AllowedEffects={e.AllowedEffects} FileDrop={hasFileDrop} " +
                   $"IsZipDrop={isZip} Formats=[{formatList}]";
        }
        catch (Exception ex)
        {
            return $"(error dumping drag data: {ex.Message})";
        }
    }
#endif

    // ── UIPI message-filter P/Invoke ─────────────────────────────────────────
    // Allows WM_DROPFILES and related messages from non-elevated sources when
    // the app runs elevated. Harmless when non-elevated; useful when the user
    // restarts as Administrator. Called in OnSourceInitialized once the HWND
    // is available.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(
        nint hwnd, uint message, uint action, nint changeInfo);

    private const uint MSGFLT_ALLOW      = 1;
    private const uint WM_DROPFILES      = 0x0233;
    private const uint WM_COPYDATA       = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;

    public MainWindow(MainViewModel viewModel)
    {
        _vm         = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    // Called once the HWND exists — safe place to install the message filter.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;

#if DEBUG
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            bool isAdmin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            Log($"OnSourceInitialized: HWND=0x{hwnd:X} IsElevated={isAdmin}");

            bool r1 = ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, nint.Zero);
            int  e1 = Marshal.GetLastWin32Error();
            Log($"  ChangeWindowMessageFilterEx WM_DROPFILES(0x0233):      ok={r1} err={e1}");

            bool r2 = ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, nint.Zero);
            int  e2 = Marshal.GetLastWin32Error();
            Log($"  ChangeWindowMessageFilterEx WM_COPYDATA(0x004A):       ok={r2} err={e2}");

            bool r3 = ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, nint.Zero);
            int  e3 = Marshal.GetLastWin32Error();
            Log($"  ChangeWindowMessageFilterEx WM_COPYGLOBALDATA(0x0049): ok={r3} err={e3}");

            if (!r1 || !r2 || !r3)
                Log("  ⚠ One or more filter calls FAILED — WM_DROPFILES path may not be unblocked.");
        }
        catch (Exception ex)
        {
            Log($"OnSourceInitialized ERROR: {ex.Message}");
        }
#else
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, nint.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, nint.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, nint.Zero);
#endif
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Drag-and-drop event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void Window_DragEnter(object sender, DragEventArgs e)
    {
        if (IsZipDrop(e))
        {
            _vm.IsDragOver = true;
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
#if DEBUG
        _dragOverLogCount = 0;
        Log($"DragEnter: sender={sender.GetType().Name} {DumpDragData(e)}");
#endif
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsZipDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
#if DEBUG
        _dragOverLogCount++;
        if (_dragOverLogCount <= 3 || _dragOverLogCount % 10 == 0)
            Log($"DragOver #{_dragOverLogCount}: sender={sender.GetType().Name} {DumpDragData(e)}");
#endif
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
        e.Handled = true;
#if DEBUG
        Log($"DragLeave: sender={sender.GetType().Name} AllowedEffects={e.AllowedEffects}");
#endif
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
#if DEBUG
        Log($"Drop: sender={sender.GetType().Name} {DumpDragData(e)}");
#endif
        if (!IsZipDrop(e)) return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var zips  = paths
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (zips.Length == 0) return;
#if DEBUG
        Log($"Drop: launching install for {zips.Length} zip(s): {string.Join(", ", zips)}");
#endif

        // Fire-and-forget async install — ViewModel manages busy state.
        _ = _vm.InstallZipsAsync(zips);
        e.Handled = true;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static bool IsZipDrop(DragEventArgs e) =>
        e.Data.GetDataPresent(DataFormats.FileDrop)
        && e.Data.GetData(DataFormats.FileDrop) is string[] files
        && files.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
}
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using EWSR_PMR_ModApp.UI.ViewModels;

namespace EWSR_PMR_ModApp.UI;

/// <summary>
/// Code-behind for MainWindow. Kept thin: only handles drag-and-drop events
/// and passes them to MainViewModel. All business logic lives in the ViewModel.
/// </summary>
public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    // ── UIPI message-filter P/Invoke ─────────────────────────────────────────
    // When the app runs elevated (as Administrator), Windows UIPI blocks
    // WM_DROPFILES from a non-elevated source (Explorer). Calling
    // ChangeWindowMessageFilterEx for each of the three drag-related messages
    // re-opens that channel for this specific HWND.
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ChangeWindowMessageFilterEx(
        nint hwnd, uint message, uint action, nint changeInfo);

    private const uint MSGFLT_ALLOW      = 1;
    private const uint WM_DROPFILES      = 0x0233;
    private const uint WM_COPYDATA       = 0x004A;
    private const uint WM_COPYGLOBALDATA = 0x0049;

    public MainWindow(MainViewModel viewModel)
    {
        _vm        = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    // Called once the HWND exists — safe place to install the message filter.
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        ChangeWindowMessageFilterEx(hwnd, WM_DROPFILES,      MSGFLT_ALLOW, nint.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYDATA,       MSGFLT_ALLOW, nint.Zero);
        ChangeWindowMessageFilterEx(hwnd, WM_COPYGLOBALDATA, MSGFLT_ALLOW, nint.Zero);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Drag-and-drop event handlers
    // ─────────────────────────────────────────────────────────────────────────

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = IsZipDrop(e) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

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
    }

    private void Window_DragLeave(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        _vm.IsDragOver = false;

        if (!IsZipDrop(e)) return;

        var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
        var zips  = paths
            .Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (zips.Length == 0) return;

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
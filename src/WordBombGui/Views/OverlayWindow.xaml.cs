// A frameless, click-through, topmost overlay that draws a colored rectangle
// border with a fully transparent interior over a screen region. Port of the
// borderWin type in ui/overlay_windows.go. WPF's native per-pixel alpha
// compositing (AllowsTransparency) replaces the Go port's color-key hack.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace WordBombTool.Views;

public partial class OverlayWindow : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_LAYERED = 0x00080000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;

    public OverlayWindow(string borderColorHex)
    {
        InitializeComponent();
        BorderRect.Stroke = Win32Interop.ParseHexBrush(borderColorHex);
        SourceInitialized += OnSourceInitialized;
        // Force the HWND to exist now (without showing the window) so
        // SourceInitialized fires and the extended styles are ready before the
        // first ShowRegion call.
        _ = new WindowInteropHelper(this).EnsureHandle();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var cur = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, cur | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }

    /// <summary>Positions and shows the overlay over the given screen-pixel
    /// region. Deliberately does not pre-convert to WPF's DIP units: the
    /// window's true bounds are set directly in physical pixels via
    /// SetWindowPos, and the border Rectangle stretches to fill (see the XAML,
    /// no fixed Width/Height) -- WPF's own per-window DPI-aware layout then
    /// renders it correctly no matter which monitor, or which DPI scale, the
    /// overlay lands on. Pre-computing DIP width/height from a single assumed
    /// scale (the old approach) drifted on mixed-DPI multi-monitor setups.</summary>
    public void ShowRegion(Region r)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Visibility = Visibility.Visible;
        SetWindowPos(hwnd, HWND_TOPMOST, r.Left, r.Top, r.Width, r.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    public void HideRegion() => Visibility = Visibility.Hidden;
}

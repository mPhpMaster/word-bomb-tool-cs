// A fullscreen, semi-transparent, drag-to-select region picker. Port of
// ui/selector_windows.go. WPF's AllowsTransparency makes this dramatically
// simpler than the raw Win32 window the Go port needed (that rewrite was
// itself a fix for a walk-related crash) -- true per-pixel alpha compositing
// is native here, no color-key tricks required.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WordBombTool.Views;

public partial class RegionSelectorWindow : Window
{
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private const uint SWP_NOZORDER = 0x0004;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    private Point _start;             // DIP, for the live visual rectangle only
    private POINT _startPhysical;     // true screen pixels, for the saved Region
    private bool _dragging;
    private Region? _result;

    public RegionSelectorWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        KeyDown += OnKeyDown;
        Loaded += (_, _) => { Focus(); Keyboard.Focus(this); };
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var (vx, vy, vw, vh) = Win32Interop.VirtualScreen();

        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HWND_TOPMOST, vx, vy, vw, vh, SWP_NOZORDER);
        SetForegroundWindow(hwnd);
        // No DPI matrix captured here on purpose: with multiple monitors at
        // different scale factors, a single scale for a window spanning the
        // whole virtual desktop is only ever approximately right. Physical
        // pixel coordinates for the saved Region come from GetCursorPos below
        // instead, which is always correct regardless of per-monitor DPI.
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _start = e.GetPosition(RootCanvas);
        GetCursorPos(out _startPhysical);
        _dragging = true;
        SelectionRect.Visibility = Visibility.Visible;
        Canvas.SetLeft(SelectionRect, _start.X);
        Canvas.SetTop(SelectionRect, _start.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        Mouse.Capture((UIElement)sender);
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging) return;
        // DIP-space rectangle: cosmetic only (the live drag outline). On a
        // single monitor, or same-DPI multi-monitor, this is pixel-accurate;
        // on mixed-DPI setups it may be very slightly off visually during the
        // drag, but the final saved Region below is always exact.
        var p = e.GetPosition(RootCanvas);
        var x = Math.Min(_start.X, p.X);
        var y = Math.Min(_start.Y, p.Y);
        var w = Math.Abs(p.X - _start.X);
        var h = Math.Abs(p.Y - _start.Y);
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        Mouse.Capture(null);

        GetCursorPos(out var endPhysical);
        var left = Math.Min(_startPhysical.X, endPhysical.X);
        var top = Math.Min(_startPhysical.Y, endPhysical.Y);
        var width = Math.Abs(endPhysical.X - _startPhysical.X);
        var height = Math.Abs(endPhysical.Y - _startPhysical.Y);

        _result = (width > 0 && height > 0) ? new Region(left, top, width, height) : null;
        DialogResult = _result != null;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result = null;
            DialogResult = false;
            Close();
        }
    }

    /// <summary>Opens a fullscreen picker and returns the dragged rectangle in
    /// virtual-screen coordinates, or null if the user cancelled (Escape or a
    /// zero-size drag). Must be called on the UI thread.</summary>
    public static Region? PickRegion(Window? owner)
    {
        var win = new RegionSelectorWindow();
        if (owner != null && owner.IsVisible) win.Owner = owner;
        win.ShowDialog();
        return win._result;
    }
}

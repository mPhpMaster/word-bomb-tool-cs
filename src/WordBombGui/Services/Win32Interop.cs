// Small Win32 P/Invoke helpers used to layer WPF windows in ways the framework
// doesn't expose directly: whole-window alpha blending that keeps native window
// chrome (border/titlebar) intact, forced topmost, click-through, and the
// virtual-screen bounds used by the fullscreen region selector.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace WordBombTool;

public static class Win32Interop
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint LWA_ALPHA = 0x00000002;

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int nIndex);

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    /// <summary>Applies whole-window alpha (0..255) to a layered window while
    /// keeping its normal chrome (border/titlebar/menu).</summary>
    public static void SetWindowAlpha(IntPtr hwnd, byte alpha)
    {
        var cur = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, cur | WS_EX_LAYERED);
        SetLayeredWindowAttributes(hwnd, 0, alpha, LWA_ALPHA);
    }

    /// <summary>Keeps a window above all others without moving/resizing it.</summary>
    public static void SetTopmost(IntPtr hwnd)
    {
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Makes a layered window click-through (mouse events pass to
    /// whatever is beneath it).</summary>
    public static void SetClickThrough(IntPtr hwnd)
    {
        var cur = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, cur | WS_EX_LAYERED | WS_EX_TRANSPARENT);
    }

    /// <summary>Bounds of the entire virtual desktop (all monitors), in device pixels.</summary>
    public static (int x, int y, int w, int h) VirtualScreen()
    {
        var x = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var y = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        return (x, y, w, h);
    }

    public static Color ParseHexColor(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length != 6) return Colors.Black;
        var r = Convert.ToByte(s.Substring(0, 2), 16);
        var g = Convert.ToByte(s.Substring(2, 2), 16);
        var b = Convert.ToByte(s.Substring(4, 2), 16);
        return Color.FromRgb(r, g, b);
    }

    public static SolidColorBrush ParseHexBrush(string hex)
    {
        var b = new SolidColorBrush(ParseHexColor(hex));
        b.Freeze();
        return b;
    }
}

// Synthetic keystroke generation on Windows via SendInput. Port of typing_windows.go.
using System.Runtime.InteropServices;
using System.Text;

namespace WordBombTool;

public static class InputSimulator
{
    private const int InputKeyboard = 1;
    private const uint KeyEventUnicode = 0x0004;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventScancode = 0x0008;
    private const ushort ScanEnter = 0x1C; // hardware scan code for the Enter key
    private const ushort VkReturn = 0x0D;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // Matches the real Win32 INPUT union layout on x64: type(4) + 4 bytes padding
    // before the union, then the union sized to its largest member (MOUSEINPUT,
    // 28 bytes, 8-aligned up to 32) = 40 bytes total. KEYBDINPUT is only 24 bytes
    // (2+2+4+4+8 with alignment), so without the explicit Size below the CLR lays
    // this out as 32 bytes -- 8 short of native. SendInput validates cbSize
    // against the platform's real sizeof(INPUT) and silently fails (returns 0,
    // types nothing) if it doesn't match exactly, which is what was happening.
    [StructLayout(LayoutKind.Sequential, Size = 40)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    /// <summary>The marshaled size of our INPUT struct. Exposed purely so a unit
    /// test can assert it stays exactly 40 bytes (the real Win32 sizeof(INPUT)
    /// on x64) -- a mismatch here means SendInput will silently fail every call,
    /// which is exactly the bug this guards against regressing to.</summary>
    public static int NativeInputStructSizeBytes => Marshal.SizeOf<INPUT>();

    private static void Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return;
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
        {
            var err = Marshal.GetLastWin32Error();
            AppLog.Errorf("SendInput sent {0}/{1} events (Win32 error {2})", sent, inputs.Length, err);
        }
    }

    /// <summary>Sends a single Unicode rune (char) as a key down + key up pair.</summary>
    public static void TypeChar(char c)
    {
        var down = new INPUT { type = InputKeyboard, ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KeyEventUnicode } };
        var up = new INPUT { type = InputKeyboard, ki = new KEYBDINPUT { wVk = 0, wScan = c, dwFlags = KeyEventUnicode | KeyEventKeyUp } };
        Send(down, up);
    }

    /// <summary>Types each character of s in order with no inter-key delay.</summary>
    public static void TypeString(string s)
    {
        foreach (var c in s) TypeChar(c);
    }

    /// <summary>Presses and releases the Enter key. Sends both the virtual-key
    /// code and the hardware scan code, with a short hold between down and up, so
    /// the keystroke looks like a real key press -- many games/web apps ignore a
    /// bare virtual-key Enter that carries no scan code.</summary>
    public static void PressEnter()
    {
        var down = new INPUT { type = InputKeyboard, ki = new KEYBDINPUT { wVk = VkReturn, wScan = ScanEnter, dwFlags = KeyEventScancode } };
        var up = new INPUT { type = InputKeyboard, ki = new KEYBDINPUT { wVk = VkReturn, wScan = ScanEnter, dwFlags = KeyEventScancode | KeyEventKeyUp } };
        Send(down);
        Thread.Sleep(25);
        Send(up);
    }
}

// A global low-level keyboard hook and hotkey dispatcher. Port of hook_windows.go
// / keys.go (which themselves replaced Python's `keyboard` library).
using System.Runtime.InteropServices;

namespace WordBombTool;

internal static class VK
{
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12; // Alt
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Prior = 0x21; // Page Up
    public const int Next = 0x22;  // Page Down
    public const int Delete = 0x2E;
    public const int Capital = 0x14; // Caps Lock
    public const int F1 = 0x70;
    public const int F2 = 0x71;
    public const int Period = 0xBE; // OEM_PERIOD
    public const int One = 0x31;
    public const int Z = 0x5A;
    public const int C = 0x43;
    public const int Q = 0x51;
    public const int Escape = 0x1B;

    public static readonly Dictionary<string, int> TokenVK = new()
    {
        ["shift"] = Shift,
        ["ctrl"] = Control,
        ["control"] = Control,
        ["alt"] = Menu,
        ["tab"] = Tab,
        ["enter"] = Return,
        ["page up"] = Prior,
        ["pageup"] = Prior,
        ["page down"] = Next,
        ["pagedown"] = Next,
        ["delete"] = Delete,
        ["caps lock"] = Capital,
        ["capslock"] = Capital,
        ["f1"] = F1,
        ["f2"] = F2,
        ["."] = Period,
        ["1"] = One,
        ["z"] = Z,
        ["c"] = C,
        ["q"] = Q,
    };

    /// <summary>Collapses left/right-specific modifier VK codes (which a
    /// low-level keyboard hook reports) down to their generic equivalents, so a
    /// hotkey whose main key is a bare modifier (e.g. "shift") matches.</summary>
    public static int Normalize(int vk) => vk switch
    {
        0xA0 or 0xA1 => Shift,   // VK_LSHIFT / VK_RSHIFT
        0xA2 or 0xA3 => Control, // VK_LCONTROL / VK_RCONTROL
        0xA4 or 0xA5 => Menu,    // VK_LMENU / VK_RMENU
        _ => vk,
    };
}

/// <summary>Parses a hotkey spec like "ctrl+f2" or "shift" into its main virtual
/// key code and required modifier flags.</summary>
internal readonly struct HotkeySpec
{
    public readonly int Vk;
    public readonly bool Ctrl, Alt, Shift;
    private HotkeySpec(int vk, bool ctrl, bool alt, bool shift) { Vk = vk; Ctrl = ctrl; Alt = alt; Shift = shift; }

    public static bool TryParse(string spec, out HotkeySpec result)
    {
        var parts = spec.ToLowerInvariant().Trim().Split('+');
        int vk = 0;
        bool ctrl = false, alt = false, shift = false;
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim();
            var isModifier = i < parts.Length - 1;
            if (isModifier && (p == "ctrl" || p == "control")) { ctrl = true; continue; }
            if (isModifier && p == "alt") { alt = true; continue; }
            if (isModifier && p == "shift") { shift = true; continue; }
            if (!VK.TokenVK.TryGetValue(p, out vk))
            {
                result = default;
                return false;
            }
        }
        result = new HotkeySpec(vk, ctrl, alt, shift);
        return true;
    }
}

/// <summary>A global low-level keyboard hook that fires registered callbacks.
/// Must be started on a dedicated thread with a Win32 message loop (WPF's UI
/// thread already has one via Dispatcher, but the hook's own GetMessage loop
/// runs on a plain background thread here, mirroring the Go implementation).</summary>
public sealed class HotkeyHook
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYUP = 0x0105;
    private const int WM_QUIT = 0x0012;

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    private static extern int GetMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int x, y;
    }

    private readonly object _lock = new();
    private readonly List<(HotkeySpec spec, Action callback)> _hotkeys = new();
    private readonly HashSet<int> _down = new();
    private HookProc? _hookProcDelegate; // keep alive so the GC doesn't collect the callback
    private IntPtr _hookHandle;
    private uint _threadId;
    private Thread? _thread;

    /// <summary>Adds a hotkey. Unknown specs are ignored (reported by the boolean).</summary>
    public bool Register(string spec, Action callback)
    {
        if (!HotkeySpec.TryParse(spec, out var hk)) return false;
        lock (_lock) _hotkeys.Add((hk, callback));
        return true;
    }

    /// <summary>Installs the hook and runs its message loop on a dedicated thread.
    /// Returns immediately; call Stop() to terminate.</summary>
    public void Start()
    {
        _thread = new Thread(ThreadMain) { IsBackground = true, Name = "WBT-HotkeyHook" };
        _thread.Start();
    }

    private void ThreadMain()
    {
        _threadId = GetCurrentThreadId();
        _hookProcDelegate = HookCallback;
        _hookHandle = SetWindowsHookExW(WH_KEYBOARD_LL, _hookProcDelegate, IntPtr.Zero, 0);

        while (GetMessageW(out _, IntPtr.Zero, 0, 0) > 0)
        {
            // No dispatch needed: this thread only exists to keep the low-level
            // hook's message queue alive; hotkey callbacks fire from HookCallback.
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    public void Stop()
    {
        if (_threadId != 0) PostThreadMessageW(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
    }

    // This runs as a reverse P/Invoke callback invoked directly by Windows for
    // every keystroke system-wide. An exception escaping a native callback
    // boundary is fatal in .NET -- it terminates the whole process immediately,
    // with nothing caught by any managed handler. The try/catch here is the
    // last line of defense against that; CallNextHookEx must still run in every
    // case (in the finally) or every other app's keyboard hook chain breaks.
    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode == 0)
            {
                var msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    var ks = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    HandleKeyDown(VK.Normalize((int)ks.vkCode));
                }
                else if (msg == WM_KEYUP || msg == WM_SYSKEYUP)
                {
                    var ks = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    lock (_lock) _down.Remove(VK.Normalize((int)ks.vkCode));
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Errorf("HotkeyHook callback error (nCode={0}): {1}\n{2}", nCode, ex.Message, ex.StackTrace);
        }
        // CallNextHookEx must run in every case (success or caught exception) or
        // every other app's keyboard hook chain breaks.
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void HandleKeyDown(int vk)
    {
        List<Action>? fire = null;
        lock (_lock)
        {
            // Edge detection: ignore auto-repeat while the key stays held.
            if (_down.Contains(vk)) return;
            _down.Add(vk);

            var ctrl = AsyncDown(VK.Control);
            var alt = AsyncDown(VK.Menu);
            var shift = AsyncDown(VK.Shift);

            foreach (var (spec, callback) in _hotkeys)
            {
                if (spec.Vk != vk) continue;
                // Modifier requirements must match. For a bare key (no modifiers
                // required) ctrl/alt must not be held, so combinations like
                // Ctrl+Tab don't trigger the plain Tab hotkey. Shift is ignored
                // for bare keys unless the key itself is Shift.
                if (spec.Ctrl != ctrl || spec.Alt != alt) continue;
                if (spec.Shift && !shift) continue;
                (fire ??= new List<Action>()).Add(callback);
            }
        }
        if (fire != null)
            foreach (var cb in fire)
                _ = Task.Run(cb);
    }

    private static bool AsyncDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
}

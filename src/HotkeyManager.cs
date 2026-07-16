namespace Tactile;

/// <summary>
/// Registers the global hotkey and raises <see cref="Pressed"/>.
///
/// Strategy: try RegisterHotKey first (cleanest, no hooks). Windows refuses
/// chords the shell already owns — Explorer registers Win+T for taskbar
/// cycling — so on refusal we fall back to a low-level keyboard hook
/// (WH_KEYBOARD_LL) that intercepts the chord before Explorer sees it. That
/// is exactly how the AutoHotkey version grabbed Win+T. The hook inspects
/// key-downs only and swallows nothing else, so normal typing is untouched.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;

    private bool _registered;          // RegisterHotKey path active
    private IntPtr _hook;              // WH_KEYBOARD_LL path active
    // Field (not local) so the GC can never collect the delegate while hooked.
    private readonly Win32.LowLevelKeyboardProc _hookProc;
    private uint _mods;
    private uint _vk;

    public event Action? Pressed;

    public HotkeyManager()
    {
        _hookProc = HookCallback;
        CreateHandle(new CreateParams());
    }

    public bool Register(uint modifiers, uint vk)
    {
        Unregister();
        _mods = modifiers;
        _vk = vk;

        // MOD_NOREPEAT: holding the chord down fires only once.
        _registered = Win32.RegisterHotKey(Handle, HotkeyId, modifiers | Win32.MOD_NOREPEAT, vk);
        if (_registered)
            return true;

        // Chord is owned by another registrant (for Win+T: Explorer itself).
        // Take it anyway with a low-level keyboard hook.
        _hook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _hookProc,
            Win32.GetModuleHandleW(null), 0);
        return _hook != IntPtr.Zero;
    }

    public void Unregister()
    {
        if (_registered)
        {
            Win32.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
        if (_hook != IntPtr.Zero)
        {
            Win32.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && ((int)wParam is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN))
        {
            var data = System.Runtime.InteropServices.Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(lParam);
            // Skip injected input (incl. our own dummy key) to avoid feedback loops.
            if ((data.flags & Win32.LLKHF_INJECTED) == 0 && data.vkCode == _vk && ModifiersMatch())
            {
                if ((_mods & Win32.MOD_WIN) != 0)
                    SendDummyKeyUp(); // mark the Win press as "used" so releasing it won't open Start

                // Defer the real work: hook callbacks must return fast or the
                // system silently drops the hook. WndProc picks this up.
                Win32.PostMessageW(Handle, Win32.WM_HOTKEY, HotkeyId, IntPtr.Zero);
                return (IntPtr)1; // swallow the keystroke — Explorer never sees it
            }
        }
        return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>True when exactly the configured modifiers are held.</summary>
    private bool ModifiersMatch()
    {
        static bool Down(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
        bool win = Down(Win32.VK_LWIN) || Down(Win32.VK_RWIN);
        bool ctrl = Down(Win32.VK_CONTROL);
        bool alt = Down(Win32.VK_MENU);
        bool shift = Down(Win32.VK_SHIFT);
        return win == ((_mods & Win32.MOD_WIN) != 0)
            && ctrl == ((_mods & Win32.MOD_CONTROL) != 0)
            && alt == ((_mods & Win32.MOD_ALT) != 0)
            && shift == ((_mods & Win32.MOD_SHIFT) != 0);
    }

    /// <summary>Injects a key-up of the unassigned VK 0xFF while Win is held —
    /// the standard trick to stop the lone-Win-press Start menu from opening
    /// after we swallowed the chord's letter.</summary>
    private static void SendDummyKeyUp()
    {
        var inputs = new Win32.INPUT[1];
        inputs[0].type = 1; // INPUT_KEYBOARD
        inputs[0].ki.wVk = 0xFF;
        inputs[0].ki.dwFlags = Win32.KEYEVENTF_KEYUP;
        Win32.SendInput(1, inputs, System.Runtime.InteropServices.Marshal.SizeOf<Win32.INPUT>());
    }

    protected override void WndProc(ref Message m)
    {
        // Arrives from RegisterHotKey directly, or posted by the hook callback.
        if (m.Msg == Win32.WM_HOTKEY && (int)m.WParam == HotkeyId)
            Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        DestroyHandle();
    }
}

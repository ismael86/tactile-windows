namespace Tactile;

/// <summary>
/// Registers global hotkeys and invokes their handlers. Supports any number of
/// chords (grid toggle, save layout, layout picker, per-layout restore keys).
///
/// Strategy per chord: try RegisterHotKey first (cleanest, no hooks). Windows
/// refuses chords the shell already owns — Explorer registers Win+T for taskbar
/// cycling — so on refusal that chord falls back to a low-level keyboard hook
/// (WH_KEYBOARD_LL) which intercepts it before Explorer sees it. That is
/// exactly how the AutoHotkey version grabbed Win+T. The hook inspects
/// key-downs only and swallows nothing but the registered chords, so normal
/// typing is untouched.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private sealed class Registration
    {
        public required int Id;
        public required uint Modifiers;
        public required uint Vk;
        public required Action Handler;
        public bool ViaHook;
    }

    private readonly Dictionary<int, Registration> _regs = [];
    private int _nextId = 1;

    private IntPtr _hook;
    // Field (not local) so the GC can never collect the delegate while hooked.
    private readonly Win32.LowLevelKeyboardProc _hookProc;

    public HotkeyManager()
    {
        _hookProc = HookCallback;
        CreateHandle(new CreateParams());
    }

    /// <summary>Registers a chord. Returns its id, or 0 when the chord could not
    /// be claimed by either mechanism.</summary>
    public int Register(uint modifiers, uint vk, Action handler)
    {
        int id = _nextId++;
        var reg = new Registration { Id = id, Modifiers = modifiers, Vk = vk, Handler = handler };

        // MOD_NOREPEAT: holding the chord down fires only once.
        if (Win32.RegisterHotKey(Handle, id, modifiers | Win32.MOD_NOREPEAT, vk))
        {
            _regs[id] = reg;
            return id;
        }

        // Chord is owned by another registrant (for Win+T: Explorer itself).
        // Take it anyway with a low-level keyboard hook.
        reg.ViaHook = true;
        if (!EnsureHook())
            return 0;
        _regs[id] = reg;
        return id;
    }

    public void Unregister(int id)
    {
        if (!_regs.Remove(id, out var reg))
            return;
        if (!reg.ViaHook)
            Win32.UnregisterHotKey(Handle, id);
        ReleaseHookIfUnused();
    }

    public void UnregisterAll()
    {
        foreach (var reg in _regs.Values)
        {
            if (!reg.ViaHook)
                Win32.UnregisterHotKey(Handle, reg.Id);
        }
        _regs.Clear();
        ReleaseHookIfUnused();
    }

    private bool EnsureHook()
    {
        if (_hook != IntPtr.Zero)
            return true;
        _hook = Win32.SetWindowsHookExW(Win32.WH_KEYBOARD_LL, _hookProc, Win32.GetModuleHandleW(null), 0);
        return _hook != IntPtr.Zero;
    }

    private void ReleaseHookIfUnused()
    {
        if (_hook != IntPtr.Zero && !_regs.Values.Any(r => r.ViaHook))
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
            if ((data.flags & Win32.LLKHF_INJECTED) == 0)
            {
                foreach (var reg in _regs.Values)
                {
                    if (!reg.ViaHook || reg.Vk != data.vkCode || !ModifiersMatch(reg.Modifiers))
                        continue;

                    if ((reg.Modifiers & Win32.MOD_WIN) != 0)
                        SendDummyKeyUp(); // mark the Win press as "used" so releasing it won't open Start

                    // Defer the real work: hook callbacks must return fast or the
                    // system silently drops the hook. WndProc picks this up.
                    Win32.PostMessageW(Handle, Win32.WM_HOTKEY, reg.Id, IntPtr.Zero);
                    return (IntPtr)1; // swallow the keystroke — the shell never sees it
                }
            }
        }
        return Win32.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>True when exactly the given modifiers are held.</summary>
    private static bool ModifiersMatch(uint mods)
    {
        static bool Down(int vk) => (Win32.GetAsyncKeyState(vk) & 0x8000) != 0;
        bool win = Down(Win32.VK_LWIN) || Down(Win32.VK_RWIN);
        return win == ((mods & Win32.MOD_WIN) != 0)
            && Down(Win32.VK_CONTROL) == ((mods & Win32.MOD_CONTROL) != 0)
            && Down(Win32.VK_MENU) == ((mods & Win32.MOD_ALT) != 0)
            && Down(Win32.VK_SHIFT) == ((mods & Win32.MOD_SHIFT) != 0);
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
        if (m.Msg == Win32.WM_HOTKEY && _regs.TryGetValue((int)m.WParam, out var reg))
            reg.Handler();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}

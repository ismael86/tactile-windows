namespace Tactile;

/// <summary>
/// Registers the global hotkey via RegisterHotKey against a hidden native
/// window and raises <see cref="Pressed"/> on WM_HOTKEY. No keyboard hook is
/// installed — outside the hotkey, typing is completely untouched.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int HotkeyId = 1;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyManager() => CreateHandle(new CreateParams());

    public bool Register(uint modifiers, uint vk)
    {
        Unregister();
        // MOD_NOREPEAT: holding the chord down fires only once.
        _registered = Win32.RegisterHotKey(Handle, HotkeyId, modifiers | Win32.MOD_NOREPEAT, vk);
        return _registered;
    }

    public void Unregister()
    {
        if (_registered)
        {
            Win32.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
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

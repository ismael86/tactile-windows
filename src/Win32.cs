using System.Runtime.InteropServices;

namespace Tactile;

/// <summary>All P/Invoke signatures and constants in one place.</summary>
internal static class Win32
{
    internal const uint MOD_ALT = 0x1;
    internal const uint MOD_CONTROL = 0x2;
    internal const uint MOD_SHIFT = 0x4;
    internal const uint MOD_WIN = 0x8;
    internal const uint MOD_NOREPEAT = 0x4000;

    internal const int WM_HOTKEY = 0x0312;
    internal const int SW_RESTORE = 9;
    internal const uint SWP_NOZORDER = 0x4;
    internal const uint SWP_NOACTIVATE = 0x10;
    internal const uint DWMWA_EXTENDED_FRAME_BOUNDS = 9;
    internal const int WS_EX_TOOLWINDOW = 0x80;
    internal const int WS_EX_TOPMOST = 0x8;
    internal const int WS_EX_NOACTIVATE = 0x08000000;

    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    internal static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool IsZoomed(IntPtr hWnd);

    [DllImport("user32.dll")]
    internal static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>attr 9 (DWMWA_EXTENDED_FRAME_BOUNDS) returns the VISIBLE window
    /// bounds in physical pixels — excludes the invisible Win10/11 resize border
    /// that GetWindowRect includes.</summary>
    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(IntPtr hwnd, uint attr, out RECT rect, int cbSize);

    /// <summary>Bitmap.GetHicon() hands us ownership of an icon handle; it must be
    /// released with DestroyIcon or it leaks a GDI handle per call.</summary>
    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    internal static string GetWindowClass(IntPtr hWnd)
    {
        var buf = new char[256];
        int len = GetClassName(hWnd, buf, buf.Length);
        return len > 0 ? new string(buf, 0, len) : "";
    }
}

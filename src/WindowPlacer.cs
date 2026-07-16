namespace Tactile;

/// <summary>
/// Best-effort window placement: restore, DWM frame compensation, SetWindowPos,
/// then verify-and-reapply timers (150/450 ms) for windows that re-adjust
/// themselves after the fact (Electron apps). Never throws.
/// </summary>
public static class WindowPlacer
{
    private readonly record struct FrameDeltas(int Left, int Top, int Right, int Bottom);

    public static void Place(IntPtr target, Rectangle visibleRect)
    {
        try
        {
            if (!Win32.IsWindow(target))
                return;

            // A maximized/minimized window ignores SetWindowPos sizing — and its
            // frame deltas are wrong while maximized, so restore BEFORE measuring.
            if (Win32.IsIconic(target) || Win32.IsZoomed(target))
                Win32.ShowWindow(target, Win32.SW_RESTORE);

            Apply(target, visibleRect);
            Win32.SetForegroundWindow(target);

            ScheduleVerify(target, visibleRect, 150);
            ScheduleVerify(target, visibleRect, 450);
        }
        catch
        {
            // Window vanished mid-flow or refused the geometry — best-effort only.
        }
    }

    /// <summary>Positions the window so its VISIBLE frame lands on rect, by
    /// expanding the move rect with the invisible-border deltas.</summary>
    private static void Apply(IntPtr target, Rectangle rect)
    {
        FrameDeltas d = GetFrameDeltas(target);
        Win32.SetWindowPos(target, IntPtr.Zero,
            rect.X - d.Left, rect.Y - d.Top,
            rect.Width + d.Left + d.Right, rect.Height + d.Top + d.Bottom,
            Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    /// <summary>Per-edge difference between the window rect and the visible DWM
    /// frame: the invisible Win10/11 resize border (~7px left/right/bottom).</summary>
    private static FrameDeltas GetFrameDeltas(IntPtr hwnd)
    {
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var frame,
                System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>()) == 0
            && Win32.GetWindowRect(hwnd, out var rect))
        {
            return new FrameDeltas(
                Math.Max(0, frame.Left - rect.Left),
                Math.Max(0, frame.Top - rect.Top),
                Math.Max(0, rect.Right - frame.Right),
                Math.Max(0, rect.Bottom - frame.Bottom));
        }
        return new FrameDeltas(0, 0, 0, 0);
    }

    private static Rectangle? GetVisibleBounds(IntPtr hwnd)
    {
        if (Win32.DwmGetWindowAttribute(hwnd, Win32.DWMWA_EXTENDED_FRAME_BOUNDS, out var f,
                System.Runtime.InteropServices.Marshal.SizeOf<Win32.RECT>()) == 0)
            return Rectangle.FromLTRB(f.Left, f.Top, f.Right, f.Bottom);
        return null;
    }

    private static void ScheduleVerify(IntPtr target, Rectangle intended, int delayMs)
    {
        var timer = new System.Windows.Forms.Timer { Interval = delayMs };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            try
            {
                if (!Win32.IsWindow(target))
                    return;
                if (GetVisibleBounds(target) is Rectangle current && OffByMoreThanOnePixel(current, intended))
                    Apply(target, intended);
            }
            catch
            {
                // best-effort
            }
        };
        timer.Start();
    }

    private static bool OffByMoreThanOnePixel(Rectangle a, Rectangle b) =>
        Math.Abs(a.Left - b.Left) > 1 || Math.Abs(a.Top - b.Top) > 1 ||
        Math.Abs(a.Right - b.Right) > 1 || Math.Abs(a.Bottom - b.Bottom) > 1;
}

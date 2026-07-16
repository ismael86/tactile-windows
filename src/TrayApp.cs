using Microsoft.Win32;
using System.Diagnostics;

namespace Tactile;

/// <summary>
/// Application context: tray icon + menu, hotkey wiring, and the toggle/session
/// logic tying the overlay to the captured target window.
/// </summary>
public sealed class TrayApp : ApplicationContext
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "Tactile";

    private readonly string _configPath;
    private Config _cfg;
    private readonly NotifyIcon _tray;
    private readonly HotkeyManager _hotkey;
    private readonly IntPtr _trayIconHandle;
    private readonly ToolStripMenuItem _headerItem;
    private readonly ToolStripMenuItem _loginItem;
    private OverlayForm? _overlay;
    private IntPtr _target;

    public TrayApp(Config cfg, string configPath)
    {
        _cfg = cfg;
        _configPath = configPath;

        _hotkey = new HotkeyManager();
        _hotkey.Pressed += Toggle;
        (uint mods, uint vk, string display) = _cfg.ParseHotkey();
        if (!_hotkey.Register(mods, vk))
        {
            MessageBox.Show(
                $"Could not register the hotkey {display} — another application already owns it.\n\n" +
                "Is the AutoHotkey version (tactile.ahk) still running? Exit it and start Tactile again.",
                "Tactile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _hotkey.Dispose();
            Environment.Exit(1);
        }

        _headerItem = new ToolStripMenuItem($"Tactile — {display}") { Enabled = false };
        _loginItem = new ToolStripMenuItem("Start at Login", null, (_, _) => ToggleStartAtLogin());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_headerItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_loginItem);
        menu.Items.Add(new ToolStripMenuItem("Reload Config", null, (_, _) => ReloadConfig()));
        menu.Items.Add(new ToolStripMenuItem("Edit Config", null, (_, _) => EditConfig()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));
        menu.Opening += (_, _) => _loginItem.Checked = IsStartAtLoginEnabled();

        (Icon icon, _trayIconHandle) = CreateGridIcon();
        _tray = new NotifyIcon
        {
            Icon = icon,
            Text = $"Tactile ({display})",
            ContextMenuStrip = menu,
            Visible = true,
        };
    }

    // ------------------------------ Toggle ----------------------------------

    private void Toggle()
    {
        if (_overlay is not null)
        {
            _overlay.CancelExternally(); // pressing the chord again while open cancels
            return;
        }

        // Capture the target hwnd BEFORE any UI exists, so placement can never
        // accidentally operate on the overlay itself.
        IntPtr target = Win32.GetForegroundWindow();
        string cls = target != IntPtr.Zero ? Win32.GetWindowClass(target) : "";
        if (target == IntPtr.Zero || cls is "" or "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            Toast.Show("No window to place");
            return;
        }

        _target = target;
        Rectangle workArea = Screen.FromHandle(target).WorkingArea; // physical px under PerMonitorV2

        var overlay = new OverlayForm(_cfg, workArea);
        overlay.PlaceRequested += (a, b) =>
        {
            _overlay = null;
            WindowPlacer.Place(_target, GridGeometry.PlacementRect(_cfg, a, b, workArea));
        };
        overlay.Cancelled += restoreFocus =>
        {
            _overlay = null;
            if (restoreFocus && Win32.IsWindow(_target))
                Win32.SetForegroundWindow(_target);
        };
        _overlay = overlay;
        overlay.Show();
    }

    // ------------------------------ Tray menu -------------------------------

    private bool IsStartAtLoginEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(RunValueName) is not null;
    }

    private void ToggleStartAtLogin()
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (key.GetValue(RunValueName) is not null)
                key.DeleteValue(RunValueName);
            else
                // Environment.ProcessPath, not Assembly.Location (empty in single-file publish).
                key.SetValue(RunValueName, $"\"{Environment.ProcessPath}\"");
        }
        catch (Exception ex)
        {
            Toast.Show("Start at Login failed: " + ex.Message);
        }
    }

    private void ReloadConfig()
    {
        Config newCfg;
        try
        {
            newCfg = Config.LoadOrCreate(_configPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("tactile.json is invalid — keeping the previous configuration.\n\n" + ex.Message,
                "Tactile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        (uint mods, uint vk, string display) = newCfg.ParseHotkey();
        if (!_hotkey.Register(mods, vk))
        {
            // New chord is taken; fall back to the old, still-valid one.
            (uint oldMods, uint oldVk, string oldDisplay) = _cfg.ParseHotkey();
            _hotkey.Register(oldMods, oldVk);
            MessageBox.Show($"Hotkey {display} is already taken — keeping {oldDisplay}.",
                "Tactile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _cfg = newCfg;
        _headerItem.Text = $"Tactile — {display}";
        _tray.Text = $"Tactile ({display})";
        Toast.Show("Config reloaded");
    }

    private void EditConfig()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_configPath) { UseShellExecute = true });
        }
        catch
        {
            Process.Start("notepad.exe", $"\"{_configPath}\"");
        }
    }

    private void ExitApp()
    {
        _tray.Visible = false; // before exit, else a ghost tray icon lingers
        _hotkey.Dispose();
        _tray.Dispose();
        if (_trayIconHandle != IntPtr.Zero)
            Win32.DestroyIcon(_trayIconHandle);
        ExitThread();
    }

    /// <summary>Draws a simple 4x3 grid glyph as the tray icon at runtime —
    /// no icon asset needed. The HICON from GetHicon must be destroyed by us.</summary>
    private static (Icon, IntPtr) CreateGridIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(Color.White);
            const int cols = 4, rows = 3, pad = 2, gap = 2;
            float cw = (32f - 2 * pad - (cols - 1) * gap) / cols;
            float ch = (32f - 2 * pad - (rows - 1) * gap) / rows;
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    g.FillRectangle(brush, pad + c * (cw + gap), pad + r * (ch + gap), cw, ch);
        }
        IntPtr hIcon = bmp.GetHicon();
        return (Icon.FromHandle(hIcon), hIcon);
    }
}

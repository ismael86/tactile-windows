namespace Tactile;

// Minimal UI for the layouts feature: a name prompt (save), a keyboard picker
// (restore), and a chord recorder (assign hotkey). All transient — no windows
// beyond the moment of use. Mirrors the macOS port's LayoutsUI.swift.

public static class LayoutsUI
{
    /// <summary>Modal name prompt. Returns null when cancelled. Overwriting an
    /// existing name requires an extra confirm.</summary>
    public static string? PromptForName(IEnumerable<string> existingNames)
    {
        using var form = new NamePromptForm();
        if (form.ShowDialog() != DialogResult.OK)
            return null;
        string name = form.EnteredName;
        if (name.Length == 0)
            return null;

        if (existingNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            var answer = MessageBox.Show(
                $"The existing arrangement saved as \"{name}\" will be overwritten.",
                $"Replace layout \"{name}\"?", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
            if (answer != DialogResult.OK)
                return null;
        }
        return name;
    }

    private sealed class NamePromptForm : Form
    {
        private readonly TextBox _box;

        public string EnteredName => _box.Text.Trim();

        public NamePromptForm()
        {
            Text = "Save layout";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(340, 130);

            var label = new Label
            {
                Text = "Name for the current window arrangement:",
                Location = new Point(14, 14),
                AutoSize = true,
            };
            _box = new TextBox
            {
                Location = new Point(16, 42),
                Width = 306,
                PlaceholderText = "e.g. work",
            };
            var ok = new Button { Text = "Save", DialogResult = DialogResult.OK, Location = new Point(150, 84), Width = 80 };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(240, 84), Width = 80 };

            Controls.AddRange([label, _box, ok, cancel]);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            _box.Focus();
        }
    }
}

/// <summary>Base for the small dark key-capturing panels: borderless, centered,
/// focused (so keystrokes can't leak), closes on Escape or focus loss.</summary>
public abstract class KeyCapturePanel : Form
{
    private readonly List<string> _lines = [];
    private readonly Font _titleFont = new("Segoe UI", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
    private readonly Font _lineFont = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    private bool _finished;

    protected KeyCapturePanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        BackColor = Color.FromArgb(0x1A, 0x1A, 0x1A);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_TOOLWINDOW; // out of Alt-Tab, but still activatable
            return cp;
        }
    }

    protected void Present(IEnumerable<string> lines)
    {
        _lines.Clear();
        _lines.AddRange(lines);

        const int lineHeight = 26;
        int width = 320;
        using (var g = CreateGraphics())
        {
            foreach (string line in _lines)
                width = Math.Max(width, TextRenderer.MeasureText(g, line, _lineFont).Width + 48);
        }
        int height = _lines.Count * lineHeight + 28;

        Screen screen = Screen.FromPoint(Cursor.Position);
        Rectangle wa = screen.WorkingArea;
        Bounds = new Rectangle(wa.X + (wa.Width - width) / 2, wa.Y + (wa.Height - height) / 2, width, height);
        Show();
        Activate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            var rect = new Rectangle(20, 14 + i * 26, ClientSize.Width - 40, 26);
            TextRenderer.DrawText(e.Graphics, _lines[i], i == 0 ? _titleFont : _lineFont, rect,
                i == 0 ? Color.White : Color.FromArgb(0xD8, 0xD8, 0xD8),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (!_finished)
            Finish();
    }

    protected override void OnKeyPress(KeyPressEventArgs e) => e.Handled = true; // never leak keystrokes

    /// <summary>Closes the panel exactly once; <paramref name="deliver"/> runs after.</summary>
    protected void Complete(Action deliver)
    {
        if (_finished)
            return;
        _finished = true;
        Close();
        deliver();
    }

    /// <summary>Cancel path (Escape / focus loss); subclasses deliver null.</summary>
    protected abstract void Finish();

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _titleFont.Dispose();
        _lineFont.Dispose();
        Dispose();
    }
}

/// <summary>Lists layouts; pressing its number restores. Esc cancels.</summary>
public sealed class LayoutPickerPanel : KeyCapturePanel
{
    private readonly List<(string Name, string? Hotkey)> _names;
    private readonly Action<string?> _completion;

    public LayoutPickerPanel(IEnumerable<(string Name, string? Hotkey)> names, Action<string?> completion)
    {
        _names = names.Take(9).ToList();
        _completion = completion;
    }

    public void Present()
    {
        var lines = new List<string> { "Restore layout" };
        for (int i = 0; i < _names.Count; i++)
        {
            string hotkey = _names[i].Hotkey is { } chord ? $"   ({HotkeyChord.Pretty(chord)})" : "";
            lines.Add($"{i + 1}  {_names[i].Name}{hotkey}");
        }
        lines.Add("Esc  cancel");
        Present(lines);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.SuppressKeyPress = true;
        if (e.KeyCode == Keys.Escape)
        {
            Finish();
            return;
        }
        int n = e.KeyCode switch
        {
            >= Keys.D1 and <= Keys.D9 => e.KeyCode - Keys.D1 + 1,
            >= Keys.NumPad1 and <= Keys.NumPad9 => e.KeyCode - Keys.NumPad1 + 1,
            _ => 0,
        };
        if (n >= 1 && n <= _names.Count)
        {
            string choice = _names[n - 1].Name;
            Complete(() => _completion(choice));
        }
    }

    protected override void Finish() => Complete(() => _completion(null));
}

/// <summary>Captures one chord (must include Ctrl, Alt or Win) for a layout hotkey.</summary>
public sealed class HotkeyRecorderPanel : KeyCapturePanel
{
    private readonly string _layoutName;
    private readonly Action<string?> _completion;

    public HotkeyRecorderPanel(string layoutName, Action<string?> completion)
    {
        _layoutName = layoutName;
        _completion = completion;
    }

    public void Present() => Present([
        $"Hotkey for \"{_layoutName}\"",
        "Press the key combination to assign",
        "(must include Ctrl, Alt or Win · Esc cancels)",
    ]);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        e.SuppressKeyPress = true;
        if (e.KeyCode == Keys.Escape)
        {
            Finish();
            return;
        }
        // Ignore bare modifier presses; wait for a real key.
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return;

        uint mods = 0;
        if (e.Control) mods |= Win32.MOD_CONTROL;
        if (e.Alt) mods |= Win32.MOD_ALT;
        if (e.Shift) mods |= Win32.MOD_SHIFT;
        // WinForms has no Win-key flag; ask the OS directly.
        if ((Win32.GetAsyncKeyState(Win32.VK_LWIN) & 0x8000) != 0 || (Win32.GetAsyncKeyState(Win32.VK_RWIN) & 0x8000) != 0)
            mods |= Win32.MOD_WIN;

        // Shift alone is not enough — it would shadow ordinary typing.
        if ((mods & ~Win32.MOD_SHIFT) == 0)
            return;
        if (HotkeyChord.Format(mods, (uint)e.KeyCode) is not string chord)
            return; // unsupported key: keep listening

        Complete(() => _completion(chord));
    }

    protected override void Finish() => Complete(() => _completion(null));
}

namespace Tactile;

/// <summary>
/// Transient centered message (e.g. "No window to place"). Never activates —
/// stealing focus on a rejection would be worse than the rejection itself.
/// </summary>
public sealed class Toast : Form
{
    private readonly string _text;
    private readonly Font _font = new("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Point);

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Win32.WS_EX_NOACTIVATE | Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_TOPMOST;
            return cp;
        }
    }

    private Toast(string text, Screen screen)
    {
        _text = text;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(0x20, 0x20, 0x20);
        Opacity = 0.92;

        Size textSize = TextRenderer.MeasureText(text, _font);
        Size = new Size(textSize.Width + 36, textSize.Height + 22);
        Rectangle wa = screen.WorkingArea;
        Location = new Point(wa.X + (wa.Width - Width) / 2, wa.Y + (wa.Height - Height) / 2);

        var timer = new System.Windows.Forms.Timer { Interval = 1200 };
        timer.Tick += (_, _) =>
        {
            timer.Dispose();
            Close();
        };
        timer.Start();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(e.Graphics, _text, _font, ClientRectangle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        base.OnFormClosed(e);
        _font.Dispose();
        Dispose();
    }

    public static void Show(string text, Screen? screen = null)
        => new Toast(text, screen ?? Screen.FromPoint(Cursor.Position)).Show();
}

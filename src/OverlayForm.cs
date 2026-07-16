namespace Tactile;

/// <summary>
/// The grid overlay: borderless, topmost, semi-transparent form covering the
/// target monitor's work area. It owns keyboard focus while open, so cell
/// letters never leak to the app underneath.
/// </summary>
public sealed class OverlayForm : Form
{
    private readonly Config _cfg;
    private readonly Rectangle _workArea;
    private GridCell? _anchor;
    private bool _sessionActive = true;

    /// <summary>Two cells chosen — place the window spanning them.</summary>
    public event Action<GridCell, GridCell>? PlaceRequested;

    /// <summary>Overlay dismissed; bool = whether to restore focus to the target.</summary>
    public event Action<bool>? Cancelled;

    public OverlayForm(Config cfg, Rectangle workArea)
    {
        _cfg = cfg;
        _workArea = workArea;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        // We lay out in physical pixels ourselves; block WinForms auto-scaling.
        AutoScaleMode = AutoScaleMode.None;
        KeyPreview = true;
        BackColor = Config.ParseColor(cfg.OverlayBgColor);
        // Set alpha before Show so the overlay never flashes opaque.
        Opacity = cfg.OverlayAlpha / 255.0;
        Bounds = workArea;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // Tool window: stays out of Alt-Tab. Must remain activatable
            // (no WS_EX_NOACTIVATE) — the overlay needs keyboard focus.
            cp.ExStyle |= Win32.WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // PerMonitorV2 may rescale a form whose handle was created at another
        // monitor's DPI; re-assert the intended physical bounds.
        Win32.SetWindowPos(Handle, Win32.HWND_TOPMOST,
            _workArea.X, _workArea.Y, _workArea.Width, _workArea.Height, 0);
        Activate(); // works here because we're inside a WM_HOTKEY interaction
    }

    // ------------------------------ Input -----------------------------------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Escape)
        {
            e.SuppressKeyPress = true;
            Cancel(restoreFocus: true);
        }
        else if (e.KeyCode is Keys.Enter or Keys.Return && _anchor is GridCell a)
        {
            e.SuppressKeyPress = true;
            FinishPlace(a, a);
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        e.Handled = true; // swallow everything: unknown keys are ignored without a beep
        if (!_sessionActive)
            return;
        if (!_cfg.KeyToCell.TryGetValue(char.ToLowerInvariant(e.KeyChar), out GridCell cell))
            return;

        if (_anchor is GridCell anchor)
            FinishPlace(anchor, cell);
        else
        {
            _anchor = cell;
            Invalidate(); // repaint with the anchor highlighted
        }
    }

    /// <summary>Called by TrayApp when the hotkey is pressed while open (toggle).</summary>
    public void CancelExternally() => Cancel(restoreFocus: true);

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        // Focus loss (alt-tab, click elsewhere) cancels WITHOUT stealing focus
        // back — the user chose another window. Our own teardown also fires
        // Deactivate; _sessionActive is already false then, so this is a no-op.
        if (_sessionActive)
            Cancel(restoreFocus: false);
    }

    private void FinishPlace(GridCell a, GridCell b)
    {
        if (!_sessionActive)
            return;
        _sessionActive = false;
        Close();
        PlaceRequested?.Invoke(a, b);
    }

    private void Cancel(bool restoreFocus)
    {
        if (!_sessionActive)
            return;
        _sessionActive = false;
        Close();
        Cancelled?.Invoke(restoreFocus);
    }

    // ------------------------------ Painting --------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        // Everything derives from ClientSize so painting always matches the
        // actual window size, and CellEdges shares PlacementRect's rounding.
        var (xs, ys) = GridGeometry.CellEdges(_cfg, ClientSize);
        Graphics g = e.Graphics;

        using var cellBrush = new SolidBrush(Config.ParseColor(_cfg.CellBgColor));
        using var anchorBrush = new SolidBrush(Config.ParseColor(_cfg.HighlightBgColor));
        using var gridPen = new Pen(BackColor, 2); // background color acts as grid lines
        Color letterColor = Config.ParseColor(_cfg.CellTextColor);
        Color anchorLetterColor = Config.ParseColor(_cfg.HighlightTextColor);

        int cellH = ys[1] - ys[0];
        // GraphicsUnit.Pixel sidesteps all point/DPI conversion.
        using var letterFont = new Font(_cfg.FontName, Math.Max(8f, (float)(cellH * _cfg.FontScale)), FontStyle.Regular, GraphicsUnit.Pixel);

        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

        for (int r = 0; r < _cfg.GridRows; r++)
        {
            for (int c = 0; c < _cfg.GridCols; c++)
            {
                var rect = Rectangle.FromLTRB(xs[c], ys[r], xs[c + 1], ys[r + 1]);
                bool isAnchor = _anchor is GridCell a && a.Col == c && a.Row == r;
                g.FillRectangle(isAnchor ? anchorBrush : cellBrush, rect);
                g.DrawRectangle(gridPen, rect);
                TextRenderer.DrawText(g, _cfg.CellHints[r][c], letterFont, rect,
                    isAnchor ? anchorLetterColor : letterColor, flags);
            }
        }

        // Hint line pinned to the bottom edge.
        using var hintFont = new Font(_cfg.FontName, 14f, FontStyle.Regular, GraphicsUnit.Pixel);
        var hintRect = new Rectangle(0, ClientSize.Height - 22, ClientSize.Width, 20);
        TextRenderer.DrawText(g, _cfg.HintLineText, hintFont, hintRect,
            Config.ParseColor(_cfg.HintTextColor), flags);
    }
}

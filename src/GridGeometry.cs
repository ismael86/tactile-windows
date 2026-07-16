namespace Tactile;

/// <summary>
/// Pure placement math, ported verbatim from tactile.ahk / tactile-macos.
///
/// Margin scheme: the grid area is inset GridMarginPx from every work-area
/// edge. On internal edges (between cells) each placed window pulls in
/// GridMarginPx/2, so two adjacent placements end up separated by exactly
/// GridMarginPx while outer edges keep the full margin. Edges are computed as
/// doubles and rounded LAST — width is Round(right) - Round(left), never
/// Round(right - left) — so neighboring placements share the same rounded
/// boundary: no overlaps, no seams.
/// </summary>
public static class GridGeometry
{
    private static int R(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    /// <summary>Rectangle (physical px) spanned by cells a..b inclusive within the work area.</summary>
    public static Rectangle PlacementRect(Config cfg, GridCell a, GridCell b, Rectangle work)
    {
        double m = cfg.GridMarginPx;
        double cellW = (work.Width - 2 * m) / cfg.GridCols;
        double cellH = (work.Height - 2 * m) / cfg.GridRows;

        int cMin = Math.Min(a.Col, b.Col), cMax = Math.Max(a.Col, b.Col);
        int rMin = Math.Min(a.Row, b.Row), rMax = Math.Max(a.Row, b.Row);

        double leftF = m + cMin * cellW + (cMin > 0 ? m / 2 : 0);
        double rightF = m + (cMax + 1) * cellW - (cMax < cfg.GridCols - 1 ? m / 2 : 0);
        double topF = m + rMin * cellH + (rMin > 0 ? m / 2 : 0);
        double botF = m + (rMax + 1) * cellH - (rMax < cfg.GridRows - 1 ? m / 2 : 0);

        return new Rectangle(
            work.X + R(leftF),
            work.Y + R(topF),
            R(rightF) - R(leftF),
            R(botF) - R(topF));
    }

    /// <summary>Rounded cell edge coordinates for painting the overlay, using the
    /// same rounding as PlacementRect so the drawn grid matches placement exactly.</summary>
    public static (int[] Xs, int[] Ys) CellEdges(Config cfg, Size client)
    {
        double m = cfg.GridMarginPx;
        double cellW = (client.Width - 2 * m) / cfg.GridCols;
        double cellH = (client.Height - 2 * m) / cfg.GridRows;

        var xs = new int[cfg.GridCols + 1];
        var ys = new int[cfg.GridRows + 1];
        for (int i = 0; i <= cfg.GridCols; i++)
            xs[i] = R(m + i * cellW);
        for (int i = 0; i <= cfg.GridRows; i++)
            ys[i] = R(m + i * cellH);
        return (xs, ys);
    }
}

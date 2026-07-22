using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactile;

// ============================================================================
// Saved layouts: named snapshots of which apps' windows sit in which grid
// cells, persisted as diff-friendly JSON and restorable in one action.
// Mirrors the macOS port's Layouts.swift. Reuses (never duplicates)
// GridGeometry for cell math, WindowPlacer for placement, and Toast for
// notifications.
// ============================================================================

// ------------------------------ Data model ----------------------------------

public class LayoutFile
{
    public int Version { get; set; } = 1;
    // SortedDictionary keeps the on-disk key order stable and diff-friendly.
    public SortedDictionary<string, Layout> Layouts { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class Layout
{
    public GridSize Grid { get; set; } = new();
    public List<WindowEntry> Windows { get; set; } = [];
    public string SavedAt { get; set; } = "";
    public ScreenInfo Screen { get; set; } = new();
    /// <summary>User-assigned restore chord (e.g. "ctrl+alt+1"), null when unassigned.</summary>
    public string? Hotkey { get; set; }
}

public class GridSize
{
    public int Cols { get; set; }
    public int Rows { get; set; }
    public bool Matches(GridSize o) => Cols == o.Cols && Rows == o.Rows;
}

public class WindowEntry
{
    /// <summary>Process executable name, lowercase (e.g. "code.exe").</summary>
    public string App { get; set; } = "";
    /// <summary>Friendly name for messages and as a last-resort match.</summary>
    public string AppNameFallback { get; set; } = "";
    public string? TitleHint { get; set; }
    public CellSpan Cells { get; set; } = new();
    /// <summary>Restore order, lowest first; highest ends up frontmost.</summary>
    public int Order { get; set; }
}

public class CellSpan
{
    public int Col1 { get; set; }
    public int Row1 { get; set; }
    public int Col2 { get; set; }
    public int Row2 { get; set; }
}

public class ScreenInfo
{
    public string Note { get; set; } = "informational only";
    public int W { get; set; }
    public int H { get; set; }
}

// ------------------------------- Store ---------------------------------------

/// <summary>Reads/writes layouts.json next to the executable.</summary>
public static class LayoutStore
{
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "layouts.json");

    private static bool _notifiedCorrupt;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>Always re-reads from disk (the user may edit the file by hand
    /// while the app runs). Missing file -> empty. Corrupt file -> backed up to
    /// layouts.json.bak, reported once, treated as empty. Never throws, never
    /// silently overwrites.</summary>
    public static LayoutFile Load(Action<string> notify)
    {
        string path = FilePath;
        if (!File.Exists(path))
            return new LayoutFile();
        try
        {
            return JsonSerializer.Deserialize<LayoutFile>(File.ReadAllText(path), JsonOpts) ?? new LayoutFile();
        }
        catch
        {
            try
            {
                string bak = path + ".bak";
                File.Delete(bak);
                File.Move(path, bak);
            }
            catch
            {
                // Backup is best-effort; never block the app on it.
            }
            if (!_notifiedCorrupt)
            {
                _notifiedCorrupt = true;
                notify("layouts.json was unreadable — backed up to layouts.json.bak");
            }
            return new LayoutFile();
        }
    }

    /// <summary>One pretty-printed, atomic write per mutation.</summary>
    public static void Write(LayoutFile file)
    {
        string path = FilePath;
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(file, JsonOpts));
        File.Move(tmp, path, overwrite: true);
    }
}

// --------------------------- Grid cell snapping ------------------------------

public static class LayoutMath
{
    /// <summary>Best-fit grid cells for a window rect: snap each edge to the
    /// nearest cell boundary; exact ties round outward so windows never shrink
    /// below intent. Windows placed via the grid round-trip exactly;
    /// hand-dragged windows get normalized to the grid (intended).</summary>
    public static CellSpan SnapToCells(Config cfg, Rectangle frame, Rectangle work)
    {
        double m = cfg.GridMarginPx;
        double cellW = (work.Width - 2 * m) / cfg.GridCols;
        double cellH = (work.Height - 2 * m) / cfg.GridRows;

        static int NearestEdge(double v, double origin, double m, double cell, int count, bool tieUp)
        {
            if (cell <= 0)
                return 0;
            double rel = (v - origin - m) / cell;
            double lower = Math.Floor(rel);
            double fraction = rel - lower;
            double idx = fraction < 0.5 ? lower
                : fraction > 0.5 ? lower + 1
                : tieUp ? lower + 1 : lower; // ties round outward
            return Math.Max(0, Math.Min(count, (int)idx));
        }

        int col1 = Math.Min(cfg.GridCols - 1, NearestEdge(frame.Left, work.Left, m, cellW, cfg.GridCols, false));
        int row1 = Math.Min(cfg.GridRows - 1, NearestEdge(frame.Top, work.Top, m, cellH, cfg.GridRows, false));
        int col2 = Math.Max(col1, NearestEdge(frame.Right, work.Left, m, cellW, cfg.GridCols, true) - 1);
        int row2 = Math.Max(row1, NearestEdge(frame.Bottom, work.Top, m, cellH, cfg.GridRows, true) - 1);
        return new CellSpan { Col1 = col1, Row1 = row1, Col2 = col2, Row2 = row2 };
    }

    /// <summary>Proportionally rescale a span saved on one grid onto another,
    /// rounding outward so windows don't shrink below intent.</summary>
    public static CellSpan Scale(CellSpan span, GridSize old, GridSize now)
    {
        if (old.Matches(now) || old.Cols <= 0 || old.Rows <= 0)
            return span;
        double sx = (double)now.Cols / old.Cols;
        double sy = (double)now.Rows / old.Rows;
        int col1 = (int)Math.Floor(span.Col1 * sx);
        int col2 = (int)Math.Ceiling((span.Col2 + 1) * sx) - 1;
        int row1 = (int)Math.Floor(span.Row1 * sy);
        int row2 = (int)Math.Ceiling((span.Row2 + 1) * sy) - 1;
        int Clamp(int v, int max) => Math.Max(0, Math.Min(max - 1, v));
        col1 = Clamp(col1, now.Cols);
        row1 = Clamp(row1, now.Rows);
        return new CellSpan
        {
            Col1 = col1,
            Row1 = row1,
            Col2 = Clamp(Math.Max(col1, col2), now.Cols),
            Row2 = Clamp(Math.Max(row1, row2), now.Rows),
        };
    }
}

// -------------------------- Window enumeration -------------------------------

/// <summary>A placeable top-level window with its z position.</summary>
public sealed class CandidateWindow
{
    public required IntPtr Hwnd { get; init; }
    public required Rectangle VisibleBounds { get; init; }
    /// <summary>0 = frontmost.</summary>
    public required int ZIndex { get; init; }
    public required string Title { get; init; }
    /// <summary>Executable name, lowercase.</summary>
    public required string AppId { get; init; }
    public required string AppName { get; init; }
}

public static class WindowEnumerator
{
    /// <summary>Every window Alt-Tab would show, front-to-back, excluding our own.
    /// EnumWindows yields z-order; the filters below are the standard
    /// "is this a real app window" tests.</summary>
    public static List<CandidateWindow> OnScreenWindows()
    {
        var result = new List<CandidateWindow>();
        uint ownPid = (uint)Environment.ProcessId;

        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd) || Win32.IsIconic(hwnd))
                return true;
            // Owned windows (dialogs, tool palettes) are not top-level app windows.
            if (Win32.GetAncestor(hwnd, Win32.GA_ROOTOWNER) != hwnd)
                return true;
            if (((long)Win32.GetWindowLongPtrW(hwnd, Win32.GWL_EXSTYLE) & Win32.WS_EX_TOOLWINDOW) != 0)
                return true;
            // Cloaked: on another virtual desktop, or a suspended UWP shell window.
            if (Win32.DwmGetWindowAttributeInt(hwnd, Win32.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            string title = Win32.GetWindowTitle(hwnd);
            if (title.Length == 0)
                return true;

            string cls = Win32.GetWindowClass(hwnd);
            if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Windows.UI.Core.CoreWindow")
                return true;

            Win32.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == ownPid)
                return true;

            Rectangle bounds = WindowPlacer.GetVisibleBounds(hwnd) ?? Rectangle.Empty;
            if (bounds.Width <= 1 || bounds.Height <= 1)
                return true;

            (string appId, string appName) = AppIdentity(pid);
            result.Add(new CandidateWindow
            {
                Hwnd = hwnd,
                VisibleBounds = bounds,
                ZIndex = result.Count,
                Title = title,
                AppId = appId,
                AppName = appName,
            });
            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static readonly Dictionary<uint, (string Id, string Name)> IdentityCache = [];

    /// <summary>(executable name, friendly name) for a process. The friendly
    /// name comes from the exe's version info when readable.</summary>
    private static (string, string) AppIdentity(uint pid)
    {
        if (IdentityCache.TryGetValue(pid, out var cached))
            return cached;

        string id = "unknown", name = "unknown";
        try
        {
            string? path = Win32.GetProcessImagePath(pid);
            if (path is not null)
            {
                id = Path.GetFileName(path).ToLowerInvariant();
                name = Path.GetFileNameWithoutExtension(path);
                try
                {
                    string? desc = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(desc))
                        name = desc;
                }
                catch
                {
                    // Version info is optional.
                }
            }
            else
            {
                // Access denied (elevated/protected): process name still works.
                using var proc = Process.GetProcessById((int)pid);
                id = proc.ProcessName.ToLowerInvariant() + ".exe";
                name = proc.ProcessName;
            }
        }
        catch
        {
            // Process vanished mid-enumeration; keep the "unknown" placeholders.
        }

        var identity = (id, name);
        IdentityCache[pid] = identity;
        return identity;
    }

    public static void ClearIdentityCache() => IdentityCache.Clear();
}

// ----------------------------- Capture / restore -----------------------------

public static class LayoutEngine
{
    /// <summary>Snapshot the windows on <paramref name="screen"/> as layout
    /// entries. Returns an empty list when nothing is eligible.</summary>
    public static List<WindowEntry> Capture(Config cfg, Screen screen)
    {
        WindowEnumerator.ClearIdentityCache();
        var onTarget = WindowEnumerator.OnScreenWindows()
            .Where(w => Screen.FromRectangle(w.VisibleBounds).DeviceName == screen.DeviceName)
            .ToList();
        if (onTarget.Count == 0)
            return [];

        // titleHint only helps when the same app has differing, unique titles.
        var titlesPerApp = onTarget.GroupBy(w => w.AppId)
            .ToDictionary(g => g.Key, g => g.Select(w => w.Title).ToList());
        string? Hint(CandidateWindow w)
        {
            var titles = titlesPerApp[w.AppId];
            if (titles.Count <= 1 || string.IsNullOrEmpty(w.Title))
                return null;
            return titles.Count(t => t == w.Title) == 1 ? w.Title : null;
        }

        // Front-to-back list -> backmost gets order 0, frontmost the highest.
        var backToFront = Enumerable.Reverse(onTarget).ToList();
        Rectangle work = screen.WorkingArea;
        return backToFront.Select((w, order) => new WindowEntry
        {
            App = w.AppId,
            AppNameFallback = w.AppName,
            TitleHint = Hint(w),
            Cells = LayoutMath.SnapToCells(cfg, w.VisibleBounds, work),
            Order = order,
        }).ToList();
    }

    public sealed class RestoreResult
    {
        public int Placed;
        public int Total;
        public List<string> MissingApps = [];
        public string? GridNote;

        public string Summary
        {
            get
            {
                string msg = $"Restored {Placed}/{Total}";
                if (MissingApps.Count > 0)
                    msg += " — no window for: " + string.Join(", ", MissingApps.Distinct());
                if (GridNote is not null)
                    msg += $" ({GridNote})";
                return msg;
            }
        }
    }

    /// <summary>Apply a layout on <paramref name="screen"/>. Matching priority
    /// per entry: app id + title hint -> app id, any window -> app display name.
    /// Each real window satisfies at most one entry; missing apps are skipped
    /// and reported — nothing is launched. Placement goes through the same
    /// GridGeometry + WindowPlacer routine as manual grid placement, so results
    /// are pixel-identical and restoring twice moves nothing.</summary>
    public static RestoreResult Restore(Config cfg, Layout layout, Screen screen)
    {
        var result = new RestoreResult();
        var current = new GridSize { Cols = cfg.GridCols, Rows = cfg.GridRows };
        if (!layout.Grid.Matches(current))
            result.GridNote = $"layout saved on {layout.Grid.Cols}x{layout.Grid.Rows}, applied to {current.Cols}x{current.Rows}";

        var entries = layout.Windows.OrderBy(w => w.Order).ToList();
        result.Total = entries.Count;

        WindowEnumerator.ClearIdentityCache();
        var candidates = WindowEnumerator.OnScreenWindows();
        var claimed = new HashSet<IntPtr>();
        var assignments = new List<(WindowEntry Entry, CandidateWindow Candidate)>();

        CandidateWindow? Take(Func<CandidateWindow, bool> predicate)
        {
            var match = candidates.FirstOrDefault(c => !claimed.Contains(c.Hwnd) && predicate(c));
            if (match is not null)
                claimed.Add(match.Hwnd);
            return match;
        }

        // Round 1: entries with a title hint claim their specific window first,
        // so a hint-less entry of the same app can't steal it.
        var pending = new List<WindowEntry>();
        foreach (var entry in entries)
        {
            var match = entry.TitleHint is { Length: > 0 } hint
                ? Take(c => c.AppId == entry.App && c.Title.Contains(hint, StringComparison.OrdinalIgnoreCase))
                : null;
            if (match is not null)
                assignments.Add((entry, match));
            else
                pending.Add(entry);
        }
        // Round 2: exact app id, any unclaimed window.
        var stillPending = new List<WindowEntry>();
        foreach (var entry in pending)
        {
            var match = Take(c => c.AppId == entry.App);
            if (match is not null)
                assignments.Add((entry, match));
            else
                stillPending.Add(entry);
        }
        // Round 3: display-name fallback.
        foreach (var entry in stillPending)
        {
            var match = Take(c => c.AppName == entry.AppNameFallback);
            if (match is not null)
                assignments.Add((entry, match));
            else
                result.MissingApps.Add(entry.AppNameFallback);
        }

        // Place in order sequence, raising as we go: highest order ends frontmost.
        Rectangle work = screen.WorkingArea;
        IntPtr frontmost = IntPtr.Zero;
        foreach (var (entry, candidate) in assignments.OrderBy(a => a.Entry.Order))
        {
            CellSpan span = LayoutMath.Scale(entry.Cells, layout.Grid, current);
            Rectangle rect = GridGeometry.PlacementRect(cfg,
                new GridCell(span.Col1, span.Row1), new GridCell(span.Col2, span.Row2), work);
            WindowPlacer.Place(candidate.Hwnd, rect, activate: false);
            // Raise without activating; the final SetForegroundWindow below
            // decides focus, avoiding a storm of foreground changes.
            Win32.SetWindowPos(candidate.Hwnd, Win32.HWND_TOP, 0, 0, 0, 0,
                Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
            frontmost = candidate.Hwnd;
            result.Placed++;
        }
        if (frontmost != IntPtr.Zero)
            Win32.SetForegroundWindow(frontmost);

        return result;
    }
}

// ------------------------- Hotkey string handling ----------------------------

/// <summary>Human-editable chord strings for layouts.json, e.g. "ctrl+alt+1".</summary>
public static class HotkeyChord
{
    /// <summary>"ctrl+alt+1" -> (modifiers, vk), or null if unparseable.</summary>
    public static (uint Modifiers, uint Vk)? Parse(string chord)
    {
        var parts = chord.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return null;

        uint mods = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i])
            {
                case "win": mods |= Win32.MOD_WIN; break;
                case "ctrl" or "control": mods |= Win32.MOD_CONTROL; break;
                case "alt": mods |= Win32.MOD_ALT; break;
                case "shift": mods |= Win32.MOD_SHIFT; break;
                default: return null;
            }
        }
        if (mods == 0)
            return null;

        string key = parts[^1];
        uint vk;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            vk = char.ToUpperInvariant(key[0]);
        else if (Enum.TryParse<Keys>(key, true, out var parsed))
            vk = (uint)parsed;
        else
            return null;
        return (mods, vk);
    }

    /// <summary>Canonical string for a captured chord, or null for unknown keys.</summary>
    public static string? Format(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & Win32.MOD_WIN) != 0) parts.Add("win");
        if ((modifiers & Win32.MOD_CONTROL) != 0) parts.Add("ctrl");
        if ((modifiers & Win32.MOD_ALT) != 0) parts.Add("alt");
        if ((modifiers & Win32.MOD_SHIFT) != 0) parts.Add("shift");
        if (parts.Count == 0)
            return null;

        var key = (Keys)vk;
        string name = key switch
        {
            >= Keys.A and <= Keys.Z => key.ToString().ToLowerInvariant(),
            >= Keys.D0 and <= Keys.D9 => ((char)('0' + (vk - (uint)Keys.D0))).ToString(),
            >= Keys.F1 and <= Keys.F12 => key.ToString().ToLowerInvariant(),
            _ => "",
        };
        if (name.Length == 0)
            return null;
        parts.Add(name);
        return string.Join("+", parts);
    }

    /// <summary>Display form: "ctrl+alt+1" -> "Ctrl+Alt+1".</summary>
    public static string Pretty(string chord) =>
        string.Join("+", chord.Split('+', StringSplitOptions.RemoveEmptyEntries).Select(p => p switch
        {
            "win" => "Win",
            "ctrl" or "control" => "Ctrl",
            "alt" => "Alt",
            "shift" => "Shift",
            _ => p.ToUpperInvariant(),
        }));
}

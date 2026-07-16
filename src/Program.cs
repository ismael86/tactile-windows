namespace Tactile;

static class Program
{
    // Held for the process lifetime so the mutex is never GC-released.
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(true, "TactileWindows_SingleInstance", out bool createdNew);
        if (!createdNew)
            return; // another instance is already running

        // Sets PerMonitorV2 DPI awareness (from the csproj) — all coordinates
        // are physical pixels from here on. Must run before any window exists.
        ApplicationConfiguration.Initialize();

        string configPath = Path.Combine(AppContext.BaseDirectory, "tactile.json");
        Config cfg;
        try
        {
            cfg = Config.LoadOrCreate(configPath);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Tactile configuration error:\n\n" + ex.Message,
                "Tactile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (args.Contains("--print-geometry"))
        {
            PrintGeometry(cfg); // visible when stdout is redirected/piped
            return;
        }

        Application.Run(new TrayApp(cfg, configPath));
    }

    /// <summary>Debug aid mirroring the macOS port: dump canonical placement
    /// rects for the primary monitor's work area and exit.</summary>
    private static void PrintGeometry(Config cfg)
    {
        Rectangle work = Screen.PrimaryScreen!.WorkingArea;
        Console.WriteLine($"Primary work area: {work}");

        void Print(string label, GridCell a, GridCell b)
            => Console.WriteLine($"{label,-28} {GridGeometry.PlacementRect(cfg, a, b, work)}");

        int lastCol = cfg.GridCols - 1, lastRow = cfg.GridRows - 1;
        Print("Full grid", new GridCell(0, 0), new GridCell(lastCol, lastRow));
        Print("Left half", new GridCell(0, 0), new GridCell(cfg.GridCols / 2 - 1, lastRow));
        Print("Right half", new GridCell(cfg.GridCols / 2, 0), new GridCell(lastCol, lastRow));
        Print("Top-left quadrant (Q-F)", new GridCell(0, 0), new GridCell(3, 1));
        Print("Single cell (0,0)", new GridCell(0, 0), new GridCell(0, 0));
        Print("Single cell (last,last)", new GridCell(lastCol, lastRow), new GridCell(lastCol, lastRow));
    }
}

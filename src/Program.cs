namespace Tactile;

static class Program
{
    // Held for the process lifetime so the mutex is never GC-released.
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    static void Main(string[] args)
    {
        bool isCli = args.Length > 0;

        // The mutex guards the tray app only; CLI commands are one-shot and may
        // run while the tray app is up.
        if (!isCli)
        {
            _singleInstanceMutex = new Mutex(true, "TactileWindows_SingleInstance", out bool createdNew);
            if (!createdNew)
                return; // another instance is already running
        }

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
            if (isCli)
            {
                Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
                Console.Error.WriteLine("Tactile configuration error: " + ex.Message);
            }
            else
            {
                MessageBox.Show("Tactile configuration error:\n\n" + ex.Message,
                    "Tactile", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return;
        }

        if (isCli)
        {
            Win32.AttachConsole(Win32.ATTACH_PARENT_PROCESS);
            RunCli(cfg, args);
            return;
        }

        Application.Run(new TrayApp(cfg, configPath));
    }

    /// <summary>Headless layout commands, mirroring the macOS port's CLI. Unlike
    /// macOS (where the CLI must hand work to the running app for permission
    /// reasons), Windows lets any process enumerate and move windows, so these
    /// run standalone.</summary>
    private static void RunCli(Config cfg, string[] args)
    {
        string command = args[0];
        string? name = args.Length > 1 ? args[1] : null;

        switch (command)
        {
            case "--print-geometry":
                PrintGeometry(cfg);
                break;

            case "--list-layouts":
            {
                var file = LayoutStore.Load(Console.Error.WriteLine);
                if (file.Layouts.Count == 0)
                {
                    Console.WriteLine("(no saved layouts)");
                    break;
                }
                foreach (var (layoutName, layout) in file.Layouts)
                {
                    string hotkey = layout.Hotkey is { } chord ? $"  hotkey={chord}" : "";
                    Console.WriteLine($"{layoutName}  ({layout.Windows.Count} windows, grid {layout.Grid.Cols}x{layout.Grid.Rows}){hotkey}");
                }
                break;
            }

            case "--save-layout":
            {
                if (name is null)
                {
                    Console.Error.WriteLine("error: missing layout name");
                    break;
                }
                Screen screen = CurrentScreen();
                var entries = LayoutEngine.Capture(cfg, screen);
                if (entries.Count == 0)
                {
                    Console.WriteLine("No windows to save");
                    break;
                }
                var file = LayoutStore.Load(Console.Error.WriteLine);
                file.Layouts.TryGetValue(name, out Layout? existing);
                file.Layouts[name] = new Layout
                {
                    Grid = new GridSize { Cols = cfg.GridCols, Rows = cfg.GridRows },
                    Windows = entries,
                    SavedAt = DateTime.UtcNow.ToString("o"),
                    Screen = new ScreenInfo { W = screen.Bounds.Width, H = screen.Bounds.Height },
                    Hotkey = existing?.Hotkey,
                };
                LayoutStore.Write(file);
                Console.WriteLine($"Saved layout \"{name}\" ({entries.Count} windows)");
                break;
            }

            case "--restore-layout":
            {
                if (name is null)
                {
                    Console.Error.WriteLine("error: missing layout name");
                    break;
                }
                var file = LayoutStore.Load(Console.Error.WriteLine);
                if (!file.Layouts.TryGetValue(name, out Layout? layout))
                {
                    Console.WriteLine($"No layout named \"{name}\"");
                    break;
                }
                Console.WriteLine(LayoutEngine.Restore(cfg, layout, CurrentScreen()).Summary);
                // Give the verify-and-reapply timers time to run before exiting.
                Application.DoEvents();
                Thread.Sleep(600);
                Application.DoEvents();
                break;
            }

            case "--list-windows":
            {
                foreach (var w in WindowEnumerator.OnScreenWindows())
                    Console.WriteLine($"z={w.ZIndex,-3} {w.AppId,-20} {w.VisibleBounds,-40} {w.Title}");
                break;
            }

            default:
                Console.Error.WriteLine($"Unknown option \"{command}\". Valid: --list-layouts, --save-layout NAME, " +
                    "--restore-layout NAME, --list-windows, --print-geometry");
                break;
        }
    }

    private static Screen CurrentScreen()
    {
        IntPtr focused = Win32.GetForegroundWindow();
        return focused != IntPtr.Zero ? Screen.FromHandle(focused) : Screen.PrimaryScreen ?? Screen.AllScreens[0];
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

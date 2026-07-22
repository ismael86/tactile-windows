namespace Tactile;

/// <summary>
/// Orchestrates the saved-layouts feature: save/restore/delete flows,
/// per-layout restore hotkeys, and the picker. The layouts file is re-read
/// before every operation, so hand edits while the app runs are respected.
/// Mirrors the macOS port's LayoutManager.swift.
/// </summary>
public sealed class LayoutManager(HotkeyManager hotkeys, Func<Config> currentConfig)
{
    private readonly List<int> _layoutHotkeyIds = [];
    private LayoutPickerPanel? _picker;
    private HotkeyRecorderPanel? _recorder;

    private static void Notify(string message) => Toast.Show(message);

    private Config Cfg => currentConfig();

    /// <summary>Same screen scoping as the grid overlay: the focused window's monitor.</summary>
    private static Screen TargetScreen()
    {
        IntPtr focused = Win32.GetForegroundWindow();
        return focused != IntPtr.Zero ? Screen.FromHandle(focused) : Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    public List<(string Name, string? Hotkey)> LayoutNames()
    {
        var file = LayoutStore.Load(Notify);
        return file.Layouts.Select(kv => (kv.Key, kv.Value.Hotkey)).ToList();
    }

    // ------------------------------- Save -----------------------------------

    public void SaveFlow()
    {
        Screen screen = TargetScreen();
        var entries = LayoutEngine.Capture(Cfg, screen);
        if (entries.Count == 0)
        {
            Notify("No windows to save");
            return;
        }

        var file = LayoutStore.Load(Notify);
        string? name = LayoutsUI.PromptForName(file.Layouts.Keys);
        if (name is null)
            return; // cancelled (also covers declining the overwrite confirm)

        file = LayoutStore.Load(Notify); // reload: the prompt may have been up a while
        file.Layouts.TryGetValue(name, out Layout? existing);
        file.Layouts[name] = new Layout
        {
            Grid = new GridSize { Cols = Cfg.GridCols, Rows = Cfg.GridRows },
            Windows = entries,
            SavedAt = DateTime.UtcNow.ToString("o"),
            Screen = new ScreenInfo { W = screen.Bounds.Width, H = screen.Bounds.Height },
            Hotkey = existing?.Hotkey, // keep any chord already assigned to this name
        };

        try
        {
            LayoutStore.Write(file);
            Notify($"Saved layout \"{name}\" ({entries.Count} windows)");
            RefreshLayoutHotkeys();
        }
        catch (Exception ex)
        {
            Notify("Could not write layouts.json: " + ex.Message);
        }
    }

    // ------------------------------ Restore ---------------------------------

    public void Restore(string name)
    {
        var file = LayoutStore.Load(Notify);
        if (!file.Layouts.TryGetValue(name, out Layout? layout))
        {
            Notify($"No layout named \"{name}\"");
            return;
        }
        var result = LayoutEngine.Restore(Cfg, layout, TargetScreen());
        Notify($"{result.Summary} · {name}");
    }

    public void ShowPicker()
    {
        var names = LayoutNames();
        if (names.Count == 0)
        {
            Notify("No saved layouts");
            return;
        }
        _picker = new LayoutPickerPanel(names, choice =>
        {
            _picker = null;
            if (choice is not null)
                Restore(choice);
        });
        _picker.Present();
    }

    // --------------------------- List / delete ------------------------------

    public void Delete(string name)
    {
        var answer = MessageBox.Show($"Delete the saved layout \"{name}\"?", "Tactile",
            MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
        if (answer != DialogResult.OK)
            return;

        var file = LayoutStore.Load(Notify);
        if (!file.Layouts.Remove(name))
            return;
        try
        {
            LayoutStore.Write(file);
            Notify($"Deleted layout \"{name}\"");
            RefreshLayoutHotkeys();
        }
        catch (Exception ex)
        {
            Notify("Could not write layouts.json: " + ex.Message);
        }
    }

    // ------------------------ Per-layout hotkeys ----------------------------

    public void AssignHotkeyFlow(string name)
    {
        _recorder = new HotkeyRecorderPanel(name, chord =>
        {
            _recorder = null;
            if (chord is not null)
                Assign(chord, name);
        });
        _recorder.Present();
    }

    private void Assign(string chord, string name)
    {
        var file = LayoutStore.Load(Notify);
        if (!file.Layouts.ContainsKey(name))
            return;

        // Reject duplicates at assignment time: other layouts and built-ins.
        var taken = file.Layouts.FirstOrDefault(kv => kv.Key != name && kv.Value.Hotkey == chord);
        if (taken.Key is not null)
        {
            Notify($"{HotkeyChord.Pretty(chord)} is already used by \"{taken.Key}\"");
            return;
        }
        if (HotkeyChord.Parse(chord) is not (uint chordMods, uint chordVk))
        {
            Notify("Could not use that key combination");
            return;
        }
        Config cfg = Cfg;
        foreach (var builtin in new[] { cfg.Hotkey, cfg.SaveLayoutHotkey, cfg.LayoutPickerHotkey })
        {
            var (mods, vk, display) = Config.ParseHotkey(builtin);
            if (mods == chordMods && vk == chordVk)
            {
                Notify($"{display} is already used by Tactile itself");
                return;
            }
        }

        file.Layouts[name].Hotkey = chord;
        try
        {
            LayoutStore.Write(file);
            Notify($"{HotkeyChord.Pretty(chord)} restores \"{name}\"");
            RefreshLayoutHotkeys();
        }
        catch (Exception ex)
        {
            Notify("Could not write layouts.json: " + ex.Message);
        }
    }

    public void RemoveHotkey(string name)
    {
        var file = LayoutStore.Load(Notify);
        if (!file.Layouts.TryGetValue(name, out Layout? layout) || layout.Hotkey is null)
            return;
        layout.Hotkey = null;
        try
        {
            LayoutStore.Write(file);
            RefreshLayoutHotkeys();
        }
        catch (Exception ex)
        {
            Notify("Could not write layouts.json: " + ex.Message);
        }
    }

    /// <summary>Re-registers all per-layout hotkeys from the file. Called at
    /// startup and after every mutation.</summary>
    public void RefreshLayoutHotkeys()
    {
        foreach (int id in _layoutHotkeyIds)
            hotkeys.Unregister(id);
        _layoutHotkeyIds.Clear();

        foreach (var (name, layout) in LayoutStore.Load(Notify).Layouts)
        {
            if (layout.Hotkey is not string chord || HotkeyChord.Parse(chord) is not (uint mods, uint vk))
                continue;
            string captured = name;
            int id = hotkeys.Register(mods, vk, () => Restore(captured));
            if (id != 0)
                _layoutHotkeyIds.Add(id);
        }
    }
}

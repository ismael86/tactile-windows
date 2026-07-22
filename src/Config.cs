using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tactile;

/// <summary>A grid position; 0-based column/row.</summary>
public readonly record struct GridCell(int Col, int Row);

public class HotkeySpec
{
    public string[] Modifiers { get; set; } = ["Win"];
    public string Key { get; set; } = "T";
}

/// <summary>
/// User configuration, persisted as tactile.json next to the executable.
/// Mirrors the config block of the sibling tactile.ahk / tactile-macos ports.
/// </summary>
public class Config
{
    public int GridCols { get; set; } = 8;
    public int GridRows { get; set; } = 4;

    /// <summary>Gap between placed windows and around screen edges (0 = flush).</summary>
    public int GridMarginPx { get; set; } = 0;

    public HotkeySpec Hotkey { get; set; } = new();

    /// <summary>Saves the current arrangement as a named layout (Win+Shift+T).</summary>
    public HotkeySpec SaveLayoutHotkey { get; set; } = new() { Modifiers = ["Win", "Shift"], Key = "T" };

    /// <summary>Opens the restore-layout picker (Win+Shift+R).</summary>
    public HotkeySpec LayoutPickerHotkey { get; set; } = new() { Modifiers = ["Win", "Shift"], Key = "R" };

    /// <summary>0-255 overlay transparency.</summary>
    public int OverlayAlpha { get; set; } = 180;

    /// <summary>Letter labels, row-major; must cover GridCols x GridRows.</summary>
    public string[][] CellHints { get; set; } =
    [
        ["Q", "W", "E", "R", "T", "Y", "U", "I"],
        ["A", "S", "D", "F", "G", "H", "J", "K"],
        ["Z", "X", "C", "V", "B", "N", "M", ","],
        ["1", "2", "3", "4", "5", "6", "7", "8"],
    ];

    public string OverlayBgColor { get; set; } = "101010";
    public string CellBgColor { get; set; } = "1C1C1C";
    public string CellTextColor { get; set; } = "E8E8E8";
    public string HighlightBgColor { get; set; } = "2D5FBF";
    public string HighlightTextColor { get; set; } = "FFFFFF";
    public string HintTextColor { get; set; } = "9C9C9C";
    public string FontName { get; set; } = "Segoe UI";

    /// <summary>Hint letter height as a fraction of cell height.</summary>
    public double FontScale { get; set; } = 0.30;

    public string HintLineText { get; set; } = "Pick two cells · Enter = single cell · Esc = cancel";

    [JsonIgnore]
    public Dictionary<char, GridCell> KeyToCell { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Loads the config, creating the file with defaults on first run.
    /// Throws with a user-readable message when the config is invalid.</summary>
    public static Config LoadOrCreate(string path)
    {
        Config cfg;
        if (File.Exists(path))
        {
            cfg = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOpts)
                  ?? throw new InvalidDataException("tactile.json is empty.");
        }
        else
        {
            cfg = new Config();
            File.WriteAllText(path, JsonSerializer.Serialize(cfg, JsonOpts));
        }
        cfg.Validate();
        return cfg;
    }

    /// <summary>Validates dimensions, hints, colors and the hotkey; builds KeyToCell.</summary>
    public void Validate()
    {
        if (GridCols < 1 || GridRows < 1)
            throw new InvalidDataException("GridCols and GridRows must be at least 1.");
        if (OverlayAlpha is < 0 or > 255)
            throw new InvalidDataException("OverlayAlpha must be 0-255.");
        if (GridMarginPx < 0)
            throw new InvalidDataException("GridMarginPx must not be negative.");
        if (FontScale <= 0 || FontScale > 1)
            throw new InvalidDataException("FontScale must be between 0 and 1.");

        if (CellHints.Length != GridRows)
            throw new InvalidDataException($"CellHints has {CellHints.Length} rows but GridRows is {GridRows}.");

        KeyToCell.Clear();
        for (int r = 0; r < GridRows; r++)
        {
            if (CellHints[r].Length != GridCols)
                throw new InvalidDataException($"CellHints row {r + 1} has {CellHints[r].Length} entries but GridCols is {GridCols}.");
            for (int c = 0; c < GridCols; c++)
            {
                string hint = CellHints[r][c];
                if (hint.Length != 1)
                    throw new InvalidDataException($"Cell hint \"{hint}\" (row {r + 1}, col {c + 1}) must be a single character.");
                char key = char.ToLowerInvariant(hint[0]);
                if (!KeyToCell.TryAdd(key, new GridCell(c, r)))
                    throw new InvalidDataException($"Duplicate cell hint \"{hint}\" (row {r + 1}, col {c + 1}).");
            }
        }

        foreach (var (name, value) in new[]
                 {
                     (nameof(OverlayBgColor), OverlayBgColor), (nameof(CellBgColor), CellBgColor),
                     (nameof(CellTextColor), CellTextColor), (nameof(HighlightBgColor), HighlightBgColor),
                     (nameof(HighlightTextColor), HighlightTextColor), (nameof(HintTextColor), HintTextColor),
                 })
        {
            if (!TryParseColor(value, out _))
                throw new InvalidDataException($"{name} \"{value}\" is not a 6-digit hex color (RRGGBB).");
        }

        // Throw on invalid hotkeys.
        ParseHotkey(Hotkey);
        ParseHotkey(SaveLayoutHotkey);
        ParseHotkey(LayoutPickerHotkey);
    }

    /// <summary>Translates a hotkey spec into RegisterHotKey arguments plus a display string.</summary>
    public static (uint Modifiers, uint Vk, string Display) ParseHotkey(HotkeySpec spec)
    {
        uint mods = 0;
        var names = new List<string>();
        foreach (string m in spec.Modifiers)
        {
            switch (m.ToLowerInvariant())
            {
                case "win": mods |= Win32.MOD_WIN; names.Add("Win"); break;
                case "ctrl" or "control": mods |= Win32.MOD_CONTROL; names.Add("Ctrl"); break;
                case "alt": mods |= Win32.MOD_ALT; names.Add("Alt"); break;
                case "shift": mods |= Win32.MOD_SHIFT; names.Add("Shift"); break;
                default: throw new InvalidDataException($"Unknown hotkey modifier \"{m}\" (use Win/Ctrl/Alt/Shift).");
            }
        }

        uint vk;
        string key = spec.Key;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
            vk = char.ToUpperInvariant(key[0]);
        else if (Enum.TryParse<Keys>(key, true, out var parsed))
            vk = (uint)parsed;
        else
            throw new InvalidDataException($"Unknown hotkey key \"{key}\".");

        names.Add(key.Length == 1 ? key.ToUpperInvariant() : key);
        return (mods, vk, string.Join("+", names));
    }

    public static Color ParseColor(string hex)
    {
        if (!TryParseColor(hex, out var color))
            throw new InvalidDataException($"\"{hex}\" is not a 6-digit hex color (RRGGBB).");
        return color;
    }

    private static bool TryParseColor(string hex, out Color color)
    {
        color = Color.Black;
        if (hex.Length != 6 || !int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
            return false;
        color = Color.FromArgb(unchecked((int)0xFF000000) | rgb);
        return true;
    }
}

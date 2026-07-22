# Tactile for Windows

Native Windows port of the [Tactile](https://extensions.gnome.org/extension/4548/tactile/)-style
keyboard window placement workflow — a C#/.NET tray app, no AutoHotkey required.
Sibling of [tactile-ahk-windows](https://github.com/ismael86/tactile-ahk-windows) (AutoHotkey
version) and `tactile-macos` (Swift/AppKit version).

Press `Win+T`, a lettered grid appears over the active window's monitor, press two
letters — the window snaps to the rectangle spanning those two cells.

## Build

Requires the [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0):

```powershell
winget install --id Microsoft.DotNet.SDK.9 -e
```

Then:

```powershell
cd tactile-windows
dotnet build                 # dev build
dotnet run                   # run directly

# Release single-file exe (needs the .NET 9 Desktop Runtime on the machine):
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

# Fully portable exe (~70 MB, no runtime needed):
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish-portable
```

The result is `publish\Tactile.exe`.

## Usage

| Key | Action |
| --- | --- |
| `Win+T` | Show the grid over the active window's monitor (press again to cancel) |
| two letters | Place window spanning both cells (inclusive) |
| same letter twice, or letter + `Enter` | Place window in exactly that cell |
| `Escape` / alt-tab away | Cancel, nothing moves |
| `Win+Shift+T` | Save the current arrangement as a named layout |
| `Win+Shift+R` | Restore-layout picker (press a number) |

The tray icon menu has **Save Layout…**, **Restore Layout**, **Start at Login**
(registers the exe in the HKCU Run key), **Reload Config**, **Edit Config**, and
**Exit**.

## Saved layouts

`Win+Shift+T` snapshots which apps' windows sit in which grid cells on the
current monitor (hand-placed windows get snapped to the nearest cells) and saves
it under a name. Restore via `Win+Shift+R`, the tray menu, a per-layout hotkey
(tray → Restore Layout → *name* → Assign Hotkey…), or the CLI:

```powershell
.\Tactile.exe --list-layouts
.\Tactile.exe --save-layout work
.\Tactile.exe --restore-layout work
.\Tactile.exe --list-windows      # what a snapshot would capture
```

Layouts live in `layouts.json` next to the exe — pretty-printed, key-sorted, and
safe to edit by hand (the file is re-read before every save/restore; a corrupt
file is backed up to `layouts.json.bak`, never overwritten silently). Positions
are stored as grid cells, so layouts survive resolution changes; a layout saved
on a different grid size is scaled proportionally on restore. Windows of apps
that aren't running are skipped and reported — nothing is launched. Restore
order is deterministic: entries are placed lowest `order` first, so the highest
lands frontmost.

Windows are matched back to entries by executable name, with a title hint
preferred when an app had several windows with distinct titles.

## Configuration

`tactile.json` is created next to `Tactile.exe` on first run. Edit it (tray →
Edit Config), then tray → Reload Config. Options mirror the sibling ports:

- `GridCols` / `GridRows` — grid dimensions (default 8×4)
- `GridMarginPx` — gap between placed windows and around screen edges (0 = flush)
- `Hotkey` — e.g. `{ "Modifiers": ["Win"], "Key": "T" }` or `["Ctrl","Alt"]` + `"G"`
- `SaveLayoutHotkey` / `LayoutPickerHotkey` — same shape (default `Win+Shift+T` / `Win+Shift+R`)
- `OverlayAlpha` — overlay transparency, 0–255
- `CellHints` — the letter labels; dimensions must match the grid
- Colors (hex `RRGGBB`), `FontName`, `FontScale`, `HintLineText`

## Implementation notes

- Per-monitor-V2 DPI aware: all geometry is physical pixels, correct on mixed-DPI
  multi-monitor setups.
- Placement compensates for the invisible Win10/11 resize border
  (`DWMWA_EXTENDED_FRAME_BOUNDS`), so visible window edges sit flush — same as
  native Win+Arrow snapping.
- After placing, the rect is re-verified at 150/450 ms and re-applied if the app
  moved itself (keeps Electron apps like VS Code pixel-exact).
- The hotkey tries `RegisterHotKey` first; for chords the shell already owns
  (like `Win+T`, Explorer's taskbar-cycling shortcut) it falls back to a
  low-level keyboard hook that intercepts only that exact chord — the same
  approach AutoHotkey uses. All other typing passes through untouched.

## Known limitations

- Apps with minimum/fixed size constraints (some Store apps, dialogs) may end up
  larger than the chosen cells — placement is best-effort.
- A non-elevated Tactile cannot move windows running as administrator.
- `Win+T` normally cycles taskbar apps; Tactile intercepts it while running
  (that shortcut comes back as soon as Tactile exits).
- Letters pressed while the Win key is still physically held arrive as OS
  shortcuts; release Win after the chord before typing the cells.

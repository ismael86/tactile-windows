<#
    Generates assets/tactile.ico — the app / shortcut / Add-Remove-Programs icon.

    Kept as a regeneration tool only; the produced .ico is committed. Run from
    the repo root:  powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1

    The glyph mirrors TrayApp.CreateGridIcon() (src/TrayApp.cs) — a 4x3 grid —
    but on an opaque rounded tile rather than white-on-transparent. The tray
    version is drawn on the taskbar where white always reads; a file icon has to
    survive a light Explorer background too.
#>

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$OutPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'assets\tactile.ico'

# Every size is written as a classic 32bpp DIB. PNG-compressed entries would
# make the file ~10x smaller, but older ICO parsers (including the one in
# .NET Framework's System.Drawing.Icon) mishandle them, and 300 KB of icon
# inside the exe costs nothing.
$Sizes = @(16, 24, 32, 48, 64, 128, 256)

$Background = [System.Drawing.Color]::FromArgb(255, 0x1F, 0x24, 0x30)
$Cell       = [System.Drawing.Color]::White

function New-GlyphBitmap([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    try {
        $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.Clear([System.Drawing.Color]::Transparent)

        # Rounded tile filling the canvas.
        $r = [float]($Size * 0.20)
        $d = $r * 2
        $path = New-Object System.Drawing.Drawing2D.GraphicsPath
        try {
            $w = [float]$Size
            $path.AddArc(0, 0, $d, $d, 180, 90)
            $path.AddArc($w - $d, 0, $d, $d, 270, 90)
            $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90)
            $path.AddArc(0, $w - $d, $d, $d, 90, 90)
            $path.CloseFigure()
            $bg = New-Object System.Drawing.SolidBrush($Background)
            try { $g.FillPath($bg, $path) } finally { $bg.Dispose() }
        } finally { $path.Dispose() }

        # 4x3 grid, same proportions as the runtime tray glyph (pad 5/32, gap 2/32).
        $cols = 4; $rows = 3
        $pad = [float]($Size * 5.0 / 32.0)
        $gap = [float]($Size * 2.0 / 32.0)
        $cw = ($Size - 2 * $pad - ($cols - 1) * $gap) / $cols
        $ch = ($Size - 2 * $pad - ($rows - 1) * $gap) / $rows
        $brush = New-Object System.Drawing.SolidBrush($Cell)
        try {
            for ($rw = 0; $rw -lt $rows; $rw++) {
                for ($c = 0; $c -lt $cols; $c++) {
                    $g.FillRectangle($brush, $pad + $c * ($cw + $gap), $pad + $rw * ($ch + $gap), $cw, $ch)
                }
            }
        } finally { $brush.Dispose() }
    } finally { $g.Dispose() }
    return $bmp
}

# 32bpp BGRA bottom-up DIB: BITMAPINFOHEADER + XOR bitmap + (zeroed) AND mask.
function Get-DibBytes([System.Drawing.Bitmap]$Bmp) {
    $size = $Bmp.Width
    $rect = New-Object System.Drawing.Rectangle(0, 0, $size, $size)
    $data = $Bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
                          [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $pixels = New-Object byte[] ($size * $size * 4)
        for ($y = 0; $y -lt $size; $y++) {
            # Bottom-up: source row (size-1-y) lands at destination row y.
            $src = [IntPtr]::Add($data.Scan0, ($size - 1 - $y) * $data.Stride)
            [System.Runtime.InteropServices.Marshal]::Copy($src, $pixels, $y * $size * 4, $size * 4)
        }
    } finally { $Bmp.UnlockBits($data) }

    $maskStride = [int][Math]::Floor((($size + 31) / 32)) * 4
    $mask = New-Object byte[] ($maskStride * $size)   # all zero = fully opaque

    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter($ms)
    try {
        $bw.Write([int]40)                 # biSize
        $bw.Write([int]$size)              # biWidth
        $bw.Write([int]($size * 2))        # biHeight (XOR + AND stacked)
        $bw.Write([int16]1)                # biPlanes
        $bw.Write([int16]32)               # biBitCount
        $bw.Write([int]0)                  # biCompression = BI_RGB
        $bw.Write([int]($pixels.Length + $mask.Length))
        $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0); $bw.Write([int]0)
        $bw.Write($pixels, 0, $pixels.Length)
        $bw.Write($mask, 0, $mask.Length)
        $bw.Flush()
        return $ms.ToArray()
    } finally { $bw.Dispose() }
}

$images = @()
foreach ($size in $Sizes) {
    $bmp = New-GlyphBitmap $size
    try { $bytes = Get-DibBytes $bmp } finally { $bmp.Dispose() }
    $images += , @{ Size = $size; Bytes = $bytes }
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)
try {
    $w.Write([int16]0)                  # reserved
    $w.Write([int16]1)                  # type = icon
    $w.Write([int16]$images.Count)

    $offset = 6 + 16 * $images.Count
    foreach ($img in $images) {
        $dim = if ($img.Size -ge 256) { 0 } else { $img.Size }
        $w.Write([byte]$dim)            # width  (0 => 256)
        $w.Write([byte]$dim)            # height
        $w.Write([byte]0)               # palette entries
        $w.Write([byte]0)               # reserved
        $w.Write([int16]1)              # planes
        $w.Write([int16]32)             # bit count
        $w.Write([int]$img.Bytes.Length)
        $w.Write([int]$offset)
        $offset += $img.Bytes.Length
    }
    foreach ($img in $images) {
        # Explicit 3-arg overload: PowerShell otherwise resolves $img.Bytes
        # (a PSObject-wrapped array) to BinaryWriter.Write(bool) and emits 1 byte.
        $blob = [byte[]]$img.Bytes
        $w.Write($blob, 0, $blob.Length)
    }
    $w.Flush()
    [System.IO.File]::WriteAllBytes($OutPath, $out.ToArray())
} finally { $w.Dispose() }

Write-Host "Wrote $OutPath ($([int]((Get-Item $OutPath).Length / 1KB)) KB, $($images.Count) sizes)"

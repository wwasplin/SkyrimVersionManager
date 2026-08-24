# Generates icon.ico (multi-size, PNG-compressed) for SkyrimVersionManager.
# Design: dark rounded tile, subtle border, bold accent-blue double chevron pointing down
# (the "downgrade" motif), with a small horizontal "version" bar above.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

function New-IconFrame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    # Rounded-rect tile
    $m = [Math]::Max(1, [int]($s * 0.03))
    $r = [int]($s * 0.22)
    $side = $s - 2 * $m
    $rect = New-Object System.Drawing.Rectangle($m, $m, $side, $side)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 47, 52, 66),
        [System.Drawing.Color]::FromArgb(255, 22, 24, 31),
        [System.Drawing.Drawing2D.LinearGradientMode]::Vertical)
    $g.FillPath($grad, $path)

    if ($s -ge 24) {
        $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 74, 82, 100), [Math]::Max(1, $s * 0.03))
        $g.DrawPath($borderPen, $path)
        $borderPen.Dispose()
    }

    $accent = [System.Drawing.Color]::FromArgb(255, 122, 162, 247)
    $accentDim = [System.Drawing.Color]::FromArgb(255, 84, 116, 190)

    # "Version" bar up top
    if ($s -ge 24) {
        $barBrush = New-Object System.Drawing.SolidBrush($accentDim)
        $bw = $s * 0.4; $bh = [Math]::Max(1.5, $s * 0.07)
        $g.FillRectangle($barBrush, [float](($s - $bw)/2), [float]($s * 0.2), [float]$bw, [float]$bh)
        $barBrush.Dispose()
    }

    # Double chevron pointing down
    function Draw-Chevron([float]$yTop, [System.Drawing.Color]$color) {
        $pen = New-Object System.Drawing.Pen($color, [Math]::Max(2, $s * 0.11))
        $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
        $pts = @(
            (New-Object System.Drawing.PointF(($s * 0.28), $yTop)),
            (New-Object System.Drawing.PointF(($s * 0.5),  ($yTop + $s * 0.18))),
            (New-Object System.Drawing.PointF(($s * 0.72), $yTop))
        )
        $g.DrawLines($pen, $pts)
        $pen.Dispose()
    }
    Draw-Chevron ($s * 0.38) $accentDim
    Draw-Chevron ($s * 0.56) $accent

    $g.Dispose(); $grad.Dispose(); $path.Dispose()
    return $bmp
}

# Standalone preview for eyeballing the design
$preview = New-IconFrame 256
$preview.Save((Join-Path $PSScriptRoot "icon-preview.png"), [System.Drawing.Imaging.ImageFormat]::Png)
$preview.Dispose()

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($s in $sizes) {
    $bmp = New-IconFrame $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    ,@($s, $ms.ToArray())
}

# Assemble .ico (PNG-compressed entries)
$out = Join-Path $PSScriptRoot "icon.ico"
$fs = [System.IO.File]::Create($out)
$w = New-Object System.IO.BinaryWriter($fs)
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $s = $f[0]; $data = $f[1]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim)
    $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$data.Length); $w.Write([uint32]$offset)
    $offset += $data.Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()
Write-Host "Wrote $out ($((Get-Item $out).Length) bytes, $($frames.Count) sizes)"

<#
.SYNOPSIS
    Draws the program icon (a long page with a scroll arrow) and writes a multi-size ICO.
.DESCRIPTION
    Every size is drawn separately (no downscaling), so small sizes stay crisp. The ICO holds
    PNG-compressed images of 16, 20, 24, 32, 40, 48, 64, 128 and 256 px (Windows Vista+).
#>
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..\src\ScrollCapture.App\app.ico')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

function New-IconImage([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $s = $size / 32.0

    # Page: a tall sheet whose lower part fades, as if it continued below.
    $page = New-Object System.Drawing.RectangleF (5 * $s), (2 * $s), (17 * $s), (28 * $s)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $r = [Math]::Max(1, 2.5 * $s)
    $path.AddArc($page.X, $page.Y, $r * 2, $r * 2, 180, 90)
    $path.AddArc($page.Right - $r * 2, $page.Y, $r * 2, $r * 2, 270, 90)
    $path.AddLine($page.Right, $page.Bottom, $page.X, $page.Bottom)
    $path.CloseFigure()
    $fill = New-Object System.Drawing.Drawing2D.LinearGradientBrush $page, ([System.Drawing.Color]::FromArgb(255, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(255, 205, 225, 245)), 90
    $g.FillPath($fill, $path)
    $border = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 40, 90, 160)), ([Math]::Max(1, 1.2 * $s))
    $g.DrawPath($border, $path)

    # Text lines.
    $line = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 90, 120, 160)), ([Math]::Max(1, 1.6 * $s))
    $widths = 11, 8, 12, 9, 11, 7
    for ($i = 0; $i -lt $widths.Count; $i++) {
        $y = (7 + $i * 3.6) * $s
        if ($size -lt 24 -and $i % 2 -eq 1) { continue }
        $g.DrawLine($line, 8 * $s, $y, (8 + $widths[$i]) * $s, $y)
    }

    # Scroll arrow on a round badge.
    $badge = New-Object System.Drawing.RectangleF (16 * $s), (14 * $s), (15 * $s), (15 * $s)
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 0, 120, 215))), $badge)
    $white = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([Math]::Max(1.5, 2.2 * $s))
    $white.StartCap = 'Round'; $white.EndCap = 'Round'; $white.LineJoin = 'Round'
    $cx = $badge.X + $badge.Width / 2
    $g.DrawLine($white, $cx, 18 * $s, $cx, 25.5 * $s)
    $g.DrawLines($white, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ($cx - 3.6 * $s), (22 * $s)),
        (New-Object System.Drawing.PointF $cx, (25.8 * $s)),
        (New-Object System.Drawing.PointF ($cx + 3.6 * $s), (22 * $s))))

    $g.Dispose()
    $stream = New-Object System.IO.MemoryStream
    $bmp.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return , $stream.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$images = foreach ($size in $sizes) { , (New-IconImage $size) }

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$images[$i].Length); $w.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $w.Write($image) }
$w.Flush()
[System.IO.File]::WriteAllBytes([System.IO.Path]::GetFullPath($OutFile), $out.ToArray())
"Wrote $OutFile ($($out.Length) bytes, sizes $($sizes -join ', '))"

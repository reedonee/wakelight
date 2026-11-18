# Draws the Wakelight mark (a glowing crescent "eclipse" on midnight indigo)
# and writes assets\wakelight.ico (16-256 px) plus assets\wakelight.png.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root   = Split-Path $PSScriptRoot
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

function New-Mark([int]$s) {
    $bmp = New-Object Drawing.Bitmap $s, $s, ([Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([Drawing.Color]::Transparent)

    # Rounded-square tile, midnight gradient
    $r = [single]($s * 0.22); $d = $r * 2; $w = [single]($s - 1)
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($w - $d, 0, $d, $d, 270, 90)
    $path.AddArc($w - $d, $w - $d, $d, $d, 0, 90); $path.AddArc(0, $w - $d, $d, $d, 90, 90); $path.CloseFigure()
    $bg = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.PointF 0,0), (New-Object Drawing.PointF $s,$s), ([Drawing.Color]::FromArgb(255,0x24,0x1D,0x45)), ([Drawing.Color]::FromArgb(255,0x0B,0x0A,0x14))
    $g.FillPath($bg, $path)

    # Soft glow behind the orb
    $cx = $s * 0.5; $cy = $s * 0.52; $orb = $s * 0.30
    $glowPath = New-Object Drawing.Drawing2D.GraphicsPath
    $gr = $orb * 1.9
    $glowPath.AddEllipse([single]($cx - $gr), [single]($cy - $gr), [single]($gr * 2), [single]($gr * 2))
    $glow = New-Object Drawing.Drawing2D.PathGradientBrush $glowPath
    $glow.CenterColor = [Drawing.Color]::FromArgb(150, 0xFF, 0x8A, 0x6A)
    $glow.SurroundColors = @([Drawing.Color]::FromArgb(0, 0xFF, 0x5C, 0x8A))
    $g.SetClip($path); $g.FillPath($glow, $glowPath); $g.ResetClip()

    # Orb: ember -> rose, with an eclipse disc subtracted (true crescent)
    $orbRect = New-Object Drawing.RectangleF ([single]($cx - $orb)), ([single]($cy - $orb)), ([single]($orb * 2)), ([single]($orb * 2))
    $orbBrush = New-Object Drawing.Drawing2D.LinearGradientBrush $orbRect, ([Drawing.Color]::FromArgb(255,0xFF,0xC4,0x6B)), ([Drawing.Color]::FromArgb(255,0xFF,0x4F,0x86)), 60
    $off = $orb * 0.48
    $cutPath = New-Object Drawing.Drawing2D.GraphicsPath
    $cutPath.AddEllipse([single]($cx - $orb * 0.92 + $off), [single]($cy - $orb * 0.92 - $off * 0.7), [single]($orb * 1.84), [single]($orb * 1.84))
    $orbPath = New-Object Drawing.Drawing2D.GraphicsPath
    $orbPath.AddEllipse($orbRect)
    $region = New-Object Drawing.Region $orbPath
    $region.Exclude($cutPath)
    $g.FillRegion($orbBrush, $region)

    # Small star
    if ($s -ge 32) {
        $st = $s * 0.04; $sx = $s * 0.70; $sy = $s * 0.30
        $star = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(235, 0xFF, 0xF1, 0xDC))
        $g.FillEllipse($star, [single]($sx - $st), [single]($sy - $st), [single]($st * 2), [single]($st * 2))
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $b = New-Mark $sz; $ms = New-Object IO.MemoryStream
    $b.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray()
}
(New-Mark 512).Save((Join-Path $assets 'wakelight.png'), [Drawing.Imaging.ImageFormat]::Png)

$ico = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ico
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $bw.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $bw.Write([byte]0); $bw.Write([byte]0); $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$len); $bw.Write([uint32]$offset); $offset += $len
}
foreach ($p in $pngs) { $bw.Write($p) }
[IO.File]::WriteAllBytes((Join-Path $assets 'wakelight.ico'), $ico.ToArray())
Write-Host "Wrote $assets\wakelight.ico and wakelight.png"

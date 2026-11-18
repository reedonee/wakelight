# Builds an original demo scene ("Ember Lake") and a fake Wallpaper Engine
# folder around it, so screenshots never show third-party wallpaper art.
# Run the app against it with:  $env:WAKELIGHT_DEMO = '<repo>\obj\demo'; dist\Wakelight.exe
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot
$demo = Join-Path $root 'obj\demo'
$we   = Join-Path $demo 'WallpaperEngine'
$proj = Join-Path $we 'projects\myprojects\ember-lake'
New-Item -ItemType Directory -Force $proj, (Join-Path $demo 'data') | Out-Null

$W = 1920; $H = 1080
$bmp = New-Object Drawing.Bitmap $W, $H
$g = [Drawing.Graphics]::FromImage($bmp); $g.SmoothingMode = 'AntiAlias'
$C = { param($a, $r, $gg, $b) [Drawing.Color]::FromArgb($a, $r, $gg, $b) }

# sky
$sky = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 0), (New-Object Drawing.Point 0, 640), (& $C 255 0x14 0x10 0x33), (& $C 255 0x6E 0x34 0x5A)
$blend = New-Object Drawing.Drawing2D.ColorBlend 3
$blend.Colors = @((& $C 255 0x0E 0x0B 0x26), (& $C 255 0x3A 0x22 0x55), (& $C 255 0xE0 0x7A 0x5F)); $blend.Positions = @(0, 0.6, 1)
$sky.InterpolationColors = $blend
$g.FillRectangle($sky, 0, 0, $W, 640)

# stars
$rnd = New-Object Random 11
for ($i = 0; $i -lt 260; $i++) {
    $s = $rnd.NextDouble() * 2.2 + 0.4; $x = $rnd.Next(0, $W); $y = $rnd.Next(0, 430)
    $g.FillEllipse((New-Object Drawing.SolidBrush (& $C ([int](80 + $rnd.NextDouble() * 175)) 255 241 220)), [single]$x, [single]$y, [single]$s, [single]$s)
}

# glow + crescent moon
$cx = 1380; $cy = 250; $r = 70
$gp = New-Object Drawing.Drawing2D.GraphicsPath; $gp.AddEllipse($cx - 260, $cy - 260, 520, 520)
$pg = New-Object Drawing.Drawing2D.PathGradientBrush $gp; $pg.CenterColor = (& $C 110 255 160 110); $pg.SurroundColors = @((& $C 0 255 92 138))
$g.FillPath($pg, $gp)
$moon = New-Object Drawing.Drawing2D.GraphicsPath; $moon.AddEllipse($cx - $r, $cy - $r, 2 * $r, 2 * $r)
$cut = New-Object Drawing.Drawing2D.GraphicsPath; $cut.AddEllipse($cx - $r + 34, $cy - $r - 24, 2 * $r - 6, 2 * $r - 6)
$reg = New-Object Drawing.Region $moon; $reg.Exclude($cut)
$mb = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Rectangle ($cx - $r), ($cy - $r), (2 * $r), (2 * $r)), (& $C 255 255 214 150), (& $C 255 255 110 140), 60
$g.FillRegion($mb, $reg)

# mountain layers
function Ridge($baseY, $amp, $seed, $color) {
    $rr = New-Object Random $seed
    $pts = New-Object System.Collections.Generic.List[Drawing.PointF]
    $pts.Add((New-Object Drawing.PointF 0, 700))
    $y = $baseY
    for ($x = 0; $x -le $W; $x += 40) { $y = [Math]::Max($baseY - $amp, [Math]::Min($baseY + $amp * 0.3, $y + ($rr.NextDouble() - 0.5) * $amp * 0.5)); $pts.Add((New-Object Drawing.PointF $x, $y)) }
    $pts.Add((New-Object Drawing.PointF $W, 700))
    $g.FillPolygon((New-Object Drawing.SolidBrush $color), $pts.ToArray())
}
Ridge 500 160 3 (& $C 255 0x4A 0x2A 0x5E)
Ridge 560 120 5 (& $C 255 0x2C 0x1C 0x44)
Ridge 610 80 9 (& $C 255 0x18 0x12 0x2C)

# lake with reflection
$g.FillRectangle((New-Object Drawing.SolidBrush (& $C 255 0x0D 0x0B 0x1C)), 0, 640, $W, $H - 640)
$refl = New-Object Drawing.Drawing2D.LinearGradientBrush (New-Object Drawing.Point 0, 640), (New-Object Drawing.Point 0, $H), (& $C 150 0xE0 0x7A 0x5F), (& $C 0 0x0D 0x0B 0x1C)
$g.FillRectangle($refl, 0, 640, $W, $H - 640)
for ($i = 0; $i -lt 26; $i++) {
    $yy = 660 + $i * 16; $wd = 260 - $i * 7
    $g.FillRectangle((New-Object Drawing.SolidBrush (& $C ([int](150 - $i * 5)) 255 190 130)), [single]($cx - $wd / 2 + $rnd.Next(-20, 20)), [single]$yy, [single]$wd, 3)
}
$g.Dispose()

$jpg = Join-Path $proj 'preview.jpg'
$bmp.Save($jpg, [Drawing.Imaging.ImageFormat]::Jpeg); $bmp.Dispose()

@'
{ "title" : "Ember Lake", "type" : "scene", "file" : "scene.json", "preview" : "preview.jpg" }
'@ | Set-Content (Join-Path $proj 'project.json') -Encoding ASCII
"{}" | Set-Content (Join-Path $proj 'scene.json') -Encoding ASCII
$file = ($proj -replace '\\', '/') + '/scene.json'
"{ `"demo`" : { `"general`" : { `"wallpaperconfig`" : { `"selectedwallpapers`" : { `"Monitor0`" : { `"file`" : `"$file`" } } } } } }" |
    Set-Content (Join-Path $we 'config.json') -Encoding ASCII
New-Item -ItemType File -Force (Join-Path $we 'wallpaper64.exe') | Out-Null
Write-Host "Demo ready in $demo"

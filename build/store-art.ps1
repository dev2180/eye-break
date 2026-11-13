# Makes the Partner Center images (screenshots, poster, box art, hero art, tile icon) in artifacts\store\final.
# Inputs: black hole renders of tools/preview/blackhole.html (raw-wide 1920x1160, raw-tall 1440x2240,
# raw-square 2160x2240, made with headless Edge), the tray panel capture (panel.png) and the app's logo images.
param([string]$Panel)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$src  = "$root\artifacts\store"
$out  = "$src\final"
New-Item -ItemType Directory -Force $out | Out-Null

$ink   = [Drawing.Color]::FromArgb(246, 239, 228)
$muted = [Drawing.Color]::FromArgb(176, 170, 196)
function New-Canvas($w, $h) {
    $b = New-Object Drawing.Bitmap($w, $h); $g = [Drawing.Graphics]::FromImage($b)
    $g.Clear([Drawing.Color]::Black); $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
    $g.TextRenderingHint = 'AntiAliasGridFit'; $g.PixelOffsetMode = 'HighQuality'
    return @($b, $g)
}
function Draw-Crop($g, $file, $x, $y, $w, $h, $sx, $sy) {
    $img = [Drawing.Image]::FromFile($file)
    $g.DrawImage($img, (New-Object Drawing.Rectangle($x, $y, $w, $h)), (New-Object Drawing.Rectangle($sx, $sy, $w, $h)), 'Pixel')
    $img.Dispose()
}
function Gold($x, $y, $w, $h) {
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Rectangle($x, $y, $w, $h)), [Drawing.Color]::FromArgb(255, 227, 174), [Drawing.Color]::FromArgb(227, 146, 53), 0)
    $br.WrapMode = 'TileFlipXY'; return $br
}
function Text($g, $s, $font, $size, $brush, $x, $y, $w, $h, $align = 'Near') {
    $sf = New-Object Drawing.StringFormat; $sf.Alignment = $align
    $g.DrawString($s, (New-Object Drawing.Font($font, [single]$size, [Drawing.GraphicsUnit]::Pixel)), $brush, (New-Object Drawing.RectangleF($x, $y, $w, $h)), $sf)
}
function Shade($g, $x, $y, $w, $h, $a0, $a1, $angle) {
    $br = New-Object Drawing.Drawing2D.LinearGradientBrush((New-Object Drawing.Rectangle($x, $y, $w, $h)), [Drawing.Color]::FromArgb($a0, 0, 0, 0), [Drawing.Color]::FromArgb($a1, 0, 0, 0), $angle)
    $br.WrapMode = 'TileFlipXY'; $g.FillRectangle($br, $x, $y, $w, $h)
}
function Save($pair, $name) { $pair[0].Save("$out\$name", [Drawing.Imaging.ImageFormat]::Png); $pair[1].Dispose(); $pair[0].Dispose() }

# 1. Hero art and screenshot 1: the black hole, no text (hero art must not include the title)
foreach ($name in 'super-hero-art-1920x1080.png', 'screenshot-1-black-hole.png') {
    $c = New-Canvas 1920 1080; Draw-Crop $c[1] "$src\raw-wide.png" 0 0 1920 1080 0 0; Save $c $name
}

# 2. Screenshot 2: the idea, in words
$c = New-Canvas 1920 1080; $g = $c[1]
Draw-Crop $g "$src\raw-wide.png" 0 0 1920 1080 0 0
Shade $g 0 0 1920 300 220 0 90
Text $g 'Every 20 minutes, rest your eyes for 20 seconds' 'Segoe UI Light' 64 (Gold 0 60 1920 90) 0 70 1920 90 'Center'
Text $g 'Your eyes close, you drift past a ray-traced black hole, and a third eye opens you back to your work.' 'Segoe UI' 30 (New-Object Drawing.SolidBrush($muted)) 0 160 1920 50 'Center'
Save $c 'screenshot-2-every-20-minutes.png'

# 3. Screenshot 3: the tray panel
$c = New-Canvas 1920 1080; $g = $c[1]
Draw-Crop $g "$src\raw-wide.png" 420 0 1500 1080 0 0
Shade $g 0 0 1400 1080 240 0 0
$p = [Drawing.Image]::FromFile($Panel)
$ph = 1000; $pw = [int]($p.Width * $ph / $p.Height)
$g.DrawImage($p, (New-Object Drawing.Rectangle(1300, 40, $pw, $ph)))
$p.Dispose()
Text $g 'One click from the tray' 'Segoe UI Light' 72 (Gold 120 300 900 100) 120 300 1000 100
$lines = 'See the time to your next break', 'Take a break now, or skip one for a while', 'Infinite mode with music, quotes and a timer', 'Set the interval, break length and more'
for ($i = 0; $i -lt $lines.Count; $i++) {
    Text $g (([string][char]0x2022 + '  ' + $lines[$i])) 'Segoe UI' 34 (New-Object Drawing.SolidBrush($ink)) 124 (430 + $i * 62) 1100 60
}
Save $c 'screenshot-3-tray-panel.png'

# 4. Poster art (9:16) and box art (1:1): black hole, logo and title
function Logo-Title($g, $w, $h, $scale) {
    $logo = [Drawing.Image]::FromFile("$src\logos\StoreTile300.png")
    $ls = [int](300 * $scale); $g.DrawImage($logo, (New-Object Drawing.Rectangle([int](($w - $ls) / 2), [int]($h * 0.07), $ls, $ls))); $logo.Dispose()
    Shade $g 0 ([int]($h * 0.68)) $w ([int]($h * 0.32)) 0 230 90
    Text $g 'EYE BREAK' 'Segoe UI Light' (150 * $scale) (Gold 0 ([int]($h * 0.74)) $w 200) 0 ([int]($h * 0.74)) $w ([int](220 * $scale)) 'Center'
    Text $g ('20 {0} 20 {0} 20' -f [char]0x00B7) 'Segoe UI' (60 * $scale) (New-Object Drawing.SolidBrush($muted)) 0 ([int]($h * 0.74 + 210 * $scale)) $w ([int](90 * $scale)) 'Center'
}
$c = New-Canvas 1440 2160; Draw-Crop $c[1] "$src\raw-tall.png" 0 0 1440 2160 0 0; Logo-Title $c[1] 1440 2160 1.0; Save $c 'poster-art-1440x2160.png'
$c = New-Canvas 2160 2160; Draw-Crop $c[1] "$src\raw-square.png" 0 0 2160 2160 0 0; $c[1].FillRectangle([Drawing.Brushes]::Black, 0, 2000, 520, 160); Logo-Title $c[1] 2160 2160 1.2; Save $c 'box-art-2160x2160.png'

Copy-Item "$src\logos\StoreTile300.png" "$out\app-tile-icon-300x300.png" -Force
Get-ChildItem $out | ForEach-Object { $i = [Drawing.Image]::FromFile($_.FullName); "{0}  {1}x{2}  {3:N0} KB" -f $_.Name, $i.Width, $i.Height, ($_.Length / 1KB); $i.Dispose() }

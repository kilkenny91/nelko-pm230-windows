$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = 'C:\Users\Admin\pm230\src\iconwork'
New-Item -ItemType Directory -Force -Path $dir | Out-Null
foreach ($f in Get-ChildItem $dir) { Remove-Item $f.FullName -Force }

function RoundedPath([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  if ($d -gt $w) { $d = $w }
  if ($d -gt $h) { $d = $h }
  $p.AddArc($x, $y, $d, $d, 180, 90)
  $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
  $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure()
  return $p
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
  $g.Clear([System.Drawing.Color]::Transparent)
  $u = $s / 16.0

  # Grundflaeche: abgerundetes Quadrat, blauer Verlauf
  $bg = RoundedPath (1.2 * $u) (1.2 * $u) (13.6 * $u) (13.6 * $u) (3.2 * $u)
  $c1 = [System.Drawing.Color]::FromArgb(255, 74, 158, 232)
  $c2 = [System.Drawing.Color]::FromArgb(255, 24, 82, 148)
  $bb = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.RectangleF(0, 0, $s, $s)), $c1, $c2, 60.0)
  $g.FillPath($bb, $bg)
  $bb.Dispose()

  # Etikett (weiss) mit abgerissener Unterkante
  $lab = RoundedPath (3.6 * $u) (4.4 * $u) (8.8 * $u) (7.0 * $u) (1.1 * $u)
  $g.FillPath([System.Drawing.Brushes]::White, $lab)

  # Druckzeilen
  $t = [System.Drawing.Color]::FromArgb(255, 18, 22, 27)
  $tb = New-Object System.Drawing.SolidBrush $t
  $bh = [Math]::Max(1.0, 1.15 * $u)
  $g.FillRectangle($tb, 4.9 * $u, 5.9 * $u, 6.2 * $u, $bh)
  $g.FillRectangle($tb, 4.9 * $u, 7.7 * $u, 4.6 * $u, $bh)
  if ($s -ge 40) { $g.FillRectangle($tb, 4.9 * $u, 9.5 * $u, 5.4 * $u, $bh) }
  $tb.Dispose()

  # Andrueckstreifen unterm Etikett
  $gb = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 214, 226, 240))
  $g.FillRectangle($gb, 3.6 * $u, 11.9 * $u, 8.8 * $u, [Math]::Max(1.0, 0.8 * $u))
  $gb.Dispose()

  $g.Dispose()
  $bmp.Save((Join-Path $dir ("i" + $s + ".png")), [System.Drawing.Imaging.ImageFormat]::Png)
  $bmp.Dispose()
}

# ICO bauen (PNG-Eintraege, Vista aufwaerts zulaessig)
$parts = @()
foreach ($s in $sizes) { $parts += , @($s, [System.IO.File]::ReadAllBytes((Join-Path $dir ("i" + $s + ".png")))) }

$b = New-Object System.Collections.Generic.List[byte]
function E1([int]$v) { [void]$b.Add([byte]($v -band 0xFF)) }
function E2([int]$v) { E1 $v; E1 ($v -shr 8) }
function E4([int]$v) { E2 $v; E2 ($v -shr 16) }

E2 0; E2 1; E2 $parts.Count
$head = 6 + 16 * $parts.Count
$off = $head
foreach ($p in $parts) {
  $s = $p[0]
  $dim = if ($s -ge 256) { 0 } else { $s }
  E1 $dim; E1 $dim; E1 0
  E2 1; E4 32
  E4 $p[1].Length; E4 $off
  $off += $p[1].Length
}
foreach ($p in $parts) { foreach ($by in $p[1]) { [void]$b.Add($by) } }

$ico = 'C:\Users\Admin\pm230\src\pm230.ico'
[System.IO.File]::WriteAllBytes($ico, $b.ToArray())
Write-Output ("ico written: " + $ico + "  " + $b.Count + " bytes, " + $parts.Count + " images")

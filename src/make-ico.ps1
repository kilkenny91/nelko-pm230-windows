$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$dir = 'C:\Users\Admin\pm230\src\iconwork'
$sizes = 16, 20, 24, 32, 40, 48, 64, 96, 128

$blobs = @()
foreach ($s in $sizes) {
  $file = Join-Path $dir ("i$s.png")
  $bmp = New-Object System.Drawing.Bitmap $file
  $rect = New-Object System.Drawing.Rectangle 0, 0, $s, $s
  $bd = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $stride = [int]$bd.Stride
  $buf = New-Object byte[] ($stride * $s)
  [System.Runtime.InteropServices.Marshal]::Copy($bd.Scan0, $buf, 0, $buf.Length)
  $bmp.UnlockBits($bd)
  $bmp.Dispose()

  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter $ms
  # BITMAPINFOHEADER (Hoehe = doppelt: Farben + Maske)
  $bw.Write([uint32]40); $bw.Write([int32]$s); $bw.Write([int32]($s * 2))
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
  $bw.Write([uint32]0); $bw.Write([int32]0); $bw.Write([int32]0)
  $bw.Write([uint32]0); $bw.Write([uint32]0)
  # Pixeldaten von unten nach oben
  $row = New-Object byte[] ($s * 4)
  for ($y = 0; $y -lt $s; $y++) {
    $src = ($s - 1 - $y) * $stride
    [Array]::Copy($buf, $src, $row, 0, $s * 4)
    $bw.Write($row)
  }
  # undurchsichtige AND-Maske (Transparenz kommt aus dem Alpha-Kanal)
  $maskRow = [int]([Math]::Floor(($s + 31) / 32) * 4)
  $zeros = New-Object byte[] ($maskRow * $s)
  $bw.Write($zeros)
  $bw.Flush()
  $blobs += , @($s, $ms.ToArray())
  $bw.Close()
}

$out = 'C:\Users\Admin\pm230\src\pm230.ico'
$fs2 = [System.IO.File]::Open($out, [System.IO.FileMode]::Create)
$w = New-Object System.IO.BinaryWriter $fs2
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$blobs.Count)
$off = 6 + 16 * $blobs.Count
foreach ($e in $blobs) {
  $s = $e[0]
  $dim = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$e[1].Length); $w.Write([uint32]$off)
  $off += $e[1].Length
}
foreach ($e in $blobs) { $w.Write($e[1]) }
$w.Flush(); $w.Close(); $fs2.Close()
Write-Output ("ico written " + $out + " sizes=" + ($sizes -join ',') + " bytes=" + (Get-Item $out).Length)

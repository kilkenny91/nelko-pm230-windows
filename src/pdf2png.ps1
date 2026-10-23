param([Parameter(Mandatory=$true)][string]$In, [Parameter(Mandatory=$true)][string]$OutDir)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Runtime.WindowsRuntime
$ag = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
})[0]
$aa = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
  $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncAction'
})[0]
function Await($op, [Type]$t) { $n = $ag.MakeGenericMethod($t).Invoke($null, @($op)); $n.Wait(-1) | Out-Null; $n.Result }
function AwaitAction($op) { $n = $aa.Invoke($null, @($op)); $n.Wait(-1) | Out-Null }

[void][Windows.Data.Pdf.PdfDocument,Windows.Data.Pdf,ContentType=WindowsRuntime]
[void][Windows.Storage.StorageFile,Windows.Storage,ContentType=WindowsRuntime]

# PdfPage.Size kommt in 96-dpi-Pixel; 203.2/96 skaliert exakt auf 8 dots/mm des PM230
$scale = 203.2 / 96.0
$file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($In)) ([Windows.Storage.StorageFile])
$pdf = Await ([Windows.Data.Pdf.PdfDocument]::LoadFromFileAsync($file)) ([Windows.Data.Pdf.PdfDocument])
Write-Output ("pages=" + $pdf.PageCount)
for ($i = 0; $i -lt $pdf.PageCount; $i++) {
  $page = $pdf.GetPage($i)
  $w = if ($page.Rotate -eq 90 -or $page.Rotate -eq 270) { [uint32]($page.Size.Height * $scale) } else { [uint32]($page.Size.Width * $scale) }
  $out = Join-Path $OutDir ("page-" + ($i + 1) + ".png")
  $fs = [System.IO.File]::Open($out, [System.IO.FileMode]::Create)
  $ras = [System.IO.WindowsRuntimeStreamExtensions]::AsRandomAccessStream($fs)
  $opts = New-Object Windows.Data.Pdf.PdfPageRenderOptions
  $opts.DestinationWidth = $w
  AwaitAction ($page.RenderToStreamAsync($ras, $opts))
  $ras.Dispose(); $fs.Dispose(); $page.Dispose()
  Write-Output ("wrote page-" + ($i + 1) + ".png width=" + $w)
}

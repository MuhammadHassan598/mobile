param([string]$OutBin, [string]$OutPng, [int]$Thr = 22)
Add-Type -AssemblyName System.Drawing
$terrainFile = Join-Path $PSScriptRoot "..\..\src\EmpireSim\wwwroot\images\world-terrain.jpg"
$img = [System.Drawing.Bitmap]::FromFile((Resolve-Path $terrainFile).Path)
$w = $img.Width; $h = $img.Height
Write-Output "image $w x $h"
$rect = [System.Drawing.Rectangle]::new(0, 0, $w, $h)
$data = $img.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$stride = $data.Stride
$buf = New-Object byte[] ($stride * $h)
[System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $buf, 0, $buf.Length)
$img.UnlockBits($data)
$mask = New-Object byte[] ($w * $h)
$out = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$od = $out.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::WriteOnly, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
$obuf = New-Object byte[] ($od.Stride * $h)
for ($y = 0; $y -lt $h; $y++) {
  for ($x = 0; $x -lt $w; $x++) {
    $i = $y * $stride + $x * 3
    $b = $buf[$i]; $g = $buf[$i + 1]; $r = $buf[$i + 2]
    $land = (($r - $b) -gt $Thr)
    $mask[$y * $w + $x] = [byte]($(if ($land) { 1 } else { 0 }))
    $o = $y * $od.Stride + $x * 3
    $v = if ($land) { 255 } else { 0 }
    $obuf[$o] = $v; $obuf[$o + 1] = $v; $obuf[$o + 2] = $v
  }
}
[System.Runtime.InteropServices.Marshal]::Copy($obuf, 0, $od.Scan0, $obuf.Length)
$out.UnlockBits($od)
[System.IO.File]::WriteAllBytes($OutBin, $mask)
$out.Save($OutPng, [System.Drawing.Imaging.ImageFormat]::Png)
$out.Dispose(); $img.Dispose()
Write-Output "wrote $OutBin and $OutPng"

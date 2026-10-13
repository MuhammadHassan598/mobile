param(
  [string]$Shapes,            # json: { shapes:[{id,name,color,polygons:[[[x,y],...]],lx,ly}], cities:[{name,x,y}] }
  [string]$Out,               # output png
  [string]$Crop = "0,0,2200,1150",   # x,y,w,h in world units
  [double]$Scale = 1.0,
  [double]$Alpha = 0.45,
  [switch]$NoLabels,
  [switch]$Verts,             # draw vertices as dots
  [switch]$Ids,               # write the owner id at each polygon's centre
  [int]$Grid = 0,             # draw a coordinate grid every N world units (labels every 2N)
  [string]$Marks = ""         # json file: [{name,x,y}] extra marks
)
Add-Type -AssemblyName System.Drawing
$terrain = [System.Drawing.Image]::FromFile("$PSScriptRoot\..\..\src\EmpireSim\wwwroot\images\world-terrain.jpg")
$c = $Crop.Split(',') | ForEach-Object { [double]$_ }
$cw = [int]($c[2] * $Scale); $ch = [int]($c[3] * $Scale)
$bmp = New-Object System.Drawing.Bitmap $cw, $ch
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'
$g.DrawImage($terrain, [System.Drawing.Rectangle]::new(0, 0, $cw, $ch), [System.Drawing.Rectangle]::new([int]$c[0], [int]$c[1], [int]$c[2], [int]$c[3]), 'Pixel')
if ($Shapes -eq "") { $data = [pscustomobject]@{ shapes = @(); cities = @() } } else { $data = Get-Content $Shapes -Raw | ConvertFrom-Json }
function ToPts($poly) {
  $arr = New-Object 'System.Drawing.PointF[]' $poly.Count
  for ($i = 0; $i -lt $poly.Count; $i++) { $arr[$i] = [System.Drawing.PointF]::new([float](($poly[$i][0] - $c[0]) * $Scale), [float](($poly[$i][1] - $c[1]) * $Scale)) }
  return ,$arr
}
$idfont = New-Object System.Drawing.Font('Consolas', 9, [System.Drawing.FontStyle]::Bold)
$font = New-Object System.Drawing.Font('Georgia', [float][Math]::Max(7, 9 * $Scale), [System.Drawing.FontStyle]::Bold)
foreach ($s in $data.shapes) {
  $col = [System.Drawing.ColorTranslator]::FromHtml($s.color)
  $fill = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb([int](255 * $Alpha), $col))
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(230, [int]($col.R * 0.55), [int]($col.G * 0.55), [int]($col.B * 0.55))), ([float][Math]::Max(0.8, 1.0 * $Scale * 0.6))
  foreach ($poly in $s.polygons) {
    if ($poly.Count -lt 3) { continue }
    $pts = ToPts $poly
    $g.FillPolygon($fill, $pts)
    $g.DrawPolygon($pen, $pts)
    if ($Ids) {
      $sx = 0.0; $sy = 0.0; foreach ($p in $pts) { $sx += $p.X; $sy += $p.Y }
      $cxp = $sx / $pts.Count; $cyp = $sy / $pts.Count
      if ($cxp -gt 0 -and $cyp -gt 0 -and $cxp -lt $cw -and $cyp -lt $ch) { $g.DrawString(($s.id -replace '^[nf]:',''), $idfont, [System.Drawing.Brushes]::Black, [float]($cxp - 14), [float]$cyp) }
    }
    if ($Verts) { foreach ($p in $pts) { $g.FillEllipse([System.Drawing.Brushes]::Black, $p.X - 1.5, $p.Y - 1.5, 3, 3) } }
  }
  if (-not $NoLabels -and $s.lx) {
    $x = ($s.lx - $c[0]) * $Scale; $y = ($s.ly - $c[1]) * $Scale
    if ($x -gt 0 -and $y -gt 0 -and $x -lt $cw -and $y -lt $ch) { $g.DrawString($s.name, $font, [System.Drawing.Brushes]::Black, [float]($x - 20), [float]$y) }
  }
}
if ($data.cities) {
  foreach ($city in $data.cities) {
    $x = ($city.x - $c[0]) * $Scale; $y = ($city.y - $c[1]) * $Scale
    if ($x -gt 0 -and $y -gt 0 -and $x -lt $cw -and $y -lt $ch) { $g.FillEllipse([System.Drawing.Brushes]::Black, [float]($x - 2), [float]($y - 2), 4, 4); $g.DrawString($city.name, $font, [System.Drawing.Brushes]::DarkRed, [float]($x + 3), [float]($y - 5)) }
  }
}
if ($Grid -gt 0) {
  $gp = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(110, 255, 255, 255)), 1
  $gp2 = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(170, 255, 255, 255)), 1
  $gf = New-Object System.Drawing.Font('Consolas', 9, [System.Drawing.FontStyle]::Bold)
  $x0 = [Math]::Ceiling($c[0] / $Grid) * $Grid
  for ($wx = $x0; $wx -lt $c[0] + $c[2]; $wx += $Grid) {
    $sx = ($wx - $c[0]) * $Scale; $major = (($wx / $Grid) % 2 -eq 0)
    $g.DrawLine($(if ($major) { $gp2 } else { $gp }), [float]$sx, 0, [float]$sx, $ch)
    if ($major) { $g.DrawString([string][int]$wx, $gf, [System.Drawing.Brushes]::Yellow, [float]($sx + 1), 1) }
  }
  $y0 = [Math]::Ceiling($c[1] / $Grid) * $Grid
  for ($wy = $y0; $wy -lt $c[1] + $c[3]; $wy += $Grid) {
    $sy = ($wy - $c[1]) * $Scale; $major = (($wy / $Grid) % 2 -eq 0)
    $g.DrawLine($(if ($major) { $gp2 } else { $gp }), 0, [float]$sy, $cw, [float]$sy)
    if ($major) { $g.DrawString([string][int]$wy, $gf, [System.Drawing.Brushes]::Cyan, 1, [float]($sy + 1)) }
  }
}
if ($Marks -ne "") {
  $mk = Get-Content $Marks -Raw | ConvertFrom-Json
  foreach ($m in $mk) { $x = ($m.x - $c[0]) * $Scale; $y = ($m.y - $c[1]) * $Scale; $g.FillEllipse([System.Drawing.Brushes]::Red, [float]($x - 3), [float]($y - 3), 6, 6); $g.DrawString($m.name, $font, [System.Drawing.Brushes]::Red, [float]($x + 4), [float]($y - 6)) }
}
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $terrain.Dispose()
Write-Output "wrote $Out ($cw x $ch)"

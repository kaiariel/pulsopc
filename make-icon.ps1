# Genera assets\pulso.ico (icono de PulsoPC) con System.Drawing
Add-Type -AssemblyName System.Drawing
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sizes = 256,64,48,32,16
$pngs = @()
foreach ($s in $sizes) {
  $bmp = New-Object System.Drawing.Bitmap $s,$s,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
  $r = [int]($s*0.22)
  $path = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r*2
  $path.AddArc(0,0,$d,$d,180,90); $path.AddArc($s-$d-1,0,$d,$d,270,90); $path.AddArc($s-$d-1,$s-$d-1,$d,$d,0,90); $path.AddArc(0,$s-$d-1,$d,$d,90,90); $path.CloseFigure()
  $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.Point 0,0),(New-Object System.Drawing.Point $s,$s),[System.Drawing.Color]::FromArgb(255,37,99,235),[System.Drawing.Color]::FromArgb(255,6,182,212))
  $g.FillPath($br,$path)
  $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::White), ([single]([math]::Max(1.6,$s*0.075)))
  $pen.LineJoin='Round'; $pen.StartCap='Round'; $pen.EndCap='Round'
  $pts = @( @(0.14,0.54), @(0.34,0.54), @(0.43,0.28), @(0.55,0.76), @(0.64,0.50), @(0.86,0.50) ) | ForEach-Object { New-Object System.Drawing.PointF ([single]($_[0]*$s)),([single]($_[1]*$s)) }
  $g.DrawLines($pen,[System.Drawing.PointF[]]$pts)
  $g.Dispose()
  $ms = New-Object System.IO.MemoryStream; $bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png); $pngs += ,($ms.ToArray()); $bmp.Dispose()
}
$out = New-Object System.IO.MemoryStream; $w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16*$sizes.Count
for ($i=0; $i -lt $sizes.Count; $i++) {
  $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
  $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset); $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
New-Item -ItemType Directory -Force (Join-Path $root 'assets') | Out-Null
[IO.File]::WriteAllBytes((Join-Path $root 'assets\pulso.ico'), $out.ToArray())
"Icono generado"

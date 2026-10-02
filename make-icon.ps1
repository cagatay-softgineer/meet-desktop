Add-Type -AssemblyName System.Drawing
$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $k = $s / 256.0
    # rounded green tile
    $r = 56 * $k; $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $w = $s - 1
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($w - $r, 0, $r, $r, 270, 90)
    $path.AddArc($w - $r, $w - $r, $r, $r, 0, 90); $path.AddArc(0, $w - $r, $r, $r, 90, 90); $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(0, 137, 123))), $path)
    # camera body + lens
    $white = [System.Drawing.Brushes]::White
    $g.FillRectangle($white, [float](48 * $k), [float](80 * $k), [float](112 * $k), [float](96 * $k))
    $g.FillPolygon($white, [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF ([float](160 * $k)), ([float](112 * $k))),
        (New-Object System.Drawing.PointF ([float](212 * $k)), ([float](76 * $k))),
        (New-Object System.Drawing.PointF ([float](212 * $k)), ([float](180 * $k))),
        (New-Object System.Drawing.PointF ([float](160 * $k)), ([float](144 * $k)))))
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
# ICO container with PNG payloads
$out = New-Object System.IO.MemoryStream; $bw = New-Object System.IO.BinaryWriter $out
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$d); $bw.Write([byte]$d); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
[System.IO.File]::WriteAllBytes("$PSScriptRoot\app.ico", $out.ToArray())

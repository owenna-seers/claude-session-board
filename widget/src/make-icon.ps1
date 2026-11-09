# 세션 위젯 아이콘(app.ico): 어두운 둥근 판 위에 상태 점 세 줄 (분홍 · 주황 · 파랑)
param([string]$Out = (Join-Path $PSScriptRoot 'app.ico'))
Add-Type -AssemblyName System.Drawing

function New-Png([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $r = [single]($size * 0.2)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $s = [single]($size - 1)
    $path.AddArc(0, 0, $r, $r, 180, 90); $path.AddArc($s - $r, 0, $r, $r, 270, 90)
    $path.AddArc($s - $r, $s - $r, $r, $r, 0, 90); $path.AddArc(0, $s - $r, $r, $r, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 30, 31, 36))), $path)
    $colors = @(
        [System.Drawing.Color]::FromArgb(255, 255, 122, 162),
        [System.Drawing.Color]::FromArgb(255, 245, 177, 76),
        [System.Drawing.Color]::FromArgb(255, 108, 182, 255)
    )
    $bar = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(200, 255, 255, 255))
    for ($i = 0; $i -lt 3; $i++) {
        $y = [single]($size * (0.22 + $i * 0.24))
        $d = [single]($size * 0.16)
        $g.FillEllipse((New-Object System.Drawing.SolidBrush $colors[$i]), [single]($size * 0.16), $y, $d, $d)
        $g.FillRectangle($bar, [single]($size * 0.40), [single]($y + $d * 0.30), [single]($size * 0.44), [single]($d * 0.40))
    }
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        # 큰 것은 PNG 그대로
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    } else {
        # 작은 것은 옛 BMP(DIB) 형식: 머리 40바이트 + BGRA 아래 줄부터 + AND 마스크
        $bw = New-Object System.IO.BinaryWriter $ms
        $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
        $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
        $maskRow = [int]([math]::Ceiling($size / 32.0) * 4)
        $bw.Write([uint32]($size * $size * 4 + $maskRow * $size))
        $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bmp.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $bmp.Dispose()
    return , $ms.ToArray()
}

$sizes = 16, 24, 32, 48, 64, 256
$pngs = foreach ($z in $sizes) { , (New-Png $z) }
$fs = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $z = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]$(if ($z -ge 256) { 0 } else { $z })); $w.Write([byte]$(if ($z -ge 256) { 0 } else { $z }))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$len); $w.Write([uint32]$offset)
    $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes($Out, $fs.ToArray())
"icon: $Out ($($fs.Length) bytes)"

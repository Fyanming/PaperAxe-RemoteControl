$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$assetsDir = Join-Path $root 'src\ZhifaRemote\Assets'
New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null

function New-RoundedRectPath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

$master = New-Object System.Drawing.Bitmap(512, 512)
$g = [System.Drawing.Graphics]::FromImage($master)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.Clear([System.Drawing.Color]::Transparent)

$dark = [System.Drawing.Color]::FromArgb(255, 14, 20, 27)
$border = [System.Drawing.Color]::FromArgb(64, 255, 255, 255)
$orange = [System.Drawing.Color]::FromArgb(255, 255, 122, 89)
$mint = [System.Drawing.Color]::FromArgb(255, 69, 191, 165)
$white = [System.Drawing.Color]::FromArgb(242, 255, 255, 255)
$wave = [System.Drawing.Color]::FromArgb(190, 159, 232, 216)

$bgPath = New-RoundedRectPath 32 32 448 448 104
$bgBrush = New-Object System.Drawing.SolidBrush($dark)
$g.FillPath($bgBrush, $bgPath)
$borderPen = New-Object System.Drawing.Pen($border, 8)
$g.DrawPath($borderPen, $bgPath)

$sail = @(
    (New-Object System.Drawing.PointF(256, 122)),
    (New-Object System.Drawing.PointF(158, 310)),
    (New-Object System.Drawing.PointF(354, 310))
)
$orangeBrush = New-Object System.Drawing.SolidBrush($orange)
$g.FillPolygon($orangeBrush, $sail)

$fold = @(
    (New-Object System.Drawing.PointF(256, 122)),
    (New-Object System.Drawing.PointF(354, 310)),
    (New-Object System.Drawing.PointF(256, 310))
)
$foldBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(44, 255, 255, 255))
$g.FillPolygon($foldBrush, $fold)

$mastPen = New-Object System.Drawing.Pen($white, 8)
$g.DrawLine($mastPen, 256, 318, 256, 124)

$hull = @(
    (New-Object System.Drawing.PointF(138, 316)),
    (New-Object System.Drawing.PointF(374, 316)),
    (New-Object System.Drawing.PointF(338, 396)),
    (New-Object System.Drawing.PointF(174, 396))
)
$mintBrush = New-Object System.Drawing.SolidBrush($mint)
$g.FillPolygon($mintBrush, $hull)

$deckPen = New-Object System.Drawing.Pen($white, 6)
$g.DrawLine($deckPen, 138, 316, 374, 316)

$waveBrush = New-Object System.Drawing.SolidBrush($wave)
$g.FillEllipse($waveBrush, 170, 410, 16, 10)
$g.FillEllipse($waveBrush, 228, 417, 16, 10)
$g.FillEllipse($waveBrush, 286, 410, 16, 10)

$pngPath = Join-Path $assetsDir 'AppIcon.png'
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngData = @{}
foreach ($size in $sizes) {
    $small = New-Object System.Drawing.Bitmap($size, $size)
    $sg = [System.Drawing.Graphics]::FromImage($small)
    $sg.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $sg.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $sg.DrawImage($master, 0, 0, $size, $size)
    $ms = New-Object System.IO.MemoryStream
    $small.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData[$size] = $ms.ToArray()
    $ms.Dispose()
    $sg.Dispose()
    $small.Dispose()
}

$icoPath = Join-Path $assetsDir 'App.ico'
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

$offset = 6 + 16 * $sizes.Count
foreach ($size in $sizes) {
    $dimension = if ($size -ge 256) { [byte]0 } else { [byte]$size }
    $bw.Write($dimension)
    $bw.Write($dimension)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngData[$size].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngData[$size].Length
}

foreach ($size in $sizes) {
    $bw.Write($pngData[$size])
}

$bw.Flush()
$bw.Close()
$fs.Dispose()

$g.Dispose()
$master.Dispose()

Write-Output "PNG: $pngPath"
Write-Output "ICO: $icoPath"

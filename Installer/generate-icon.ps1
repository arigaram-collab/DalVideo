Add-Type -AssemblyName System.Drawing

$outputPath = Join-Path $PSScriptRoot "..\Assets\app.ico"
$assetsDir = Split-Path $outputPath
if (-not (Test-Path $assetsDir)) { New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null }

$sizes = @(256, 48, 32, 16)

function New-RoundedRectPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    [float]$margin = [Math]::Max(1, [int]($size * 0.04))
    [float]$bgW = $size - $margin * 2
    [float]$bgH = $size - $margin * 2
    [float]$radius = [Math]::Max(2, [int]($size * 0.20))

    # Dark background rounded rectangle
    $bgPath = New-RoundedRectPath $margin $margin $bgW $bgH $radius
    $pt1 = New-Object System.Drawing.PointF(0, 0)
    $pt2 = New-Object System.Drawing.PointF([float]$size, [float]$size)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($pt1, $pt2,
        [System.Drawing.Color]::FromArgb(255, 30, 41, 59),
        [System.Drawing.Color]::FromArgb(255, 15, 23, 42))
    $g.FillPath($bgBrush, $bgPath)

    # Subtle border
    [float]$borderWidth = [Math]::Max(1, [float]($size * 0.02))
    $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(50, 148, 163, 184), $borderWidth)
    $g.DrawPath($borderPen, $bgPath)

    # Red record circle (center)
    [float]$circleSize = [int]($bgW * 0.44)
    [float]$cx = ($size - $circleSize) / 2.0
    [float]$cy = ($size - $circleSize) / 2.0

    # Solid red brush (clean look)
    $redBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 220, 38, 38))
    $g.FillEllipse($redBrush, $cx, $cy, $circleSize, $circleSize)

    # Darker edge ring for depth
    if ($size -ge 32) {
        [float]$ringWidth = [Math]::Max(1.0, $size * 0.015)
        $ringPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 127, 29, 29), $ringWidth)
        $g.DrawEllipse($ringPen, $cx, $cy, $circleSize, $circleSize)
        $ringPen.Dispose()
    }

    # Glow around record button
    if ($size -ge 32) {
        [float]$glowWidth = [Math]::Max(1.0, $size * 0.02)
        $glowPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(35, 239, 68, 68), $glowWidth)
        $g.DrawEllipse($glowPen, ($cx - 2), ($cy - 2), ($circleSize + 4), ($circleSize + 4))
        $glowPen.Dispose()
    }

    # Highlight on record button
    if ($size -ge 32) {
        [float]$hlSize = [int]($circleSize * 0.28)
        [float]$hlX = $cx + $circleSize * 0.22
        [float]$hlY = $cy + $circleSize * 0.15
        $hlBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(65, 255, 255, 255))
        $g.FillEllipse($hlBrush, $hlX, $hlY, $hlSize, $hlSize)
        $hlBrush.Dispose()
    }

    # Cleanup
    $redBrush.Dispose()
    $borderPen.Dispose()
    $bgBrush.Dispose()
    $bgPath.Dispose()
    $g.Dispose()

    return $bmp
}

# Generate PNG byte arrays
$pngList = New-Object System.Collections.ArrayList
foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [void]$pngList.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

# Write ICO file (multi-size, PNG-compressed)
$fs = [System.IO.File]::Create($outputPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
$bw.Write([uint16]0)
$bw.Write([uint16]1)
$bw.Write([uint16]$sizes.Count)

# ICONDIRENTRY for each size
[uint32]$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    [byte[]]$pngBytes = $pngList[$i]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([uint16]1)
    $bw.Write([uint16]32)
    $bw.Write([uint32]$pngBytes.Length)
    $bw.Write([uint32]$dataOffset)
    $dataOffset += $pngBytes.Length
}

# Image data
foreach ($png in $pngList) {
    $bw.Write([byte[]]$png)
}

$bw.Close()
$fs.Close()

Write-Host "Icon created: $outputPath" -ForegroundColor Green
Write-Host "Sizes: $($sizes -join ', ')px" -ForegroundColor Gray

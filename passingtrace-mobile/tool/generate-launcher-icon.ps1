param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

function New-RoundedRectanglePath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Fill-RoundedRectangle {
    param(
        [System.Drawing.Graphics]$Graphics,
        [System.Drawing.Brush]$Brush,
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )
    $path = New-RoundedRectanglePath -X $X -Y $Y -Width $Width -Height $Height -Radius $Radius
    $Graphics.FillPath($Brush, $path)
    $path.Dispose()
}

function New-Point {
    param([float]$X, [float]$Y)
    return [System.Drawing.PointF]::new($X, $Y)
}

function Write-PassingTraceIcon {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][int]$Size
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $bitmap = [System.Drawing.Bitmap]::new($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.ScaleTransform([single]($Size / 108.0), [single]($Size / 108.0))

    $pine = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#2F6B57'))
    $paper = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#FFF6E8'))
    $coral = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#E28A62'))
    $coralStrong = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#D97856'))
    $mint = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A8CCBA'))

    Fill-RoundedRectangle -Graphics $graphics -Brush $pine -X 3 -Y 3 -Width 102 -Height 102 -Radius 28

    $cardState = $graphics.Save()
    $graphics.TranslateTransform(42, 42)
    $graphics.RotateTransform(-8)
    $graphics.TranslateTransform(-42, -42)
    Fill-RoundedRectangle -Graphics $graphics -Brush $paper -X 28 -Y 22 -Width 28 -Height 40 -Radius 5
    $graphics.FillEllipse($coral, 34, 28, 12, 12)
    $graphics.FillPolygon($mint, [System.Drawing.PointF[]]@(
        (New-Point 31 51), (New-Point 39 42), (New-Point 46 48),
        (New-Point 53 40), (New-Point 57 58), (New-Point 31 58)
    ))
    $graphics.Restore($cardState)

    $noteState = $graphics.Save()
    $graphics.TranslateTransform(67, 45)
    $graphics.RotateTransform(7)
    $graphics.TranslateTransform(-67, -45)
    Fill-RoundedRectangle -Graphics $graphics -Brush $coral -X 53 -Y 25 -Width 28 -Height 40 -Radius 5
    Fill-RoundedRectangle -Graphics $graphics -Brush $paper -X 60 -Y 33 -Width 14 -Height 5 -Radius 2.5
    Fill-RoundedRectangle -Graphics $graphics -Brush $paper -X 60 -Y 43 -Width 11 -Height 5 -Radius 2.5
    $graphics.Restore($noteState)

    $graphics.FillPolygon($coralStrong, [System.Drawing.PointF[]]@(
        (New-Point 19 49), (New-Point 89 49), (New-Point 82 63), (New-Point 26 63)
    ))
    $graphics.FillPolygon($paper, [System.Drawing.PointF[]]@(
        (New-Point 25 59), (New-Point 83 59), (New-Point 78 85), (New-Point 30 85)
    ))
    Fill-RoundedRectangle -Graphics $graphics -Brush $coral -X 45 -Y 65 -Width 18 -Height 8 -Radius 4

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $mint.Dispose()
    $coralStrong.Dispose()
    $coral.Dispose()
    $paper.Dispose()
    $pine.Dispose()
    $graphics.Dispose()
    $bitmap.Dispose()
}

$sizes = [ordered]@{
    'mipmap-mdpi' = 48
    'mipmap-hdpi' = 72
    'mipmap-xhdpi' = 96
    'mipmap-xxhdpi' = 144
    'mipmap-xxxhdpi' = 192
}

foreach ($entry in $sizes.GetEnumerator()) {
    $target = Join-Path $ProjectRoot "android/app/src/main/res/$($entry.Key)/ic_launcher.png"
    Write-PassingTraceIcon -Path $target -Size $entry.Value
}

$preview = Join-Path $ProjectRoot 'design/passingtrace-app-icon.png'
Write-PassingTraceIcon -Path $preview -Size 512
Write-Output "Generated memory-box launcher icons and preview at $ProjectRoot"

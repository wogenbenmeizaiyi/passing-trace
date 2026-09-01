param(
    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing.Common

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $diameter = $Radius * 2
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc(
        $X + $Width - $diameter,
        $Y + $Height - $diameter,
        $diameter,
        $diameter,
        0,
        90
    )
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Write-PassingTraceIcon {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [int]$Size
    )

    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null

    $bitmap = [System.Drawing.Bitmap]::new(
        $Size,
        $Size,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb
    )
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $scale = [single]($Size / 108.0)
    $graphics.ScaleTransform($scale, $scale)

    $background = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#2F6B57')
    )
    $backgroundPath = New-RoundedRectanglePath -X 3 -Y 3 -Width 102 -Height 102 -Radius 28
    $graphics.FillPath($background, $backgroundPath)

    $routePath = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $routePath.StartFigure()
    $routePath.AddBezier(32, 35, 42, 27, 56, 28, 64, 36)
    $routePath.AddBezier(64, 36, 72, 44, 68, 54, 56, 56)
    $routePath.AddBezier(56, 56, 43, 58, 38, 65, 45, 73)
    $routePath.AddBezier(45, 73, 52, 80, 65, 79, 76, 69)
    $routePen = [System.Drawing.Pen]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#FFFDF9'),
        7
    )
    $routePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $routePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $routePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $graphics.DrawPath($routePen, $routePath)

    $paper = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#FFFDF9')
    )
    $accent = [System.Drawing.SolidBrush]::new(
        [System.Drawing.ColorTranslator]::FromHtml('#E29A79')
    )
    $graphics.FillEllipse($accent, 24.5, 27.5, 15, 15)
    $graphics.FillEllipse($paper, 29.6, 32.6, 4.8, 4.8)
    $graphics.FillEllipse($paper, 68.5, 61.5, 15, 15)
    $graphics.FillEllipse($background, 73.6, 66.6, 4.8, 4.8)

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)

    $routePen.Dispose()
    $routePath.Dispose()
    $paper.Dispose()
    $accent.Dispose()
    $backgroundPath.Dispose()
    $background.Dispose()
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
Write-Output "Generated launcher icons and preview at $ProjectRoot"

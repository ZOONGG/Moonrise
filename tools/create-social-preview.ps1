param(
    [string]$ScreenshotPath = "docs\images\en\moonrise-launch.png",
    [string]$OutputPath = "docs\images\moonrise-social-preview.png"
)

$ErrorActionPreference = "Stop"
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$screenshot = if ([IO.Path]::IsPathRooted($ScreenshotPath)) { $ScreenshotPath } else { Join-Path $repositoryRoot $ScreenshotPath }
$output = if ([IO.Path]::IsPathRooted($OutputPath)) { $OutputPath } else { Join-Path $repositoryRoot $OutputPath }
$iconPath = Join-Path $repositoryRoot "assets\branding\moonrise-icon-512.png"

Add-Type -AssemblyName System.Drawing

$canvas = [Drawing.Bitmap]::new(1280, 640, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
$graphics = [Drawing.Graphics]::FromImage($canvas)
$source = [Drawing.Image]::FromFile($screenshot)
$icon = [Drawing.Image]::FromFile($iconPath)

try {
    $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([Drawing.Color]::FromArgb(11, 13, 39))

    $scale = [Math]::Max(1280.0 / $source.Width, 640.0 / $source.Height)
    $drawWidth = [int][Math]::Ceiling($source.Width * $scale)
    $drawHeight = [int][Math]::Ceiling($source.Height * $scale)
    $drawX = [int]((1280 - $drawWidth) / 2)
    $drawY = [int]((640 - $drawHeight) / 2)
    $graphics.DrawImage($source, [Drawing.Rectangle]::new($drawX, $drawY, $drawWidth, $drawHeight))

    $overlay = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(174, 7, 8, 24))
    $leftPanel = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Rectangle]::new(0, 0, 900, 640),
        [Drawing.Color]::FromArgb(252, 9, 10, 31),
        [Drawing.Color]::FromArgb(25, 9, 10, 31),
        [Drawing.Drawing2D.LinearGradientMode]::Horizontal)
    try {
        $graphics.FillRectangle($overlay, 0, 0, 1280, 640)
        $graphics.FillRectangle($leftPanel, 0, 0, 900, 640)
    }
    finally {
        $overlay.Dispose()
        $leftPanel.Dispose()
    }

    $graphics.DrawImage($icon, [Drawing.Rectangle]::new(86, 104, 146, 146))

    $titleFont = [Drawing.Font]::new("Segoe UI", 55, [Drawing.FontStyle]::Bold, [Drawing.GraphicsUnit]::Pixel)
    $taglineFont = [Drawing.Font]::new("Segoe UI", 25, [Drawing.FontStyle]::Regular, [Drawing.GraphicsUnit]::Pixel)
    $titleBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(248, 245, 255))
    $taglineBrush = [Drawing.SolidBrush]::new([Drawing.Color]::FromArgb(204, 193, 226))
    $accentBrush = [Drawing.Drawing2D.LinearGradientBrush]::new(
        [Drawing.Rectangle]::new(88, 386, 390, 6),
        [Drawing.Color]::FromArgb(139, 91, 255),
        [Drawing.Color]::FromArgb(86, 221, 225),
        [Drawing.Drawing2D.LinearGradientMode]::Horizontal)
    try {
        $graphics.DrawString("Moonrise", $titleFont, $titleBrush, 82, 274)
        $graphics.DrawString("A modern mod manager and launcher", $taglineFont, $taglineBrush, 87, 360)
        $graphics.DrawString("for Lunar Client.", $taglineFont, $taglineBrush, 87, 397)
        $graphics.FillRectangle($accentBrush, 88, 455, 390, 6)
    }
    finally {
        $titleFont.Dispose()
        $taglineFont.Dispose()
        $titleBrush.Dispose()
        $taglineBrush.Dispose()
        $accentBrush.Dispose()
    }

    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($output))) | Out-Null
    $canvas.Save($output, [Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $icon.Dispose()
    $source.Dispose()
    $graphics.Dispose()
    $canvas.Dispose()
}

Write-Output "Created 1280x640 Moonrise social preview at $output"

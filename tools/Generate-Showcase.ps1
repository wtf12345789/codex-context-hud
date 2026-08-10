[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectDir = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $projectDir 'assets'
$heroPath = Join-Path $assetsDir 'hero.png'
$demoPath = Join-Path $assetsDir 'demo.png'

function New-RoundedPath([Drawing.RectangleF]$Rect, [float]$Radius) {
    $diameter = $Radius * 2
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($Rect.X, $Rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($Rect.Right - $diameter, $Rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($Rect.Right - $diameter, $Rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($Rect.X, $Rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-RoundedBox($Graphics, [Drawing.RectangleF]$Rect, [float]$Radius,
    [Drawing.Color]$Fill, [Drawing.Color]$Stroke) {
    $path = New-RoundedPath $Rect $Radius
    $brush = New-Object Drawing.SolidBrush $Fill
    $pen = New-Object Drawing.Pen $Stroke, 1
    $Graphics.FillPath($brush, $path)
    $Graphics.DrawPath($pen, $path)
    $pen.Dispose(); $brush.Dispose(); $path.Dispose()
}

function Draw-Pill($Graphics, [string]$Text, [float]$X, [float]$Y,
    [Drawing.Color]$Accent, [Drawing.Font]$Font) {
    $size = $Graphics.MeasureString($Text, $Font)
    $rect = [Drawing.RectangleF]::new($X, $Y, $size.Width + 42, 36)
    Draw-RoundedBox $Graphics $rect 18 ([Drawing.Color]::FromArgb(235, 35, 36, 39)) `
        ([Drawing.Color]::FromArgb(255, 58, 59, 64))
    $dot = New-Object Drawing.SolidBrush $Accent
    $Graphics.FillEllipse($dot, $X + 14, $Y + 14, 8, 8)
    $dot.Dispose()
    $textBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(228, 228, 230))
    $Graphics.DrawString($Text, $Font, $textBrush, $X + 28, $Y + 8)
    $textBrush.Dispose()
    return $rect.Right
}

function Draw-Composer($Graphics, [float]$X, [float]$Y, [float]$Width, [float]$Height,
    [Drawing.Font]$UiFont, [Drawing.Font]$UiBold) {
    Draw-RoundedBox $Graphics ([Drawing.RectangleF]::new($X, $Y, $Width, $Height)) 26 `
        ([Drawing.Color]::FromArgb(43, 43, 43)) ([Drawing.Color]::FromArgb(57, 57, 58))

    $muted = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(116, 116, 118))
    $native = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(221, 221, 223))
    $orange = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(243, 105, 36))
    $Graphics.DrawString('Do anything', $UiFont, $muted, $X + 22, $Y + 20)
    $Graphics.DrawString('+', $UiFont, $native, $X + 22, $Y + $Height - 43)
    $Graphics.DrawString('!', $UiBold, $orange, $X + 64, $Y + $Height - 42)
    $Graphics.DrawString('Full access', $UiFont, $orange, $X + 80, $Y + $Height - 42)

    $toolbarY = $Y + $Height - 31
    $modelX = $X + $Width - 260

    # Remaining quota: the exact 18x12, 16x2 renderer geometry.
    $track = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(82, 224, 224, 226))
    $quota = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(134, 165, 142))
    $Graphics.FillRectangle($track, $modelX - 88, $toolbarY + 7, 16, 2)
    $Graphics.FillRectangle($quota, $modelX - 88, $toolbarY + 7, 10, 2)

    # Saturated 10+ compaction state: three black bars with a faint native outline.
    $barBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(23, 23, 23))
    $barPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(72, 255, 255, 255)), .7
    foreach ($offset in @(-58, -53, -48)) {
        $barX = $modelX + $offset
        $Graphics.FillRectangle($barBrush, $barX, $toolbarY + 3, 2, 10)
        $Graphics.DrawRectangle($barPen, $barX, $toolbarY + 3, 2, 10)
    }

    # Codex native context ring.
    $ringPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(145, 151, 151, 153)), 2
    $ringPen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $ringPen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawArc($ringPen, $modelX - 25, $toolbarY + 2, 13, 13, -72, 260)
    $Graphics.DrawString('5.6 Sol', $UiBold, $native, $modelX, $toolbarY - 1)
    $Graphics.DrawString('High', $UiFont, $muted, $modelX + 57, $toolbarY - 1)
    $Graphics.DrawString('⌄', $UiFont, $muted, $modelX + 90, $toolbarY - 1)
    $Graphics.DrawString('◦', $UiFont, $native, $modelX + 136, $toolbarY - 1)
    $Graphics.FillEllipse($native, $X + $Width - 49, $toolbarY - 3, 34, 34)
    $arrow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(61, 61, 63))
    $Graphics.DrawString('↑', $UiBold, $arrow, $X + $Width - 42, $toolbarY + 1)

    $track.Dispose(); $quota.Dispose(); $barBrush.Dispose(); $barPen.Dispose()
    $ringPen.Dispose(); $arrow.Dispose(); $muted.Dispose(); $native.Dispose(); $orange.Dispose()
}

New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
$hero = New-Object Drawing.Bitmap 1280,640
$hero.SetResolution(96, 96)
$g = [Drawing.Graphics]::FromImage($hero)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit

$background = New-Object Drawing.Drawing2D.LinearGradientBrush `
    ([Drawing.Rectangle]::new(0, 0, 1280, 640)), `
    ([Drawing.Color]::FromArgb(20, 20, 21)), `
    ([Drawing.Color]::FromArgb(29, 31, 32)), 18
$g.FillRectangle($background, 0, 0, 1280, 640)
$background.Dispose()
$sageGlow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(18, 134, 165, 142))
$blueGlow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(15, 92, 127, 218))
$g.FillEllipse($sageGlow, -200, 410, 560, 380)
$g.FillEllipse($blueGlow, 900, -260, 600, 600)
$sageGlow.Dispose(); $blueGlow.Dispose()

$eyebrowFont = New-Object Drawing.Font 'Segoe UI Semibold', 13
$titleFont = New-Object Drawing.Font 'Segoe UI Semibold', 46
$subtitleFont = New-Object Drawing.Font 'Segoe UI', 18
$pillFont = New-Object Drawing.Font 'Segoe UI', 10
$uiFont = New-Object Drawing.Font 'Segoe UI', 11
$uiBold = New-Object Drawing.Font 'Segoe UI Semibold', 11
$smallFont = New-Object Drawing.Font 'Segoe UI', 10

$eyebrow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(134, 165, 142))
$title = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(246, 246, 247))
$subtitle = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(174, 174, 178))
$g.DrawString('WINDOWS  ·  CODEX DESKTOP  ·  LOCALHOST', $eyebrowFont, $eyebrow, 80, 62)
$g.DrawString('Codex Context HUD', $titleFont, $title, 76, 98)
$g.DrawString('Context, compactions and quota — inside the native composer.',
    $subtitleFont, $subtitle, 80, 174)

$next = Draw-Pill $g 'Native layout' 80 226 ([Drawing.Color]::FromArgb(119, 151, 230)) $pillFont
$next = Draw-Pill $g 'Session-aware' ($next + 12) 226 ([Drawing.Color]::FromArgb(134, 165, 142)) $pillFont
$null = Draw-Pill $g 'No JSONL scans' ($next + 12) 226 ([Drawing.Color]::FromArgb(212, 187, 111)) $pillFont

Draw-Composer $g 80 342 1120 170 $uiFont $uiBold

# Unified hover card, matching the renderer's compact native styling.
Draw-RoundedBox $g ([Drawing.RectangleF]::new(716, 288, 194, 102)) 12 `
    ([Drawing.Color]::FromArgb(48, 48, 48)) ([Drawing.Color]::FromArgb(76, 76, 77))
$cardMuted = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(166, 166, 168))
$cardText = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(240, 240, 241))
$g.DrawString('SESSION STATS', $smallFont, $cardMuted, 732, 302)
$g.DrawString('Account quota', $smallFont, $cardMuted, 732, 329)
$g.DrawString('64%', $uiBold, $cardText, 858, 328)
$g.DrawString('Compactions', $smallFont, $cardMuted, 732, 354)
$g.DrawString('28', $uiBold, $cardText, 863, 353)
$cardMuted.Dispose(); $cardText.Dispose()

$footer = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(134, 134, 138))
$g.DrawString('Native context ring  ·  stable compaction stages  ·  primary quota at a glance',
    $smallFont, $footer, 80, 566)
$footer.Dispose(); $eyebrow.Dispose(); $title.Dispose(); $subtitle.Dispose()
$eyebrowFont.Dispose(); $titleFont.Dispose(); $subtitleFont.Dispose(); $pillFont.Dispose()
$uiFont.Dispose(); $uiBold.Dispose(); $smallFont.Dispose()
$g.Dispose()
$hero.Save($heroPath, [Drawing.Imaging.ImageFormat]::Png)
$hero.Dispose()

# Keep a sanitized composer-only asset for downstream demos.
$demo = New-Object Drawing.Bitmap 1280,220
$dg = [Drawing.Graphics]::FromImage($demo)
$dg.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$dg.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$dg.Clear([Drawing.Color]::FromArgb(24, 24, 25))
$demoUi = New-Object Drawing.Font 'Segoe UI', 11
$demoBold = New-Object Drawing.Font 'Segoe UI Semibold', 11
Draw-Composer $dg 60 25 1160 170 $demoUi $demoBold
$demoUi.Dispose(); $demoBold.Dispose(); $dg.Dispose()
$demo.Save($demoPath, [Drawing.Imaging.ImageFormat]::Png)
$demo.Dispose()

Write-Host "Generated: $heroPath"
Write-Host "Generated: $demoPath"

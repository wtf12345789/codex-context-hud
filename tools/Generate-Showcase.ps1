[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectDir = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $projectDir 'assets'
$demoPath = Join-Path $assetsDir 'demo.png'
$heroPath = Join-Path $assetsDir 'hero.png'
$gifPath = Join-Path $assetsDir 'motion-demo.gif'
$fontRegular = Join-Path $env:WINDIR 'Fonts\segoeui.ttf'
$fontBold = Join-Path $env:WINDIR 'Fonts\segoeuib.ttf'
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source

if (-not (Test-Path -LiteralPath $demoPath)) { throw 'assets\demo.png 不存在。' }

function New-RoundedPath([Drawing.RectangleF]$rect, [float]$radius) {
    $diameter = $radius * 2
    $path = New-Object Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $diameter, $diameter, 180, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Y, $diameter, $diameter, 270, 90)
    $path.AddArc($rect.Right - $diameter, $rect.Bottom - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-RoundedBox($graphics, [Drawing.RectangleF]$rect, [float]$radius,
    [Drawing.Color]$fill, [Drawing.Color]$stroke) {
    $path = New-RoundedPath $rect $radius
    $brush = New-Object Drawing.SolidBrush $fill
    $pen = New-Object Drawing.Pen $stroke, 1
    $graphics.FillPath($brush, $path)
    $graphics.DrawPath($pen, $path)
    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
}

function Draw-Pill($graphics, [string]$text, [float]$x, [float]$y,
    [Drawing.Color]$accent, $font) {
    $size = $graphics.MeasureString($text, $font)
    $rect = [Drawing.RectangleF]::new($x, $y, $size.Width + 42, 38)
    Draw-RoundedBox $graphics $rect 19 ([Drawing.Color]::FromArgb(235, 29, 31, 38)) `
        ([Drawing.Color]::FromArgb(255, 51, 55, 67))
    $dotBrush = New-Object Drawing.SolidBrush $accent
    $graphics.FillEllipse($dotBrush, $x + 14, $y + 15, 8, 8)
    $dotBrush.Dispose()
    $textBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(232, 236, 242))
    $graphics.DrawString($text, $font, $textBrush, $x + 28, $y + 9)
    $textBrush.Dispose()
    return $rect.Right
}

New-Item -ItemType Directory -Path $assetsDir -Force | Out-Null
$privateFonts = New-Object Drawing.Text.PrivateFontCollection
$privateFonts.AddFontFile($fontRegular)
$privateFonts.AddFontFile($fontBold)
$regularFamily = $privateFonts.Families | Select-Object -First 1
$boldFamily = $privateFonts.Families | Select-Object -Last 1

$hero = New-Object Drawing.Bitmap 1280,640
$hero.SetResolution(96,96)
$g = [Drawing.Graphics]::FromImage($hero)
$g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
$g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$background = New-Object Drawing.Drawing2D.LinearGradientBrush `
    ([Drawing.Rectangle]::new(0,0,1280,640)), `
    ([Drawing.Color]::FromArgb(14,15,19)), `
    ([Drawing.Color]::FromArgb(24,27,35)), 18
$g.FillRectangle($background, 0, 0, 1280, 640)
$background.Dispose()

$blueGlow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(22, 91, 130, 230))
$greenGlow = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(18, 73, 183, 126))
$g.FillEllipse($blueGlow, 850, -260, 620, 620)
$g.FillEllipse($greenGlow, -180, 430, 520, 360)
$blueGlow.Dispose(); $greenGlow.Dispose()

$eyebrowFont = New-Object Drawing.Font $boldFamily, 13, ([Drawing.FontStyle]::Bold)
$titleFont = New-Object Drawing.Font $boldFamily, 48, ([Drawing.FontStyle]::Bold)
$subtitleFont = New-Object Drawing.Font $regularFamily, 19, ([Drawing.FontStyle]::Regular)
$pillFont = New-Object Drawing.Font $regularFamily, 11, ([Drawing.FontStyle]::Regular)
$smallFont = New-Object Drawing.Font $regularFamily, 11, ([Drawing.FontStyle]::Regular)

$eyebrowBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(119,151,230))
$titleBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(246,247,250))
$subtitleBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(171,176,188))
$g.DrawString('WINDOWS  ·  LOCAL  ·  READ-ONLY', $eyebrowFont, $eyebrowBrush, 80, 68)
$g.DrawString('Codex Context HUD', $titleFont, $titleBrush, 76, 105)
$g.DrawString('Context usage and compaction count — right beside the Codex composer.',
    $subtitleFont, $subtitleBrush, 80, 184)

$nextX = Draw-Pill $g 'Active-task aware' 80 238 ([Drawing.Color]::FromArgb(119,151,230)) $pillFont
$nextX = Draw-Pill $g 'No injection' ($nextX + 12) 238 ([Drawing.Color]::FromArgb(91,178,122)) $pillFont
$null = Draw-Pill $g 'Zero network calls' ($nextX + 12) 238 ([Drawing.Color]::FromArgb(217,168,83)) $pillFont

$shadow = [Drawing.RectangleF]::new(62, 332, 1156, 205)
Draw-RoundedBox $g $shadow 24 ([Drawing.Color]::FromArgb(110,0,0,0)) `
    ([Drawing.Color]::FromArgb(0,0,0,0))
$frame = [Drawing.RectangleF]::new(70, 324, 1140, 205)
Draw-RoundedBox $g $frame 22 ([Drawing.Color]::FromArgb(255,20,21,25)) `
    ([Drawing.Color]::FromArgb(255,53,57,67))

$demo = [Drawing.Image]::FromFile($demoPath)
$inner = [Drawing.RectangleF]::new(82, 343, 1116, 163)
$g.DrawImage($demo, $inner)
$demo.Dispose()

$footerBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(132,138,151))
$g.DrawString('Compactions  ·  Context pressure  ·  Native sidebar tracking',
    $smallFont, $footerBrush, 80, 572)

$footerBrush.Dispose(); $eyebrowBrush.Dispose(); $titleBrush.Dispose(); $subtitleBrush.Dispose()
$eyebrowFont.Dispose(); $titleFont.Dispose(); $subtitleFont.Dispose(); $pillFont.Dispose(); $smallFont.Dispose()
$g.Dispose()
$hero.Save($heroPath, [Drawing.Imaging.ImageFormat]::Png)
$hero.Dispose()

# Deterministic motion demo built from the sanitized real composer capture.
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-showcase-{0}" -f [Guid]::NewGuid())
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
try {
    $demo = [Drawing.Image]::FromFile($demoPath)
    $frameCount = 84
    for ($i = 0; $i -lt $frameCount; $i++) {
        if ($i -lt 12) { $progress = 0 }
        elseif ($i -lt 36) {
            $t = ($i - 12) / 23.0
            $progress = 1 - [Math]::Pow(1 - $t, 4)
        }
        elseif ($i -lt 48) { $progress = 1 }
        elseif ($i -lt 72) {
            $t = ($i - 48) / 23.0
            $progress = [Math]::Pow(1 - $t, 4)
        }
        else { $progress = 0 }

        $frame = New-Object Drawing.Bitmap 960,360
        $fg = [Drawing.Graphics]::FromImage($frame)
        $fg.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $fg.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $fg.Clear([Drawing.Color]::FromArgb(22,23,27))

        $panelWidth = [int][Math]::Round(190 * $progress)
        $shift = [int][Math]::Round(-95 * $progress)
        $fg.DrawImage($demo, [Drawing.Rectangle]::new(30 + $shift, 130, 900, 132))
        if ($panelWidth -gt 0) {
            $panelBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(35,36,42))
            $borderPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(57,60,70)), 1
            $fg.FillRectangle($panelBrush, 960 - $panelWidth, 0, $panelWidth, 360)
            $fg.DrawLine($borderPen, 960 - $panelWidth, 0, 960 - $panelWidth, 360)
            $borderPen.Dispose(); $panelBrush.Dispose()
        }

        $labelFont = New-Object Drawing.Font $boldFamily, 18, ([Drawing.FontStyle]::Bold)
        $noteFont = New-Object Drawing.Font $regularFamily, 11, ([Drawing.FontStyle]::Regular)
        $labelBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(239,241,246))
        $noteBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(148,154,168))
        $fg.DrawString('HUD follows the Codex composer', $labelFont, $labelBrush, 42, 42)
        $fg.DrawString('Monotonic DWM-synchronized movement · content animations paused',
            $noteFont, $noteBrush, 44, 78)
        $labelBrush.Dispose(); $noteBrush.Dispose(); $labelFont.Dispose(); $noteFont.Dispose()
        $fg.Dispose()
        $frame.Save((Join-Path $tempRoot ("frame-{0:D3}.png" -f $i)),
            [Drawing.Imaging.ImageFormat]::Png)
        $frame.Dispose()
    }
    $demo.Dispose()

    & $ffmpeg -hide_banner -loglevel error -y -framerate 30 `
        -i (Join-Path $tempRoot 'frame-%03d.png') `
        -vf 'fps=30,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=sierra2_4a' `
        -loop 0 $gifPath
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg 生成 GIF 失败：$LASTEXITCODE" }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemp).StartsWith('CodexContextHUD-showcase-')) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Generated: $heroPath"
Write-Host "Generated: $gifPath"

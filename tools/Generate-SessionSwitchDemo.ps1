[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$projectDir = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $projectDir 'assets'
$demoPath = Join-Path $assetsDir 'demo.png'
$outputPath = Join-Path $assetsDir 'session-switch-demo.gif'
$ffmpeg = (Get-Command ffmpeg -ErrorAction Stop).Source

if (-not (Test-Path -LiteralPath $demoPath)) {
    throw 'assets\demo.png is missing.'
}

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
    $fillBrush = New-Object Drawing.SolidBrush $Fill
    $strokePen = New-Object Drawing.Pen $Stroke, 1
    $Graphics.FillPath($fillBrush, $path)
    $Graphics.DrawPath($strokePen, $path)
    $strokePen.Dispose()
    $fillBrush.Dispose()
    $path.Dispose()
}

function Get-CubicBezier([double]$Progress, [double]$X1, [double]$Y1,
    [double]$X2, [double]$Y2) {
    $progress = [Math]::Max(0, [Math]::Min(1, $Progress))
    $value = $progress
    for ($i = 0; $i -lt 6; $i++) {
        $inverse = 1 - $value
        $x = 3 * $inverse * $inverse * $value * $X1 +
            3 * $inverse * $value * $value * $X2 + $value * $value * $value
        $derivative = 3 * $inverse * $inverse * $X1 +
            6 * $inverse * $value * ($X2 - $X1) +
            3 * $value * $value * (1 - $X2)
        if ([Math]::Abs($derivative) -lt 0.000001) { break }
        $value = [Math]::Max(0, [Math]::Min(1, $value - ($x - $progress) / $derivative))
    }
    $remaining = 1 - $value
    return 3 * $remaining * $remaining * $value * $Y1 +
        3 * $remaining * $value * $value * $Y2 + $value * $value * $value
}

function Get-BlendColor([Drawing.Color]$From, [Drawing.Color]$To, [double]$Amount) {
    $amount = [Math]::Max(0, [Math]::Min(1, $Amount))
    return [Drawing.Color]::FromArgb(
        [int][Math]::Round($From.R + ($To.R - $From.R) * $amount),
        [int][Math]::Round($From.G + ($To.G - $From.G) * $amount),
        [int][Math]::Round($From.B + ($To.B - $From.B) * $amount))
}

function Get-CompressionColor([int]$Count) {
    if ($Count -ge 3) { return [Drawing.Color]::FromArgb(224, 104, 104) }
    if ($Count -eq 2) { return [Drawing.Color]::FromArgb(217, 168, 83) }
    return [Drawing.Color]::FromArgb(119, 151, 230)
}

function Get-ContextColor([int]$Percent) {
    if ($Percent -ge 85) { return [Drawing.Color]::FromArgb(224, 104, 104) }
    if ($Percent -ge 70) { return [Drawing.Color]::FromArgb(217, 168, 83) }
    return [Drawing.Color]::FromArgb(91, 178, 122)
}

function Get-RemainingColor([int]$Percent) {
    if ($Percent -lt 0) { return [Drawing.Color]::FromArgb(111, 115, 123) }
    if ($Percent -le 15) { return [Drawing.Color]::FromArgb(224, 104, 104) }
    if ($Percent -le 30) { return [Drawing.Color]::FromArgb(217, 168, 83) }
    return [Drawing.Color]::FromArgb(91, 178, 122)
}

function Get-MetricFrame([double]$Progress) {
    $eased = Get-CubicBezier $Progress .2 .8 .2 1
    if ($eased -le .62) {
        $local = $eased / .62
        return @{
            Y = 7 - 8 * $local
            Opacity = .18 + .82 * $local
            Glow = .35 - .16 * $local
        }
    }
    $local = ($eased - .62) / .38
    return @{
        Y = -1 + $local
        Opacity = 1
        Glow = .19 * (1 - $local)
    }
}

function Draw-MetricText($Graphics, [string]$Text, [Drawing.Font]$Font,
    [Drawing.RectangleF]$Bounds, [Drawing.Color]$Color, [double]$Progress,
    [bool]$Animate, [Drawing.Color]$Background) {
    $drawY = $Bounds.Y
    $drawColor = $Color
    if ($Animate) {
        $frame = Get-MetricFrame $Progress
        $lit = Get-BlendColor $Color ([Drawing.Color]::White) $frame.Glow
        $drawColor = Get-BlendColor $Background $lit $frame.Opacity
        $drawY += $frame.Y
    }
    $brush = New-Object Drawing.SolidBrush $drawColor
    $format = New-Object Drawing.StringFormat
    $format.Alignment = [Drawing.StringAlignment]::Near
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $format.FormatFlags = [Drawing.StringFormatFlags]::NoWrap
    $Graphics.DrawString($Text, $Font, $brush,
        [Drawing.RectangleF]::new($Bounds.X, $drawY, $Bounds.Width, $Bounds.Height), $format)
    $format.Dispose()
    $brush.Dispose()
}

function Draw-CompressionIcon($Graphics, [float]$CenterX, [float]$CenterY,
    [Drawing.Color]$Color, [double]$Spin, [double]$Scale) {
    $state = $Graphics.Save()
    $Graphics.TranslateTransform($CenterX, $CenterY)
    $Graphics.ScaleTransform([float]$Scale, [float]$Scale)
    $Graphics.RotateTransform([float]$Spin)
    $Graphics.TranslateTransform(-$CenterX, -$CenterY)
    $pen = New-Object Drawing.Pen $Color, 2
    $pen.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawArc($pen, $CenterX - 7, $CenterY - 7, 14, 14, 25, 125)
    $Graphics.DrawArc($pen, $CenterX - 7, $CenterY - 7, 14, 14, 205, 125)
    $brush = New-Object Drawing.SolidBrush $Color
    $Graphics.FillPolygon($brush, [Drawing.PointF[]]@(
        [Drawing.PointF]::new($CenterX - 8, $CenterY - 2),
        [Drawing.PointF]::new($CenterX - 8, $CenterY - 7),
        [Drawing.PointF]::new($CenterX - 3, $CenterY - 5)))
    $Graphics.FillPolygon($brush, [Drawing.PointF[]]@(
        [Drawing.PointF]::new($CenterX + 8, $CenterY + 2),
        [Drawing.PointF]::new($CenterX + 8, $CenterY + 7),
        [Drawing.PointF]::new($CenterX + 3, $CenterY + 5)))
    $brush.Dispose()
    $pen.Dispose()
    $Graphics.Restore($state)
}

function Draw-HudMetrics($Graphics, [int]$Compression, [int]$Context,
    [int]$FromContext, [double]$AnimationElapsedMs, [bool]$Animate,
    [Drawing.Font]$LabelFont, [Drawing.Font]$ValueFont) {
    $background = [Drawing.Color]::FromArgb(45, 45, 45)
    $labelColor = [Drawing.Color]::FromArgb(190, 190, 194)
    $compressionColor = Get-CompressionColor $Compression
    $contextColor = Get-ContextColor $Context

    $displayedContext = $Context
    $remaining = if ($Context -lt 0) { -1 } else { 100 - $Context }
    $spin = 0.0
    $scale = 1.0
    if ($Animate) {
        if ($AnimationElapsedMs -lt 320) {
            $eased = Get-CubicBezier ($AnimationElapsedMs / 320) .4 0 .6 1
            $displayedContext = [int][Math]::Round($FromContext * (1 - $eased))
        }
        elseif ($AnimationElapsedMs -lt 390) {
            $displayedContext = 0
        }
        else {
            $eased = Get-CubicBezier ([Math]::Min(1, ($AnimationElapsedMs - 390) / 420)) .18 .85 .28 1
            $displayedContext = [int][Math]::Round($Context * $eased)
        }

        $icon = Get-CubicBezier ([Math]::Min(1, $AnimationElapsedMs / 760)) .2 .8 .2 1
        if ($icon -le .58) {
            $local = $icon / .58
            $spin = 250 * $local
            $scale = 1 + .1 * $local
        }
        else {
            $local = ($icon - .58) / .42
            $spin = 250 + 110 * $local
            $scale = 1.1 - .1 * $local
        }
    }

    $metricBackground = New-Object Drawing.SolidBrush $background
    $Graphics.FillRectangle($metricBackground, 379, 293, 286, 34)
    $metricBackground.Dispose()
    Draw-CompressionIcon $Graphics 394 310 $compressionColor $spin $scale

    $labelBrush = New-Object Drawing.SolidBrush $labelColor
    $labelFormat = New-Object Drawing.StringFormat
    $labelFormat.LineAlignment = [Drawing.StringAlignment]::Center
    $labelFormat.FormatFlags = [Drawing.StringFormatFlags]::NoWrap
    $compressionLabel = ([char]0x538B).ToString() + [char]0x7F29
    $contextLabel = ([char]0x4E0A).ToString() + [char]0x4E0B + [char]0x6587
    $Graphics.DrawString($compressionLabel, $LabelFont, $labelBrush,
        [Drawing.RectangleF]::new(407, 293, 36, 34), $labelFormat)
    Draw-MetricText $Graphics $Compression.ToString() $ValueFont `
        ([Drawing.RectangleF]::new(443, 293, 28, 34)) $compressionColor `
        ([Math]::Min(1, $AnimationElapsedMs / 500)) $Animate $background

    $divider = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(72, 72, 76)), 1
    $Graphics.DrawLine($divider, 472, 300, 472, 320)
    $divider.Dispose()

    $ringRect = [Drawing.RectangleF]::new(485, 301, 18, 18)
    $ringTrack = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(78, 78, 82)), 3
    $Graphics.DrawEllipse($ringTrack, $ringRect)
    $ringTrack.Dispose()
    $ringColor = if ($Animate -and $AnimationElapsedMs -lt 390) {
        Get-ContextColor $FromContext
    } else { $contextColor }
    $ringValue = New-Object Drawing.Pen $ringColor, 3
    $ringValue.StartCap = [Drawing.Drawing2D.LineCap]::Round
    $ringValue.EndCap = [Drawing.Drawing2D.LineCap]::Round
    $Graphics.DrawArc($ringValue, $ringRect, -90, $displayedContext * 3.6)
    $ringValue.Dispose()

    $Graphics.DrawString($contextLabel, $LabelFont, $labelBrush,
        [Drawing.RectangleF]::new(510, 293, 50, 34), $labelFormat)
    $contextProgress = [Math]::Max(0, [Math]::Min(1, ($AnimationElapsedMs - 390) / 520))
    Draw-MetricText $Graphics ("{0}%" -f $Context) $ValueFont `
        ([Drawing.RectangleF]::new(557, 293, 42, 34)) $contextColor $contextProgress `
        ($Animate -and $AnimationElapsedMs -ge 390) $background

    $divider = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(72, 72, 76)), 1
    $Graphics.DrawLine($divider, 604, 300, 604, 320)
    $divider.Dispose()
    $remainingColor = Get-RemainingColor $remaining
    $remainingLabel = ([char]0x4F59).ToString() + [char]0x91CF
    $Graphics.DrawString($remainingLabel, $LabelFont, $labelBrush,
        [Drawing.RectangleF]::new(612, 293, 36, 34), $labelFormat)
    $remainingProgress = [Math]::Max(0, [Math]::Min(1, ($AnimationElapsedMs - 390) / 520))
    Draw-MetricText $Graphics ("{0}%" -f $remaining) $ValueFont `
        ([Drawing.RectangleF]::new(650, 293, 40, 34)) $remainingColor $remainingProgress `
        ($Animate -and $AnimationElapsedMs -ge 390) $background
    $labelFormat.Dispose()
    $labelBrush.Dispose()
}

function Draw-TaskChip($Graphics, [Drawing.RectangleF]$Rect, [string]$Title,
    [bool]$Selected, [Drawing.Font]$Font) {
    $fill = if ($Selected) {
        [Drawing.Color]::FromArgb(43, 46, 55)
    } else { [Drawing.Color]::FromArgb(26, 28, 34) }
    $stroke = if ($Selected) {
        [Drawing.Color]::FromArgb(91, 111, 162)
    } else { [Drawing.Color]::FromArgb(47, 50, 59) }
    Draw-RoundedBox $Graphics $Rect 12 $fill $stroke
    $dotColor = if ($Selected) {
        [Drawing.Color]::FromArgb(119, 151, 230)
    } else { [Drawing.Color]::FromArgb(93, 97, 108) }
    $dotBrush = New-Object Drawing.SolidBrush $dotColor
    $Graphics.FillEllipse($dotBrush, $Rect.X + 16, $Rect.Y + 15, 8, 8)
    $dotBrush.Dispose()
    $textBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(224, 227, 234))
    $format = New-Object Drawing.StringFormat
    $format.LineAlignment = [Drawing.StringAlignment]::Center
    $Graphics.DrawString($Title, $Font, $textBrush,
        [Drawing.RectangleF]::new($Rect.X + 34, $Rect.Y, $Rect.Width - 42, $Rect.Height), $format)
    $format.Dispose()
    $textBrush.Dispose()
}

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-session-demo-{0}" -f [Guid]::NewGuid())
New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null

try {
    $demo = [Drawing.Image]::FromFile($demoPath)
    $titleFont = New-Object Drawing.Font 'Segoe UI Semibold', 22, ([Drawing.FontStyle]::Bold)
    $subtitleFont = New-Object Drawing.Font 'Segoe UI', 11, ([Drawing.FontStyle]::Regular)
    $chipFont = New-Object Drawing.Font 'Segoe UI Semibold', 10, ([Drawing.FontStyle]::Bold)
    $labelFont = New-Object Drawing.Font 'Microsoft YaHei UI', 9, ([Drawing.FontStyle]::Regular)
    $valueFont = New-Object Drawing.Font 'Segoe UI Semibold', 9, ([Drawing.FontStyle]::Bold)

    $frameCount = 180
    for ($i = 0; $i -lt $frameCount; $i++) {
        $frame = New-Object Drawing.Bitmap 960,420
        $g = [Drawing.Graphics]::FromImage($frame)
        $g.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $g.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.TextRenderingHint = [Drawing.Text.TextRenderingHint]::ClearTypeGridFit

        $background = New-Object Drawing.Drawing2D.LinearGradientBrush `
            ([Drawing.Rectangle]::new(0,0,960,420)), `
            ([Drawing.Color]::FromArgb(14,15,19)), `
            ([Drawing.Color]::FromArgb(24,27,35)), 12
        $g.FillRectangle($background, 0, 0, 960, 420)
        $background.Dispose()

        $titleBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(243,245,249))
        $subtitleBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(151,157,171))
        $g.DrawString('Active task tracking', $titleFont, $titleBrush, 38, 28)
        $g.DrawString('Switch tasks - the HUD confirms the active session, then makes every value change visible.',
            $subtitleFont, $subtitleBrush, 40, 67)
        $titleBrush.Dispose(); $subtitleBrush.Dispose()

        $selectedFresh = $i -ge 48 -and $i -lt 128
        Draw-TaskChip $g ([Drawing.RectangleF]::new(40, 108, 190, 40)) 'LONG CONTEXT' (-not $selectedFresh) $chipFont
        Draw-TaskChip $g ([Drawing.RectangleF]::new(242, 108, 190, 40)) 'FRESH TASK' $selectedFresh $chipFont

        # A subtle cursor/click cue makes the task change unambiguous without exposing real task names.
        if ($i -ge 30 -and $i -le 52) {
            $move = Get-CubicBezier (($i - 30) / 22.0) .2 .8 .2 1
            $cursorX = 211 + (413 - 211) * $move
            $cursorY = 119
        }
        elseif ($i -ge 110 -and $i -le 132) {
            $move = Get-CubicBezier (($i - 110) / 22.0) .2 .8 .2 1
            $cursorX = 413 + (211 - 413) * $move
            $cursorY = 119
        }
        else {
            $cursorX = if ($selectedFresh) { 413 } else { 211 }
            $cursorY = 119
        }
        $cursorBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(238, 241, 247))
        $cursorOutline = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(62, 65, 74)), 1
        $cursorPoints = [Drawing.PointF[]]@(
            [Drawing.PointF]::new($cursorX, $cursorY),
            [Drawing.PointF]::new($cursorX + 3, $cursorY + 14),
            [Drawing.PointF]::new($cursorX + 7, $cursorY + 9),
            [Drawing.PointF]::new($cursorX + 12, $cursorY + 14),
            [Drawing.PointF]::new($cursorX + 15, $cursorY + 11),
            [Drawing.PointF]::new($cursorX + 10, $cursorY + 6))
        $g.FillPolygon($cursorBrush, $cursorPoints)
        $g.DrawPolygon($cursorOutline, $cursorPoints)
        $cursorOutline.Dispose()
        $cursorBrush.Dispose()
        if (($i -ge 46 -and $i -le 51) -or ($i -ge 126 -and $i -le 131)) {
            $clickStart = if ($i -lt 90) { 46 } else { 126 }
            $clickProgress = ($i - $clickStart) / 5.0
            $clickPen = New-Object Drawing.Pen ([Drawing.Color]::FromArgb(
                [int][Math]::Round(190 * (1 - $clickProgress)), 119, 151, 230)), 2
            $radius = 7 + 11 * $clickProgress
            $clickCenterX = $cursorX + 4
            $clickCenterY = $cursorY + 4
            $g.DrawEllipse($clickPen, $clickCenterX - $radius, $clickCenterY - $radius,
                $radius * 2, $radius * 2)
            $clickPen.Dispose()
        }

        $statusBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(116, 123, 138))
        $statusText = if ($selectedFresh) { 'ACTIVE  /  FRESH TASK' } else { 'ACTIVE  /  LONG CONTEXT' }
        $g.DrawString($statusText, $chipFont, $statusBrush, 452, 118)
        $statusBrush.Dispose()

        Draw-RoundedBox $g ([Drawing.RectangleF]::new(24, 182, 912, 188)) 20 `
            ([Drawing.Color]::FromArgb(18,19,23)) ([Drawing.Color]::FromArgb(49,52,61))
        $g.DrawImage($demo, [Drawing.RectangleF]::new(30, 205, 900, 132))

        if ($i -lt 48) {
            Draw-HudMetrics $g 11 69 69 999 $false $labelFont $valueFont
        }
        elseif ($i -lt 55) {
            Draw-HudMetrics $g 11 69 69 999 $false $labelFont $valueFont
        }
        elseif ($i -lt 128) {
            $elapsed = [Math]::Min(910, ($i - 55) * (1000 / 30.0))
            Draw-HudMetrics $g 1 24 69 $elapsed ($elapsed -lt 910) $labelFont $valueFont
        }
        elseif ($i -lt 135) {
            Draw-HudMetrics $g 1 24 24 999 $false $labelFont $valueFont
        }
        else {
            $elapsed = [Math]::Min(910, ($i - 135) * (1000 / 30.0))
            Draw-HudMetrics $g 11 69 24 $elapsed ($elapsed -lt 910) $labelFont $valueFont
        }

        $footBrush = New-Object Drawing.SolidBrush ([Drawing.Color]::FromArgb(116,123,138))
        $g.DrawString('Compaction digit flips in  /  context ring drains and refills  /  icon rotates once',
            $subtitleFont, $footBrush, 40, 382)
        $footBrush.Dispose()

        $g.Dispose()
        $frame.Save((Join-Path $tempRoot ("frame-{0:D3}.png" -f $i)),
            [Drawing.Imaging.ImageFormat]::Png)
        $frame.Dispose()
    }

    $titleFont.Dispose(); $subtitleFont.Dispose(); $chipFont.Dispose()
    $labelFont.Dispose(); $valueFont.Dispose(); $demo.Dispose()

    & $ffmpeg -hide_banner -loglevel error -y -framerate 30 `
        -i (Join-Path $tempRoot 'frame-%03d.png') `
        -vf 'fps=24,split[s0][s1];[s0]palettegen=max_colors=96:stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle' `
        -gifflags +transdiff -loop 0 $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "ffmpeg failed to generate the session switch GIF: $LASTEXITCODE"
    }
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemp).StartsWith('CodexContextHUD-session-demo-')) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Generated: $outputPath"

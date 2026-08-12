[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CodexContextHUD'),
    [ValidateRange(1024, 65535)]
    [int]$Port = 9231,
    [switch]$NoStartMenu
)

$ErrorActionPreference = 'Stop'
$targetExe = Join-Path $InstallDir 'CodexContextHUD.exe'
$targetLauncher = Join-Path $InstallDir 'Launch-CodexWithHUD.ps1'
$targetIcon = Join-Path $InstallDir 'Codex.ico'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Context HUD.lnk'
$programsDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Context HUD'
$launcherShortcut = Join-Path $programsDir 'Codex with Context HUD.lnk'
$taskbarShortcut = Join-Path $env:APPDATA `
    'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Codex with Context HUD.lnk'
$payload = @(
    'CodexContextHUD.exe',
    'Launch-CodexWithHUD.ps1',
    'Uninstall.ps1',
    'README.md',
    'README.en.md',
    'CHANGELOG.md',
    'LICENSE'
)

foreach ($name in $payload) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $name))) {
        throw "安装包中缺少 $name。"
    }
}

# 更新时只退出安装目录里的 HUD；绝不关闭或重启 Codex。
$restartHud = $false
Get-CimInstance Win32_Process -Filter "Name='CodexContextHUD.exe'" -ErrorAction SilentlyContinue |
    ForEach-Object {
        try {
            if ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($targetExe)) {
                $restartHud = $true
                Stop-Process -Id $_.ProcessId -Force
                Wait-Process -Id $_.ProcessId -Timeout 3 -ErrorAction SilentlyContinue
            }
        } catch { }
    }

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
foreach ($name in $payload) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) `
        -Destination (Join-Path $InstallDir $name) -Force
}

$shell = New-Object -ComObject WScript.Shell
# 新版不再登录即启动 HUD；更新时清除本工具创建的旧启动项。
if (Test-Path -LiteralPath $startupShortcut) {
    try {
        $legacyShortcut = $shell.CreateShortcut($startupShortcut)
        if ([IO.Path]::GetFullPath($legacyShortcut.TargetPath) -eq
            [IO.Path]::GetFullPath($targetExe)) {
            Remove-Item -LiteralPath $startupShortcut -Force
        }
    } catch { }
}

if (-not $NoStartMenu) {
    $package = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue |
        Sort-Object Version -Descending | Select-Object -First 1
    $codexExe = if ($package) {
        Join-Path $package.InstallLocation 'app\ChatGPT.exe'
    } else {
        $null
    }
    $codexIconPng = if ($package) {
        Join-Path $package.InstallLocation `
            'assets\Square44x44Logo.targetsize-256_altform-unplated.png'
    } else {
        $null
    }
    if ($codexIconPng -and (Test-Path -LiteralPath $codexIconPng)) {
        $pngBytes = [IO.File]::ReadAllBytes($codexIconPng)
        $stream = [IO.File]::Create($targetIcon)
        try {
            $writer = New-Object IO.BinaryWriter($stream)
            try {
                $writer.Write([UInt16]0)                # Reserved
                $writer.Write([UInt16]1)                # ICO
                $writer.Write([UInt16]1)                # One image
                $writer.Write([Byte]0)                  # 256 px width
                $writer.Write([Byte]0)                  # 256 px height
                $writer.Write([Byte]0)                  # Color count
                $writer.Write([Byte]0)                  # Reserved
                $writer.Write([UInt16]1)                # Planes
                $writer.Write([UInt16]32)               # Bits per pixel
                $writer.Write([UInt32]$pngBytes.Length)
                $writer.Write([UInt32]22)               # Header + directory
                $writer.Write($pngBytes)
            } finally {
                $writer.Dispose()
            }
        } finally {
            $stream.Dispose()
        }
    } elseif ($codexExe -and (Test-Path -LiteralPath $codexExe)) {
        Add-Type -AssemblyName System.Drawing
        $icon = [Drawing.Icon]::ExtractAssociatedIcon($codexExe)
        if ($icon) {
            try {
                $stream = [IO.File]::Create($targetIcon)
                try { $icon.Save($stream) } finally { $stream.Dispose() }
            } finally {
                $icon.Dispose()
            }
        }
    }

    New-Item -ItemType Directory -Path $programsDir -Force | Out-Null
    $powershell = (Get-Process -Id $PID).Path
    $shortcut = $shell.CreateShortcut($launcherShortcut)
    $shortcut.TargetPath = $powershell
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$targetLauncher`" -Port $Port"
    $shortcut.WorkingDirectory = $InstallDir
    if (Test-Path -LiteralPath $targetIcon) {
        $shortcut.IconLocation = "$targetIcon,0"
    }
    $shortcut.Description = 'Start Codex with the local Context HUD bridge'
    $shortcut.Save()

    if ((Test-Path -LiteralPath $taskbarShortcut) -and
        (Test-Path -LiteralPath $targetIcon)) {
        try {
            $pinnedShortcut = $shell.CreateShortcut($taskbarShortcut)
            if ([IO.Path]::GetFullPath($pinnedShortcut.TargetPath) -eq
                [IO.Path]::GetFullPath($powershell) -and
                $pinnedShortcut.Arguments.IndexOf(
                    $targetLauncher,
                    [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $pinnedShortcut.IconLocation = "$targetIcon,0"
                $pinnedShortcut.Save()
            }
        } catch { }
    }
}

# 仅恢复安装前已经运行、且 Codex 调试端口仍然存在的 HUD。
if ($restartHud) {
    $debugPattern = "(?:^|\s)--remote-debugging-port(?:=|\s+)$Port(?:\s|$)"
    $codexReady = Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -match $debugPattern } |
        Select-Object -First 1
    if ($codexReady) {
        Start-Process -FilePath $targetExe -ArgumentList @('--renderer-attach', $Port) `
            -WorkingDirectory $InstallDir -WindowStyle Hidden
    }
}

Write-Host "安装完成：$targetExe"
if ($NoStartMenu) {
    Write-Host "请运行 $targetLauncher 启动 Codex 与 HUD。"
} else {
    Write-Host '请从开始菜单打开 Codex with Context HUD。'
}
Write-Host '安装器没有关闭或重启 Codex。'
Write-Host 'HUD 不再开机自启，只会在 Codex 的本地端口就绪后加载。'

[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CodexContextHUD'),
    [ValidateRange(1024, 65535)]
    [int]$Port = 9231,
    [switch]$NoStartup,
    [switch]$NoStartMenu,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$targetExe = Join-Path $InstallDir 'CodexContextHUD.exe'
$targetLauncher = Join-Path $InstallDir 'Launch-CodexWithHUD.ps1'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Context HUD.lnk'
$programsDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Context HUD'
$launcherShortcut = Join-Path $programsDir 'Codex with Context HUD.lnk'
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

# 更新时只退出安装目录里的 HUD；绝不查询、关闭或重启 Codex。
Get-CimInstance Win32_Process -Filter "Name='CodexContextHUD.exe'" -ErrorAction SilentlyContinue |
    ForEach-Object {
        try {
            if ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($targetExe)) {
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
if (-not $NoStartup) {
    $shortcut = $shell.CreateShortcut($startupShortcut)
    $shortcut.TargetPath = $targetExe
    $shortcut.Arguments = "--renderer-attach $Port"
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.WindowStyle = 7
    $shortcut.Description = 'Codex Context HUD background host'
    $shortcut.Save()
}

if (-not $NoStartMenu) {
    New-Item -ItemType Directory -Path $programsDir -Force | Out-Null
    $powershell = Join-Path $PSHOME 'powershell.exe'
    $shortcut = $shell.CreateShortcut($launcherShortcut)
    $shortcut.TargetPath = $powershell
    $shortcut.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$targetLauncher`" -Port $Port"
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Start Codex with the local Context HUD bridge'
    $shortcut.Save()
}

if (-not $NoLaunch) {
    Start-Process -FilePath $targetExe -ArgumentList @('--renderer-attach', $Port) `
        -WorkingDirectory $InstallDir -WindowStyle Hidden
}

Write-Host "安装完成：$targetExe"
if ($NoStartMenu) {
    Write-Host "请运行 $targetLauncher 启动 Codex 与 HUD。"
} else {
    Write-Host '请从开始菜单打开 Codex with Context HUD。'
}
Write-Host '安装器没有关闭或重启 Codex。'

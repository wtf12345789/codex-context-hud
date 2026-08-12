[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CodexContextHUD')
)

$ErrorActionPreference = 'Stop'
$targetExe = Join-Path $InstallDir 'CodexContextHUD.exe'
$startupShortcut = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Context HUD.lnk'
$programsDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'Codex Context HUD'
$launcherShortcut = Join-Path $programsDir 'Codex with Context HUD.lnk'
$taskbarShortcut = Join-Path $env:APPDATA `
    'Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Codex with Context HUD.lnk'

# 只退出本工具，Codex 进程始终不受影响。
Get-CimInstance Win32_Process -Filter "Name='CodexContextHUD.exe'" -ErrorAction SilentlyContinue |
    ForEach-Object {
        try {
            if ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($targetExe)) {
                Stop-Process -Id $_.ProcessId -Force
                Wait-Process -Id $_.ProcessId -Timeout 3 -ErrorAction SilentlyContinue
            }
        } catch { }
    }

foreach ($shortcutPath in @($startupShortcut, $launcherShortcut, $taskbarShortcut)) {
    if (-not (Test-Path -LiteralPath $shortcutPath)) { continue }
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $target = [IO.Path]::GetFullPath($shortcut.TargetPath)
        if ($target -eq [IO.Path]::GetFullPath($targetExe) -or
            $shortcut.Arguments.IndexOf($InstallDir, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Remove-Item -LiteralPath $shortcutPath -Force
        }
    } catch { }
}

foreach ($name in @(
    'CodexContextHUD.exe', 'Launch-CodexWithHUD.ps1', 'Uninstall.ps1',
    'Codex.ico', 'README.md', 'README.en.md', 'CHANGELOG.md', 'LICENSE'
)) {
    Remove-Item -LiteralPath (Join-Path $InstallDir $name) -Force -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $InstallDir) {
    $remaining = @(Get-ChildItem -LiteralPath $InstallDir -Force)
    if ($remaining.Count -eq 0) { Remove-Item -LiteralPath $InstallDir -Force }
}
if (Test-Path -LiteralPath $programsDir) {
    $remaining = @(Get-ChildItem -LiteralPath $programsDir -Force)
    if ($remaining.Count -eq 0) { Remove-Item -LiteralPath $programsDir -Force }
}

Write-Host 'Codex Context HUD 已卸载；Codex 未被关闭或重启。'

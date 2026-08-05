[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CodexContextHUD')
)

$ErrorActionPreference = 'Stop'
$targetExe = Join-Path $InstallDir 'CodexContextHUD.exe'
$targetReadme = Join-Path $InstallDir 'README.md'
$shortcutPath = Join-Path ([Environment]::GetFolderPath('Startup')) 'Codex Context HUD.lnk'

# 只退出本工具，Codex 进程始终不受影响。
Get-Process -Name 'CodexContextHUD' -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ([IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($targetExe)) {
            Stop-Process -Id $_.Id -Force
            $_.WaitForExit(3000)
        }
    } catch { }
}

if (Test-Path -LiteralPath $shortcutPath) {
    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcutTarget = $shell.CreateShortcut($shortcutPath).TargetPath
        if ([IO.Path]::GetFullPath($shortcutTarget) -eq
            [IO.Path]::GetFullPath($targetExe)) {
            Remove-Item -LiteralPath $shortcutPath -Force
        }
    } catch { }
}
Remove-Item -LiteralPath $targetExe -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $targetReadme -Force -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath $InstallDir) {
    $remaining = @(Get-ChildItem -LiteralPath $InstallDir -Force)
    if ($remaining.Count -eq 0) { Remove-Item -LiteralPath $InstallDir -Force }
}

Write-Host 'Codex Context HUD 已卸载；Codex 未被关闭或重启。'

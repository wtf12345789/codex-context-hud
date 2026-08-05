[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'CodexContextHUD'),
    [switch]$NoStartup,
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$sourceExe = Join-Path $PSScriptRoot 'CodexContextHUD.exe'
$sourceReadme = Join-Path $PSScriptRoot 'README.md'
$targetExe = Join-Path $InstallDir 'CodexContextHUD.exe'
$startupDir = [Environment]::GetFolderPath('Startup')
$shortcutPath = Join-Path $startupDir 'Codex Context HUD.lnk'

if (-not (Test-Path -LiteralPath $sourceExe)) { throw '安装包中缺少 CodexContextHUD.exe。' }
if (-not (Test-Path -LiteralPath $sourceReadme)) { throw '安装包中缺少 README.md。' }

# 更新时只退出安装目录里的 HUD；绝不查询、关闭或重启 Codex。
Get-Process -Name 'CodexContextHUD' -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        if ([IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($targetExe)) {
            Stop-Process -Id $_.Id -Force
            $_.WaitForExit(3000)
        }
    } catch { }
}

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $targetExe -Force
Copy-Item -LiteralPath $sourceReadme -Destination (Join-Path $InstallDir 'README.md') -Force

if (-not $NoStartup) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $targetExe
    $shortcut.WorkingDirectory = $InstallDir
    $shortcut.Description = 'Codex Context HUD'
    $shortcut.Save()
}

if (-not $NoLaunch) { Start-Process -FilePath $targetExe -WorkingDirectory $InstallDir }

Write-Host "安装完成：$targetExe"
Write-Host '安装过程没有关闭或重启 Codex。'

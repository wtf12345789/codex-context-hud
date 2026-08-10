[CmdletBinding()]
param(
    [string]$InstallDir = $PSScriptRoot,
    [ValidateRange(1024, 65535)]
    [int]$Port = 9231
)

$ErrorActionPreference = 'Stop'
$hudExe = Join-Path $InstallDir 'CodexContextHUD.exe'
if (-not (Test-Path -LiteralPath $hudExe)) {
    throw '未找到 CodexContextHUD.exe，请重新安装。'
}

$running = @(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'" -ErrorAction SilentlyContinue)
$debugPattern = "(?:^|\s)--remote-debugging-port(?:=|\s+)$Port(?:\s|$)"
if ($running | Where-Object { $_.CommandLine -match $debugPattern }) {
    Start-Process -FilePath $hudExe -ArgumentList @('--renderer-attach', $Port) `
        -WorkingDirectory $InstallDir -WindowStyle Hidden
    exit 0
}

if ($running.Count -gt 0) {
    Add-Type -AssemblyName System.Windows.Forms
    $message = 'Codex 已在运行，但没有启用本地 HUD 端口。' +
        [Environment]::NewLine + [Environment]::NewLine +
        '请自行保存工作并退出 Codex，然后再次打开 "Codex with Context HUD"。' +
        '本工具不会替你关闭或重启 Codex。'
    [void][Windows.Forms.MessageBox]::Show(
        $message,
        'Codex Context HUD',
        [Windows.Forms.MessageBoxButtons]::OK,
        [Windows.Forms.MessageBoxIcon]::Information)
    exit 3
}

$package = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue |
    Sort-Object Version -Descending | Select-Object -First 1
$codexExe = if ($package) { Join-Path $package.InstallLocation 'app\ChatGPT.exe' } else { $null }
if (-not $codexExe -or -not (Test-Path -LiteralPath $codexExe)) {
    throw '未找到 Microsoft Store 安装的 Codex Desktop。'
}

Start-Process -FilePath $hudExe -ArgumentList @('--renderer-attach', $Port) `
    -WorkingDirectory $InstallDir -WindowStyle Hidden
Start-Process -FilePath $codexExe -ArgumentList @(
    "--remote-debugging-address=127.0.0.1",
    "--remote-debugging-port=$Port"
) -WorkingDirectory (Split-Path -Parent $codexExe)

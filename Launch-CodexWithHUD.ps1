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

function Show-HudMessage {
    param(
        [Parameter(Mandatory)]
        [string]$Message,
        [ValidateSet('Information', 'Warning', 'Error')]
        [string]$Icon = 'Information'
    )

    Add-Type -AssemblyName System.Windows.Forms
    $iconValue = [Enum]::Parse([Windows.Forms.MessageBoxIcon], $Icon)
    [void][Windows.Forms.MessageBox]::Show(
        $Message,
        'Codex Context HUD',
        [Windows.Forms.MessageBoxButtons]::OK,
        $iconValue)
}

function Get-CodexAppUserModelId {
    $package = Get-AppxPackage -Name 'OpenAI.Codex' -ErrorAction SilentlyContinue |
        Sort-Object Version -Descending | Select-Object -First 1
    if (-not $package) {
        throw '未找到 Microsoft Store 安装的 Codex Desktop。'
    }

    $manifestPath = Join-Path $package.InstallLocation 'AppxManifest.xml'
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw 'Codex Desktop 的 AppxManifest.xml 不存在，应用包可能正在更新。'
    }

    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
    $application = $manifest.SelectSingleNode("/*[local-name()='Package']/*[local-name()='Applications']/*[local-name()='Application'][1]")
    if (-not $application -or -not $application.Id) {
        throw '无法从 Codex Desktop 应用包读取启动标识。'
    }

    return "$($package.PackageFamilyName)!$($application.Id)"
}

function Start-PackagedCodex {
    param(
        [Parameter(Mandatory)]
        [string]$AppUserModelId,
        [Parameter(Mandatory)]
        [string]$Arguments
    )

    if (-not ('CodexContextHUD.PackageActivator' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace CodexContextHUD
{
    [Flags]
    internal enum ActivateOptions : uint
    {
        None = 0
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            ActivateOptions options,
            out uint processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            ActivateOptions options,
            out uint processId);
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    internal class ApplicationActivationManager
    {
    }

    public static class PackageActivator
    {
        public static uint Activate(string appUserModelId, string arguments)
        {
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            uint processId;
            int result = manager.ActivateApplication(
                appUserModelId,
                arguments,
                ActivateOptions.None,
                out processId);
            if (result < 0)
            {
                Marshal.ThrowExceptionForHR(result);
            }

            return processId;
        }
    }
}
'@
    }

    return [CodexContextHUD.PackageActivator]::Activate($AppUserModelId, $Arguments)
}

function Get-DebugListener {
    param([int]$LocalPort)

    return Get-NetTCPConnection -State Listen -LocalAddress '127.0.0.1' `
        -LocalPort $LocalPort -ErrorAction SilentlyContinue |
        Select-Object -First 1
}

$running = @(Get-CimInstance Win32_Process -Filter "Name='ChatGPT.exe'" -ErrorAction SilentlyContinue)
$debugPattern = "(?:^|\s)--remote-debugging-port(?:=|\s+)$Port(?:\s|$)"
if ($running | Where-Object { $_.CommandLine -match $debugPattern }) {
    Start-Process -FilePath $hudExe -ArgumentList @('--renderer-attach', $Port) `
        -WorkingDirectory $InstallDir -WindowStyle Hidden
    exit 0
}

if ($running.Count -gt 0) {
    $message = 'Codex 已在运行，但没有启用本地 HUD 端口。' +
        [Environment]::NewLine + [Environment]::NewLine +
        '请自行保存工作并退出 Codex，然后再次打开 "Codex with Context HUD"。' +
        '本工具不会替你关闭或重启 Codex。'
    Show-HudMessage -Message $message
    exit 3
}

if (Get-DebugListener -LocalPort $Port) {
    throw "本地端口 $Port 已被其他程序占用，未启动 Codex 或 HUD。"
}

$appUserModelId = Get-CodexAppUserModelId
$activationArguments = @(
    "--remote-debugging-address=127.0.0.1",
    "--remote-debugging-port=$Port"
) -join ' '
$codexProcessId = Start-PackagedCodex -AppUserModelId $appUserModelId `
    -Arguments $activationArguments

$listener = $null
for ($attempt = 0; $attempt -lt 60 -and -not $listener; $attempt++) {
    if (-not (Get-Process -Id $codexProcessId -ErrorAction SilentlyContinue)) {
        break
    }

    $listener = Get-DebugListener -LocalPort $Port
    if (-not $listener) {
        Start-Sleep -Milliseconds 250
    }
}

if (-not $listener) {
    Show-HudMessage -Message (
        "Codex 已通过 Windows 应用入口启动，但本地 HUD 端口 $Port 未就绪。" +
        [Environment]::NewLine + [Environment]::NewLine +
        'HUD 没有启动，也不会自动重启 Codex。') `
        -Icon Warning
    exit 4
}

Start-Process -FilePath $hudExe -ArgumentList @('--renderer-attach', $Port) `
    -WorkingDirectory $InstallDir -WindowStyle Hidden

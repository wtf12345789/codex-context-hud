[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$source = Join-Path $projectDir 'CodexContextHUD.cs'
$output = Join-Path $projectDir 'CodexContextHUD.exe'
$buildOutput = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-build-{0}.exe" -f [Guid]::NewGuid())
$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw '未找到 Windows 自带的 .NET Framework C# 编译器。'
}

$arguments = @(
    '/nologo', '/target:winexe', '/optimize+', "/out:$buildOutput",
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    "/reference:$gac\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll",
    "/reference:$gac\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll",
    "/reference:$gac\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
    $source
)
$selfTest = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-selftest-{0}.txt" -f [Guid]::NewGuid())
try {
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "编译失败，退出码 $LASTEXITCODE" }
    $testProcess = Start-Process -FilePath $buildOutput -ArgumentList @('--self-test', $selfTest) -Wait -PassThru
    if ($testProcess.ExitCode -ne 0) { throw 'HUD 自测失败。' }
    Write-Host (Get-Content -LiteralPath $selfTest -Raw)

    $restartHud = $false
    Get-Process -Name 'CodexContextHUD' -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ([IO.Path]::GetFullPath($_.Path) -eq [IO.Path]::GetFullPath($output)) {
                $restartHud = $true
                Stop-Process -Id $_.Id -Force
                $_.WaitForExit(3000)
            }
        } catch { }
    }
    Copy-Item -LiteralPath $buildOutput -Destination $output -Force
    if ($restartHud) { Start-Process -FilePath $output -WorkingDirectory $projectDir }
}
finally {
    Remove-Item -LiteralPath $selfTest -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $buildOutput -Force -ErrorAction SilentlyContinue
}

Write-Host "构建完成：$output"

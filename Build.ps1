[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$sources = @(
    (Join-Path $projectDir 'CodexContextHUD.cs'),
    (Join-Path $projectDir 'RendererHudBridge.cs')
)
$rendererScript = Join-Path $projectDir 'RendererHudScript.js'
$output = Join-Path $projectDir 'CodexContextHUD.exe'
$buildOutput = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-build-{0}.exe" -f [Guid]::NewGuid())
$gac = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'Windows .NET Framework C# compiler was not found.'
}

$arguments = @(
    '/nologo', '/target:winexe', '/optimize+', "/out:$buildOutput",
    '/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll',
    '/reference:System.Web.Extensions.dll',
    "/resource:$rendererScript,CodexContextHUD.RendererHudScript.js",
    "/reference:$gac\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll",
    "/reference:$gac\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll",
    "/reference:$gac\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
    $sources
)
$selfTest = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-selftest-{0}.txt" -f [Guid]::NewGuid())
$rendererSelfTest = Join-Path ([IO.Path]::GetTempPath()) ("CodexContextHUD-renderer-selftest-{0}.txt" -f [Guid]::NewGuid())
try {
    & $compiler @arguments
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
    $testProcess = Start-Process -FilePath $buildOutput -ArgumentList @('--self-test', $selfTest) -Wait -PassThru
    if ($testProcess.ExitCode -ne 0) { throw 'HUD self-test failed.' }
    Write-Host (Get-Content -LiteralPath $selfTest -Raw)
    $rendererTestProcess = Start-Process -FilePath $buildOutput -ArgumentList @('--renderer-self-test', $rendererSelfTest) -Wait -PassThru
    if ($rendererTestProcess.ExitCode -ne 0) { throw 'Renderer HUD self-test failed.' }
    Write-Host (Get-Content -LiteralPath $rendererSelfTest -Raw)

    $restartHud = $false
    $restartHudArguments = @()
    Get-CimInstance Win32_Process -Filter "Name='CodexContextHUD.exe'" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if ([IO.Path]::GetFullPath($_.ExecutablePath) -eq [IO.Path]::GetFullPath($output)) {
                $restartHud = $true
                if ($_.CommandLine -match '--renderer-attach\s+(\d+)') {
                    $restartHudArguments = @('--renderer-attach', $Matches[1])
                } elseif ($_.CommandLine -match '(?:^|\s)--legacy-overlay(?:\s|$)') {
                    $restartHudArguments = @('--legacy-overlay')
                }
                Stop-Process -Id $_.ProcessId -Force
                Wait-Process -Id $_.ProcessId -Timeout 3 -ErrorAction SilentlyContinue
            }
        } catch { }
    }
    $copied = $false
    for ($copyAttempt = 0; $copyAttempt -lt 15 -and -not $copied; $copyAttempt++) {
        try {
            Copy-Item -LiteralPath $buildOutput -Destination $output -Force
            $copied = $true
        } catch {
            if ($copyAttempt -eq 14) { throw }
            Start-Sleep -Milliseconds 200
        }
    }
    if ($restartHud) {
        if ($restartHudArguments.Count -gt 0) {
            Start-Process -FilePath $output -ArgumentList $restartHudArguments `
                -WorkingDirectory $projectDir -WindowStyle Hidden
        } else {
            Start-Process -FilePath $output -WorkingDirectory $projectDir
        }
    }
}
finally {
    Remove-Item -LiteralPath $selfTest -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $rendererSelfTest -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $buildOutput -Force -ErrorAction SilentlyContinue
}

Write-Host "Build completed: $output"

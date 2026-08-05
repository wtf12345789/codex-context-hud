[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$outputZip = Join-Path $projectDir 'CodexContextHUD-portable.zip'
$packageRoot = Join-Path ([IO.Path]::GetTempPath()) `
    ("CodexContextHUD-package-{0}" -f [Guid]::NewGuid())
$packageDir = Join-Path $packageRoot 'CodexContextHUD-portable'
$checksumPath = Join-Path $projectDir 'SHA256SUMS.txt'
$payload = @(
    'CodexContextHUD.exe',
    'Install.ps1',
    'Uninstall.ps1',
    'README.md',
    'README.en.md',
    'LICENSE'
)

if (-not $SkipBuild) { & (Join-Path $projectDir 'Build.ps1') }

New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
try {
    foreach ($name in $payload) {
        $source = Join-Path $projectDir $name
        if (-not (Test-Path -LiteralPath $source)) { throw "缺少发布文件：$name" }
        Copy-Item -LiteralPath $source -Destination (Join-Path $packageDir $name)
    }

    $checksumLines = foreach ($name in $payload) {
        $hash = (Get-FileHash -LiteralPath (Join-Path $packageDir $name) `
            -Algorithm SHA256).Hash
        "$hash  $name"
    }
    [IO.File]::WriteAllLines($checksumPath, $checksumLines,
        (New-Object Text.UTF8Encoding($false)))
    Copy-Item -LiteralPath $checksumPath -Destination `
        (Join-Path $packageDir 'SHA256SUMS.txt')

    if (Test-Path -LiteralPath $outputZip) {
        $resolvedZip = (Resolve-Path -LiteralPath $outputZip).Path
        if ([IO.Path]::GetDirectoryName($resolvedZip) -ne $projectDir) {
            throw "ZIP 目标越界：$resolvedZip"
        }
        Remove-Item -LiteralPath $resolvedZip -Force
    }
    Compress-Archive -LiteralPath $packageDir -DestinationPath $outputZip `
        -CompressionLevel Optimal
}
finally {
    $resolvedTemp = [IO.Path]::GetFullPath($packageRoot)
    $systemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolvedTemp.StartsWith($systemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolvedTemp).StartsWith('CodexContextHUD-package-')) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "Package: $outputZip"
Write-Host "SHA256: $((Get-FileHash -LiteralPath $outputZip -Algorithm SHA256).Hash)"

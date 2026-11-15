param(
    [switch]$Publish,
    [string]$DotNetPath = 'dotnet',
    [string]$OutputDirectory,
    [string]$ArtifactsPath
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRoot 'artifacts\SoundCity' }
$buildOptions = @()
if ($ArtifactsPath) { $buildOptions += @('--artifacts-path', [IO.Path]::GetFullPath($ArtifactsPath)) }
& (Join-Path $PSScriptRoot 'Get-Backend.ps1')
if ($Publish) {
    & $DotNetPath publish (Join-Path $taskRoot 'src\SoundCity.App') -c Release -r win-x64 --self-contained true -o $OutputDirectory @buildOptions
} else {
    & $DotNetPath build (Join-Path $taskRoot 'src\SoundCity.App') -c Release @buildOptions
}
if ($LASTEXITCODE -ne 0) { throw 'SoundCity build failed.' }
if ($Publish) {
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE.txt') -Destination (Join-Path $OutputDirectory 'LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md')
    $taskDocs = Join-Path $OutputDirectory 'docs'
    New-Item -ItemType Directory -Path $taskDocs -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\DEVICE-SUPPORT.md') -Destination (Join-Path $taskDocs 'DEVICE-SUPPORT.md')
    $sourcePath = Join-Path $OutputDirectory 'tools\OpenSCQ30-v2.13.0-source.zip'
    if (!(Test-Path -LiteralPath $sourcePath)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/Oppzippy/OpenSCQ30/archive/refs/tags/v2.13.0.zip' -OutFile $sourcePath
    }
    if ((Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash -ne '24f3d449646eee3de0b2a863826fcf855f8f59366433450ea9350229c058be35') {
        throw 'OpenSCQ30 source archive did not match its pinned SHA-256.'
    }
    # Include the runtime redistribution license and notices from the SDK used to publish.
    $dotnetExecutable = (Get-Command $DotNetPath -ErrorAction Stop).Source
    $dotnetDirectory = Split-Path -Parent $dotnetExecutable
    $licenses = Join-Path $OutputDirectory 'licenses'
    New-Item -ItemType Directory -Force -Path $licenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $dotnetDirectory 'LICENSE.txt') -Destination (Join-Path $licenses 'DOTNET-LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $dotnetDirectory 'ThirdPartyNotices.txt') -Destination (Join-Path $licenses 'DOTNET-THIRD-PARTY-NOTICES.txt')
}

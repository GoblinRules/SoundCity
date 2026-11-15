param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'Get-Backend.ps1')
if ($Publish) {
    dotnet publish (Join-Path $taskRoot 'src\SoundCity.App') -c Release -r win-x64 --self-contained true -o (Join-Path $taskRoot 'artifacts\SoundCity')
} else {
    dotnet build (Join-Path $taskRoot 'src\SoundCity.App') -c Release
}
if ($LASTEXITCODE -ne 0) { throw 'SoundCity build failed.' }
if ($Publish) {
    Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE.txt') -Destination (Join-Path $taskRoot 'artifacts\SoundCity\LICENSE.txt')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'README.md') -Destination (Join-Path $taskRoot 'artifacts\SoundCity\README.md')
    $taskDocs = Join-Path $taskRoot 'artifacts\SoundCity\docs'
    New-Item -ItemType Directory -Path $taskDocs -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'docs\DEVICE-SUPPORT.md') -Destination (Join-Path $taskDocs 'DEVICE-SUPPORT.md')
    $sourcePath = Join-Path $taskRoot 'artifacts\SoundCity\tools\OpenSCQ30-v2.13.0-source.zip'
    if (!(Test-Path -LiteralPath $sourcePath)) {
        Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/Oppzippy/OpenSCQ30/archive/refs/tags/v2.13.0.zip' -OutFile $sourcePath
    }
}

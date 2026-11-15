$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$backendDirectory = Join-Path $taskRoot 'tools\openscq30'
$backendPath = Join-Path $backendDirectory 'openscq30.exe'
$expectedHash = 'c4b24b7e59f56bbcff0a240830a613cd51bee2822808d5b6075c80087fe04bf0'
New-Item -ItemType Directory -Force -Path $backendDirectory | Out-Null
if (!(Test-Path -LiteralPath $backendPath) -or (Get-FileHash -LiteralPath $backendPath -Algorithm SHA256).Hash -ne $expectedHash) {
    $downloadPath = Join-Path $backendDirectory 'openscq30.download'
    Invoke-WebRequest -UseBasicParsing -Uri 'https://github.com/Oppzippy/OpenSCQ30/releases/download/v2.13.0/openscq30-cli-windows-x86_64.exe' -OutFile $downloadPath
    if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'OpenSCQ30 download did not match the pinned SHA-256. It has not been installed.'
    }
    Move-Item -LiteralPath $downloadPath -Destination $backendPath -Force
}
Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/Oppzippy/OpenSCQ30/v2.13.0/LICENSE.txt' -OutFile (Join-Path $backendDirectory 'LICENSE.txt')
Write-Output 'OpenSCQ30 v2.13.0 verified and ready.'

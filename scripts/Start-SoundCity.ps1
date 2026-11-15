$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$appPath = Join-Path $taskRoot 'artifacts\SoundCity\SoundCity.exe'
if (!(Test-Path -LiteralPath $appPath)) {
    $appPath = Join-Path $taskRoot 'src\SoundCity.App\bin\Release\net9.0-windows10.0.19041.0\SoundCity.exe'
}
if (!(Test-Path -LiteralPath $appPath)) {
    & (Join-Path $PSScriptRoot 'Build.ps1')
}
# This is the interactive application, not a background helper.
Start-Process -FilePath $appPath

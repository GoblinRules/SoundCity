param(
    [string]$DotNetPath = 'dotnet',
    [string]$IsccPath,
    [string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $taskRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a three-part release version in Directory.Build.props.' }
if (!$OutputDirectory) { $OutputDirectory = Join-Path $taskRoot "artifacts\releases\$version" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (!$IsccPath) {
    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
    )
    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$IsccPath -or !(Test-Path -LiteralPath $IsccPath)) { throw 'Install Inno Setup 6.7 or newer, or supply -IsccPath.' }
# Every package uses fresh staging so local state and old build files cannot enter a release.
$staging = Join-Path $OutputDirectory ('.staging-' + [Guid]::NewGuid().ToString('N'))
$publish = Join-Path $staging 'publish'
New-Item -ItemType Directory -Force -Path $publish | Out-Null
& (Join-Path $PSScriptRoot 'Build.ps1') -Publish -DotNetPath $DotNetPath -OutputDirectory $publish -ArtifactsPath (Join-Path $staging 'build')
if (Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Name -match '\.(pdb|log|sqlite|db)(-|$)|^\.env($|\.)' }) {
    throw 'Unexpected debug, diagnostic or local-state file in release staging.'
}
$artwork = Join-Path $staging 'wizard.png'
& (Join-Path $PSScriptRoot 'New-InstallerArtwork.ps1') -OutputPath $artwork
& $IsccPath /Qp "/DAppVersion=$version" "/DProjectRoot=$taskRoot" "/DPublishDir=$publish" "/DReleaseDir=$OutputDirectory" "/DArtworkFile=$artwork" (Join-Path $taskRoot 'installer\SoundCity.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$setup = Join-Path $OutputDirectory "SoundCity-$version-Setup-x64.exe"
$portable = Join-Path $OutputDirectory "SoundCity-$version-Portable-x64.zip"
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $portable) { throw "Portable archive already exists: $portable. Use a fresh output directory." }
[IO.Compression.ZipFile]::CreateFromDirectory($publish, $portable, [IO.Compression.CompressionLevel]::Optimal, $false)
$checksums = @($setup, $portable) | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($_) }
[IO.File]::WriteAllLines((Join-Path $OutputDirectory 'SHA256SUMS.txt'), $checksums, (New-Object Text.UTF8Encoding($false)))
Write-Output "Release assets: $OutputDirectory"
Write-Output "Published payload for verification: $publish"

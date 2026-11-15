# Windows release packaging

Requirements: Windows, .NET 10 SDK and Inno Setup 6.7 or newer. The application
version is set once in `Directory.Build.props`. The release is a self-contained
x64 build; end users do not need a .NET SDK or runtime installation.

Run from the repository root:

```powershell
dotnet run --project tests/SoundCity.Tests -c Release -- --engine
dotnet run --project tests/SoundCity.UiTests -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Release.ps1
```

The engine tests need `scripts/Get-Backend.ps1` to have completed at least once.
These checks use simulated devices and temporary configurations; no headphone
settings or active Windows audio configuration are changed.

`Build-Release.ps1` accepts `-DotNetPath`, `-IsccPath` and `-OutputDirectory` for
custom tool locations or a build drive with more free space. Use a fresh output
directory for each build. Allow at least 3 GB for build intermediates and packages.
NuGet's `NUGET_PACKAGES` and Windows `TEMP`/`TMP` environment variables can also
point to that drive. Build scripts do not install or update build tools.

Each build uses a new staging directory, verifies the pinned OpenSCQ30 engine
and corresponding-source archive, includes license notices, and rejects debug
symbols or local-state files in the payload. Release compilation omits PDBs and
maps source paths to `/_/` to avoid embedding the developer's profile path.

The output directory contains:

- `SoundCity-<version>-Setup-x64.exe`: dark, per-user installer with a Start menu
  shortcut, optional desktop shortcut and Windows uninstall registration.
- `SoundCity-<version>-Portable-x64.zip`: the same application files without setup.
- `SHA256SUMS.txt`: SHA-256 checksums for both packages.

Only these three files are release uploads. The `.staging-*` subdirectory is
local build material and must not be uploaded. Generated packages and build
tools are excluded from Git. Do not include profiles, diagnostics, credentials,
local databases or real device identifiers in release material.

## Validate and publish

Test a fresh installation, a second installation over it, app launch in demo
mode, and uninstall. Confirm the uninstaller preserves `%LOCALAPPDATA%\SoundCity`
and Equalizer APO configuration. Compare installed files with the staged payload.
Verify no private paths or personal examples are embedded in the packages.

Commit and push the tested source, then create an annotated `v<version>` tag on
that commit. Create a draft GitHub release with the three assets and the matching
notes from `docs/releases/`. Verify the uploaded assets and tag before publishing.
GitHub's automatic source archives identify the exact tagged SoundCity source;
the packages also include the corresponding OpenSCQ30 source archive.

The initial release is unsigned. A future signed release requires a legitimate
code-signing certificate; certificates, private keys and signing credentials must
stay outside the repository and release uploads. If signing is introduced, sign
the app before packaging and the installer afterward, then regenerate checksums.

The installer never installs Equalizer APO, alters audio/driver/security settings,
raises volume, enables startup tasks or requires elevation. APO setup remains an
explicit, separate step for Windows EQ and boost.

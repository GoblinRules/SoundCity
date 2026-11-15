# README images

The PNGs in this directory are rendered directly from SoundCity's WPF controls.
They are documentation renders, not photographs or evidence of live device support.
The main view uses OpenSCQ30's explicitly simulated Liberty 4 Pro data. The EQ
views use the same illustrative ten-band curve, with both positive and negative
gain. They show the real disabled Apply state before APO setup.

Regenerate on Windows after setting up the .NET 10 SDK and the pinned backend:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Get-Backend.ps1
dotnet run --project tools/ReadmeImages -c Release
```

The renderer lives in [tools/ReadmeImages](../../tools/ReadmeImages). It uses an
isolated temporary database, profiles and view preferences, never opens a window,
never starts Bluetooth discovery and never invokes Apply. Only the engine's demo
device is queried. It writes three PNGs here and removes its unique temporary data.
An optional output-directory argument can be passed after `--`.

Review every rendered image for layout, clipped text, demo labels and personal
data before publishing it. Keep image files free of EXIF or text metadata.
Do not replace the images with captures of personal Bluetooth names or profiles.

The header logo is [assets/soundcity-logo.svg](../../assets/soundcity-logo.svg).
Regenerate the existing branding with:

```powershell
powershell -NoProfile -STA -ExecutionPolicy Bypass -File scripts/Generate-Brand.ps1
```

The SVG wordmark uses outlined glyphs so it does not depend on the reader having
the same fonts installed.

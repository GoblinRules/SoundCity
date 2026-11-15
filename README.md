# SoundCity

A local Windows desktop controller for Soundcore, UGREEN, Parrot, Beats and Apple headphones, with an orange, graphite and midnight interface. Soundcore controls use a pinned OpenSCQ30 engine. Studio Max5s has a native encrypted UG3 driver, and Zik 3 has an experimental native Parrot driver. Beats Solo Buds and AirPods Pro 3 currently use Windows audio controls. Windows output volume and mute use Core Audio.

## Run

Download **SoundCity-0.1.0-Setup-x64.exe** from the [latest GitHub release](https://github.com/GoblinRules/SoundCity/releases/latest). It installs for your Windows account, adds a Start menu shortcut and offers an optional desktop shortcut. Uninstall through Windows Settings → Apps. Your saved profiles and Equalizer APO configuration remain in place when you uninstall.

The release also includes a portable ZIP: extract it to its own folder and open `SoundCity.exe`. Both downloads include the .NET 10 runtime and need no separate .NET installation or administrator access. Supports x64 Windows 10 version 2004 or newer, and Windows 11. The first release is not code-signed; Windows may display an unknown-publisher or SmartScreen warning. SHA-256 checksums are included with the downloads.

To build from source, install the .NET 10 SDK and run from PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build.ps1 -Publish
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Start-SoundCity.ps1
```

The published app is written to `artifacts/SoundCity/`. See [release packaging](docs/RELEASING.md) to build the installer and portable archive.

The build downloads OpenSCQ30 v2.13.0 from its official release and verifies a pinned SHA-256. A published build also includes its license and source archive. Internet access is needed for setup; normal headphone control is local.

1. Pair the headphones in Windows Bluetooth settings and turn them on.
2. Select them in SoundCity. Connected supported models load automatically; **Refresh controls** reads again. Reading controls does not change headphone settings.
3. Make a change and press **Apply**. The app rechecks available controls before writing, then reloads the device state.
4. Use **Keep in tray** to hide the window. Double-click its tray icon to restore it; closing the window exits.

Use **Identify headphones** for a personal or shortened Bluetooth name, such as “My AirPods Pro” or “Parrot Zik”. Select the paired device, choose its exact model and confirm that you checked it. The selection is stored locally for that Bluetooth address; **Forget selection** removes it. Generic AirPods Pro, Zik and Beats names are never silently assigned to another generation. Exact recognized names take precedence over saved selections.

`Ctrl+R` refreshes controls; `Ctrl+D` refreshes the paired-device list. Filter by category or search by control name. **Equaliser** opens the band editor; **Volume boost** opens optional Windows processing. **Connection details** inspects RFCOMM services. **Export diagnostics** saves a local JSON report with addresses, serial numbers, and paired-host names omitted. Windows output volume and mute are shown only for endpoints matching the selected headphones, never a fallback speaker. At startup, SoundCity selects the connected headphones matching the Windows default output; refreshing preserves your device selection.

## Equaliser and volume boost

The EQ editor provides graphite rotary dials and sliders with matching green → amber → red gain gradients, numeric dB entry, a live gain curve, Flat/Warm/Bass lift/Vocals/Bright starting curves, revert and named profiles saved per model on this PC. Colours progress away from zero in both directions: green near zero, amber halfway, red at the strongest cut or boost. Slider fill starts at zero and stays aligned to the scale; the handle colour matches the dial indicator. Colours indicate requested gain, not measured loudness. Use **Dials / Sliders** above the controls to change views without losing your draft. EQ and boost remember their view choices separately on this PC.

Drag a dial up/down; hold Shift for fine adjustment. Arrow keys step the gain, Page Up/Down make larger changes, and double-click resets to 0 dB. Hover over a dial or slider and use the mouse wheel to adjust it without clicking first. Each notch uses the control’s native gain step; smooth-wheel deltas accumulate, and the page stays still while you adjust a control. Boost offers a large rotary dial or a horizontal slider. Switching views and loading or saving a profile do not apply audio changes. **Apply to headphones** sends the draft to the device. Drafts survive category changes within the session.

**Windows equaliser** is available for every catalog model, including Solo Buds, AirPods Pro 3 and Zik 3. It has ten bands from 31 Hz to 16 kHz, ±12 dB in 0.1 dB steps, and separate local profiles. It starts disabled. Curves can be designed and saved before setup, but applying needs Equalizer APO and a matching connected playback endpoint. **Apply Windows EQ** saves the configuration for that endpoint; it does not change onboard headphone EQ. Positive bands reserve equivalent preamp headroom. Explicit volume boost can use that headroom and may clip. The app reports configuration, not proof that the APO is processing audio.

The supplied AutoEq link is for **Beats Studio Buds**, not **Solo Buds**. No Studio Buds correction has been included or applied. The editor’s starting curves are personal tuning suggestions, not headphone measurements.

Studio Max5s custom EQ uses eight fallback bands (62, 250, 500, 1000, 2000, 4000, 8000 and 16000 Hz) and whole-dB gains from -6 to +6, matching the official app. Saved curves use the frequencies read from the device. An empty saved curve appears as a clearly labelled new draft while the current factory preset remains active. Custom writes use UG3 command 21 and verify the stored gains and active mode by reading them back. Firmware 0.1.5's unsupported staging commands are not used. The packet format and live read capability have been checked; an actual custom-EQ write still needs a listening test. Soundcore uses its own reported frequencies and gain ranges through OpenSCQ30.

Volume boost is an optional **Equalizer APO** integration with 0 to +12 dB preamp gain in 0.5 dB steps. It is off by default and requires [Equalizer APO setup](https://sourceforge.net/p/equalizerapo/wiki/Documentation/): install the audio component, enable it for the headphone playback endpoint, and restart Windows if requested. SoundCity detects the configuration and provides a setup link; it does not install an audio component, restart audio services or raise volume automatically. EQ and boost prefer an output with the post-mix APO configured, then the Windows default, then stereo. Apply stays unavailable if the selected output lacks that APO or has audio enhancements disabled, and setup is checked again before saving. Enable boost and press Apply after setup.

Boost configuration is restricted to the exact selected Windows output GUID. SoundCity preserves existing APO filters, backs up the main configuration when first connecting, and uses a separate `SoundCity/boost.txt` include. Turning boost off removes its preamp gain. "Configured" means the configuration was saved; it does not prove the APO is running in the current playback path. Windows audio enhancements must be enabled, and exclusive-mode output can bypass APO processing. Positive gain adds to existing filters and can clip loud material; lower it if distortion occurs. There is no limiter or promise of extra clean output above the headphones' hardware limits. The integration follows the [APO configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/).

## Device support

| Device | Profile | Current state |
| --- | --- | --- |
| Soundcore Liberty 4 Pro | A3954 | Dynamic controls through OpenSCQ30; simulated reads and writes tested. Physical device tests still needed. |
| Soundcore Life P3 | A3939 | Dynamic controls through OpenSCQ30; simulated reads and writes tested. The user must confirm A3939 before loading this driver. |
| UGREEN FitBuds | WS213 / SKU 65655 | Windows output volume/mute when connected, discovery and service inspection. Vendor controls pending exact-model verification. |
| UGREEN Studio Max5s | HP208 / SKU 16172 | Encrypted UG3 status reads on firmware 0.1.5. ANC mode/strength, eight EQ presets and custom band EQ, spatial/game modes, wind reduction, multipoint, codec preference, prompts and verified button assignments. Write commands are implemented with acknowledgement and readback; physical changes have not yet been exercised. |
| Parrot Zik 3 | Zik 3 / 3.0 | Experimental native Bluetooth control: reported battery/firmware, noise control enable and five modes, Concert Hall enable/room/angle, saved onboard EQ on/off, head detection and auto-connect. Protocol simulation tests pass; physical Zik 3 validation is still required. |
| Beats Solo Buds | A3150 / A3151 / A3152 | Windows output volume/mute; optional Windows EQ and boost. Solo Buds have no ANC/transparency. Vendor battery and button preferences are not integrated. Physical device testing is pending. |
| Apple AirPods Pro 3 | A3063 / A3064 / A3065 | Windows output volume/mute; optional Windows EQ and boost. Apple-protocol battery, ANC, Adaptive Audio and ear detection are not integrated. Physical device testing is pending. |

The interface exposes settings returned by the device: battery information, noise modes, EQ, named EQ profiles, gestures, spatial audio, wearing detection, prompts, multipoint and other supported features. Availability changes with model, firmware and state. There are no fabricated controls or battery values. Demo mode is explicitly marked.

This version does not implement Windows audio-output switching, forced Bluetooth disconnect/reconnect, global hotkeys, firmware flashing, or full phone-app parity. The Windows Bluetooth settings link provides access to system connection management. Device-side multipoint controls appear when exposed by OpenSCQ30. Enabling a codec in the headphones does not add codec support to Windows.

Studio Max5s uses **UG3**, not the Max5c/Studio Pro UG1 protocol. Its exact model profile, packet framing, encryption handshake and command mappings were identified in the official UGREEN Audio 2.7.0 app, then its encrypted status reads were verified on the connected HP208. Initial hardware validation used read-only queries. Changes are serialized, validated against a fresh device read, acknowledged and checked by reading back the stored value.

Status reads accept identical repeated fields, including nested battery, button and prompt values. Conflicting repeated values trigger one fresh query of that status field; persistent conflicts stop with a refresh message instead of choosing an arbitrary value. Truncated packets and duplicate encryption-handshake fields remain rejected. Recovery only reads status and never replays a setting change.

Max5s personalized hearing profiles and some prompt/button options remain unfinished. Unknown fields stay absent or read only. An empty custom-EQ field enables creation of a new curve without presenting it as live data; an absent ANC-depth value stays absent. FitBuds requires its own exact-model protocol verification. Firmware flashing and factory reset are not exposed.

Zik 3 uses its model-specific RFCOMM service, bounded frame reads, XML response-path checking, fresh capability reads and readback after each write. Rejected optional endpoints hide their controls; missing or unknown values never become editable defaults. Custom onboard EQ bands, adaptive ANC, firmware operations and flight mode are not exposed. Bluetooth is required for its control channel; USB-only Zik use is not currently discovered. See [device implementation notes](docs/DEVICE-SUPPORT.md).

AirPods Pro 3’s advanced controls use Apple’s AAP L2CAP channel. Windows does not expose that channel to ordinary desktop apps. Projects such as NTPods add a kernel driver; this build does not install one or change Windows driver/security settings. Use the iPhone or headphone controls for these features for now.

## Development and checks

The native interface uses an explicit midnight window theme, dark menus and orange accents regardless of the Windows light/dark preference. Original SoundCity artwork is in `assets/`: editable SVGs, a transparent 1024px icon and a logo lockup. The executable, window and tray use the same emblem. Regenerate the vector resources, PNGs and ten-size Windows ICO with `powershell -NoProfile -STA -ExecutionPolicy Bypass -File scripts/Generate-Brand.ps1`.

```powershell
dotnet build src/SoundCity.App -c Release
dotnet run --project tests/SoundCity.Tests -c Release -- --engine
# In-memory rotary input/accessibility checks; no audio or headphone writes.
dotnet run --project tests/SoundCity.UiTests -c Release
# Optional: only reads settings if Liberty 4 Pro is currently connected.
dotnet run --project tests/SoundCity.Tests -c Release -- --engine --live-read
# Read Windows audio endpoints and the connected Studio Max5s, without changing settings.
dotnet run --project tests/SoundCity.Tests -c Release -- --engine --audio-read --ugreen-read
```

Tests cover strict model matching, tagged JSON, settings repeated across categories, EQ bounds, profile escaping, provisional-model restrictions, stale capability rejection, serialized commands, and the pinned engine's simulated Liberty 4 Pro and Life P3 reads/writes. UG3 checks use sanitized HP208 captures, reject malformed fields, and verify command mappings and CRC. Live read checks exercise the encrypted HP208 handshake and Windows audio enumeration. Simulated engine state resets between processes; demo changes are not evidence of hardware compatibility.

New-device checks cover renamed-device mappings, exact endpoint isolation, Parrot fragmented/coalesced frames, unsafe XML, rejected/missing capabilities, single-write readback, and Windows EQ/boost persistence and headroom. Output selection and APO registration tests cover stereo/hands-free preference, default headphones, missing post-mix processing and disabled enhancements. The engine test run passes 142 checks. Parrot fixtures use simulated responses; physical Zik 3 validation is still pending.

To explore the UI with simulated data, run `SoundCity.exe --demo`. It uses a separate database and cannot control real headphones. Run `SoundCity.exe --windows-eq` to open the Windows equaliser after device discovery. To produce a read-only hardware report, run `SoundCity.exe --diagnose <absolute-report-path.json>`. This enumerates paired devices and queries services only for connected targets; it sends no headphone settings commands.

Application configuration is in `%LOCALAPPDATA%\SoundCity`. The app uses `OPENSCQ30_DATABASE_PATH` to isolate its backend database from any standalone OpenSCQ30 installation. Diagnostics are only saved when requested, and are not uploaded.

## Next device validation

- Connect Liberty 4 Pro and load controls; verify reported firmware, battery, EQ and noise mode against the phone app. Test one reversible setting at a time.
- Confirm P3 model A3939 before using its driver.
- Validate Max5s setting changes one at a time against the iPhone app. The first checks were intentionally read only to avoid interrupting listening.
- Verify FitBuds with its exact profile and live responses before enabling vendor commands. Never substitute the Max5s driver.
- Pair Zik 3 by Bluetooth, identify its model if renamed, and compare reported battery, noise mode and Concert Hall state with the phone app. Then test one reversible setting and check readback.
- Pair Solo Buds and AirPods Pro 3 and verify exact Windows volume/mute endpoint matching. Enable optional Windows EQ only after configuring Equalizer APO for their output.
- Close the phone's control app if the desktop control connection is unavailable; some devices allow only one control client at a time.

## Attribution and license

SoundCity source is licensed under GPL-3.0-only; see `LICENSE.txt`. The separate OpenSCQ30 engine is documented in `THIRD-PARTY-NOTICES.txt`.

References:
- https://github.com/Oppzippy/OpenSCQ30/tree/v2.13.0
- https://github.com/DamienStaebler/SoundcoreDesktop
- https://github.com/gmallios/SoundcoreManager
- https://github.com/tzy3454u/ugreen-headphone-control
- https://github.com/tung1883/ugreen-desktop-gui
- https://github.com/dustincorder/ugreenapp
- https://github.com/nx-d1frnt/ugreenapp
- https://github.com/ilyamen/ugreenapp
- https://github.com/lucasrc97/ugreenapp-windows
- https://us.ugreen.com/pages/ugreen-app
- https://github.com/m0sia/pyParrotZik
- https://github.com/AeonLucid/Parroter
- https://github.com/kradhub/zik2ctl
- https://github.com/arctumn/ntpods/tree/main/windows
- https://support.apple.com/109525

No vendor artwork is bundled. This project is independent of Anker, Soundcore, UGREEN, Parrot, Beats and Apple.

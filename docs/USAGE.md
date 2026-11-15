# Using SoundCity

[Downloads and device support](../README.md) · [Protocol notes](DEVICE-SUPPORT.md)

## Connecting headphones

Pair the headphones in Windows Bluetooth settings and turn them on before opening
SoundCity. At startup, the app prefers the connected headphones matching Windows'
default output. Refreshing the device list preserves your selection when possible.
**Refresh controls** reads the selected device; it does not change its settings.

Use **Identify headphones** if the Bluetooth name is personal or shortened.
Choose the paired device, check its exact model in the phone app or on the case,
then save the selection. This maps that Bluetooth address locally. **Forget
selection** removes the mapping. An exact recognised product name takes precedence
over a saved mapping. Life P3 must be confirmed as **A3939** before its driver loads.

The support table distinguishes native vendor controls from Windows audio controls.
A device appearing in the sidebar does not mean all phone-app features are available.
If a vendor connection fails, close the phone's control app and try again; some
headphones allow only one control client at a time.

## Adjusting the sound

The category picker and search field filter the available controls. Edit a value,
then press its **Apply** button. SoundCity rechecks capabilities before sending a
change and reads back device state afterward where supported.

The equaliser offers dials, sliders, numeric dB entry and a gain-curve display.
**Dials / Sliders** changes the view without changing the draft. Both views support
the mouse wheel without first clicking the control. Colours run green → amber →
red away from zero in both directions and indicate requested gain, not measured
loudness. EQ and volume boost remember their view choices separately.

| Control | Action |
| --- | --- |
| Mouse wheel over a dial or slider | Adjust by its native gain step; partial wheel movement accumulates. |
| Drag a dial up/down | Adjust gain; hold **Shift** for finer movement. |
| Arrow keys | Adjust the focused control by one small step. |
| Page Up / Page Down on a dial | Make a larger adjustment. |
| Home / End on a dial | Select its minimum / maximum. |
| Double-click a dial or slider | Reset to 0 dB. |
| **Ctrl+R** | Refresh controls. |
| **Ctrl+D** | Refresh paired devices. |

Flat, Warm, Bass lift, Vocals and Bright load starting curves. They are personal
tuning suggestions, not measurements or AutoEq corrections. Named profiles are
saved on this PC for the selected model; Windows EQ profiles are separate from
onboard headphone profiles. Loading or saving a profile does not apply audio.
Drafts survive category changes during the session. **Revert edits** restores the
editor's original curve.

**Apply to headphones** writes onboard EQ. **Apply Windows EQ** saves processing
for the selected Windows output. They are separate signal-processing stages.
The graph shows the requested band gains, not a measured frequency response.

## Windows EQ and boost setup

Windows EQ has ten bands from 31 Hz to 16 kHz with ±12 dB range in 0.1 dB steps.
Volume boost adds 0 to +12 dB in 0.5 dB steps. Both start off and require
[Equalizer APO](https://sourceforge.net/p/equalizerapo/wiki/Documentation/).

1. Install Equalizer APO from its official project page.
2. In its Device Selector / Configurator, enable it for the headphone playback
   output you actually use. Restart Windows if the setup requests it.
3. Keep Windows audio enhancements enabled for that output.
4. Return to SoundCity and use **Check setup**. Select the intended output.
5. Enable Windows EQ or boost, choose your values, and press the appropriate Apply
   button. Start with low gain and listen for distortion.

APO installation can require administrator access. The SoundCity installer does
not perform it, install drivers, restart audio services or raise volume.

SoundCity prefers an output with post-mix APO processing configured, then the
default output, then stereo. Apply stays unavailable for an unconfigured output
or disabled audio enhancements. Windows volume/mute also targets only outputs
matching the selected headphones; the app never falls back to unrelated speakers.

Positive EQ bands reserve equivalent preamp headroom. Explicit boost can use that
headroom and adds to other filters, which may clip loud recordings. There is no
limiter or promise of extra clean output beyond the hardware's limits. Reduce gain
if you hear distortion. **Configured** means settings were saved; it does not
prove that APO is processing the current audio stream. Exclusive-mode playback
can bypass Windows processing.

The integration scopes settings to the selected output GUID, preserves existing
APO filters and backs up the main configuration when first connecting. Its own
include is `SoundCity/boost.txt`. Turning boost off removes its preamp gain.
See the [APO configuration reference](https://sourceforge.net/p/equalizerapo/wiki/Configuration%20reference/)
for advanced processing behaviour.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| Headphones are missing | Pair them in Windows, power them on, refresh devices and use Identify headphones for renamed devices. |
| Music plays but vendor controls are absent | Check the support table. Windows playback does not imply vendor protocol support. Confirm Life P3's model if applicable. |
| A vendor control connection fails | Close the phone's control app, check the headphones remain connected, then refresh. |
| Windows EQ or boost Apply is unavailable | Select the intended connected output, configure APO for it and enable audio enhancements. |
| EQ/boost is saved but sounds unchanged | Check APO's selected output, any requested restart, and whether playback uses exclusive mode. |
| Boost sounds distorted | Reduce gain. Existing headphone EQ and Windows filters can add to the boost. |
| A control disappears after another change | Firmware capabilities can change with codec, multipoint or audio modes. Refresh to read current support. |

Use **Connection details** to inspect the device's RFCOMM services. **Export
diagnostics** writes a local JSON report; the normal export omits Bluetooth
addresses, serial-number fields and paired-host fields. Review any report before
sharing it, particularly device names and error messages.

## Tray, storage and command-line options

**Keep in tray** hides the window. Double-click the tray icon to restore it.
Closing the window exits the app. SoundCity does not enable automatic startup.

Local profiles, model mappings and display preferences live in
`%LOCALAPPDATA%\SoundCity`. The OpenSCQ30 database is isolated from standalone
OpenSCQ30 installations. Uninstalling SoundCity preserves that user data and
Equalizer APO configuration.

| Option | Purpose |
| --- | --- |
| `SoundCity.exe --demo` | Explore explicitly simulated Soundcore controls using a separate demo database. |
| `SoundCity.exe --windows-eq` | Open Windows EQ after device discovery. |
| `SoundCity.exe --diagnose <absolute-report-path.json>` | Enumerate paired devices and inspect connected targets without sending setting commands. Review personal device names and error paths before sharing this report. |

Current exclusions include Windows output switching, forced Bluetooth reconnects,
global hotkeys, firmware flashing, factory reset and full phone-app parity.
Changing a headphone codec preference does not add that codec to Windows.

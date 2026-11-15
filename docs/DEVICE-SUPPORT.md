# Device implementation notes

For the feature summary, see the [device support table](../README.md#device-support).
For setup and controls, see the [user guide](USAGE.md).

## Soundcore Liberty 4 Pro and Life P3

Liberty 4 Pro (A3954) and Life P3 (A3939) use the pinned OpenSCQ30 v2.13.0 engine.
SoundCity reads the reported schema, frequencies, gain ranges and current settings
instead of hardcoding another model's controls. Simulated reads and writes pass;
physical tests against the phone app are still needed. Life P3 requires model
confirmation before registration. Refreshing capabilities before a write protects
against controls becoming unavailable after codec, multipoint or audio-mode changes.

## UGREEN Studio Max5s

Studio Max5s is HP208 / SKU 16172. It uses **UG3**, not the Max5c / Studio Pro UG1
protocol. Packet framing, encryption handshake and exact-model command mappings
were identified in the official UGREEN Audio 2.7.0 app. Encrypted status reads were
verified on HP208 firmware 0.1.5. Initial physical validation was read only.

Implemented controls include reported ANC mode/strength, eight EQ presets and
custom bands, spatial/game modes, wind reduction, multipoint, codec preference,
prompts and known button assignments. Writes are serialized, validated against a
fresh read, acknowledged and checked by reading back the stored value. Physical
setting-change validation is still pending. Personalized hearing profiles and
some prompt/button options remain unfinished. Unknown fields stay absent or
read only; missing ANC strength never becomes an invented default.

Custom EQ uses eight fallback frequencies (62, 250, 500, 1000, 2000, 4000, 8000 and
16000 Hz), with whole-dB gains from -6 to +6, matching the official app. Saved
curves use the frequencies read from the device. An empty curve creates a clearly
labelled draft while the current preset remains active. Writes use UG3 command
21 and verify stored gains and active mode. Unsupported staging commands from
firmware 0.1.5 are not used. A custom-EQ write still needs a physical listening test.

Identical repeated status fields are accepted, including nested battery, button
and prompt values. Conflicts trigger one fresh read of that field; persistent
conflicts stop instead of choosing an arbitrary value. Truncated packets and
duplicate encryption-handshake fields remain rejected. Recovery never replays a
setting change. Firmware flashing and factory reset are not exposed.

## UGREEN FitBuds

FitBuds is WS213 / SKU 65655. Discovery, service inspection and matching Windows
volume/mute are available, with optional Windows EQ and boost after APO setup.
Vendor controls still require exact-model protocol verification. The Max5s driver
must not be substituted for FitBuds.

## Parrot Zik 3

This is an original C# transport/backend based on the public protocol references below. It is experimental pending a physical Zik 3 test. The referenced Linux audio-routing gist only switches PulseAudio streams; it is not a vendor-control protocol and is not used in Windows.

- Parroter, specifically for Zik 3 on Windows: `6bc4bbdbb00aa7a420c1ea0890bc99ac22a18d26`.
- pyParrotZik: `3f8786741afa17c30e23186c465fe870b2de9efe`.
- zik2ctl, for frame boundaries and session ACK: `1be3290fb866b16c4dc2abea10d50e3137bcbfff`.
- parrot-zik-status's public XML examples corroborate response paths and attribute names.

Zik 3 advertises RFCOMM service `8b6814d3-6ce7-4498-9700-9312c1711f64`. The client sends `00 03 00` to open a session and expects `00 03 02`. Requests have a big-endian total length, message type `80` and ASCII `GET /api/.../get` or `SET /api/.../set?arg=...`. Response XML starts after a seven-byte header. The implementation reads exact frame boundaries, limits size/time, prohibits XML DTDs and correlates each answer with the requested path. Notifications do not complete a request.

| Feature | Read path | Write |
| --- | --- | --- |
| Battery and firmware | `/api/system/battery`, `/api/software/version` | Read only |
| Noise-control switch | `/api/audio/noise_control/enabled` | Boolean |
| Listening mode | `/api/audio/noise_control` | `off&value=1`, `anc&value=1/2`, `aoc&value=1/2` |
| Concert Hall | `/api/audio/sound_effect` | `/enabled`, `/room_size`, `/angle` |
| Saved onboard EQ switch | `/api/audio/equalizer/enabled` | Boolean |
| Head detection | `/api/system/head_detection/enabled` | Boolean |
| Auto-connect | `/api/system/auto_connection/enabled` | Boolean |

Only recognized reported fields become editable. An explicit endpoint rejection hides that endpoint; transport, malformed-XML and mismatched-response failures stop the read. Writes re-read capabilities, validate input, send once and verify via fresh reads. An uncertain acknowledgement never triggers an automatic write retry. Custom onboard EQ, auto ANC, resets, flight mode and firmware updates remain excluded.

The tests use synthetic protocol fixtures. They cover framing, fragmentation, coalescing, request correlation, malformed XML, capability changes and disagreeing readback. They do not prove compatibility with every firmware version or Bluetooth adapter.

## Beats Solo Buds

Solo Buds model codes are A3150/A3151/A3152. The referenced AutoEq URL points to Studio Buds, a different product. No measurement or filter from that URL is distributed or applied. Solo Buds lack active noise cancellation and transparency; the UI does not offer them. Windows volume/mute and optional Equalizer APO processing are available. Vendor battery/button communication is not implemented. Physical endpoint validation is pending.

## AirPods Pro 3

Apple's identification page lists A3063/A3064/A3065 for AirPods Pro 3. The app accepts the exact name or an explicit saved model selection for a renamed pair. Generic “AirPods Pro” is ambiguous and is not inferred as generation 3.

NTPods Windows source/docs (`188e45df20259b616ce155be812086259d1a2488`) show that advanced Apple features require the AAP L2CAP channel and a kernel-mode bridge on Windows. This build includes no such driver/daemon integration. It exposes Windows audio controls and optional PC processing, with the limitations visible in the UI. No Apple packets are sent, no third-party driver is installed, and no Windows security setting changes.

## Windows EQ and boost

The optional APO configuration remains in `SoundCity/boost.txt` to preserve existing installations. Each entry is restricted to the selected playback GUID. EQ has ten fixed frequencies and gains in tenths of a dB. Its positive peak is subtracted from preamp gain to reserve headroom; explicit boost is added once. EQ and boost can be independently disabled, and saving one retains the other. Existing configurations are backed up before adding the include; externally edited managed files are rejected rather than overwritten.

Equalizer APO must be installed and enabled for the endpoint. Configuration presence is not evidence of active audio processing. Automated configuration tests do not replace a listening test of the active playback path.

## Next physical validation

- Compare Liberty 4 Pro's reported firmware, battery, EQ and listening modes with the phone app. Confirm A3939 before testing Life P3.
- Validate Max5s setting changes one at a time against the official phone app.
- Verify FitBuds' exact protocol before enabling vendor writes.
- Pair Zik 3 over Bluetooth and compare battery, noise mode and Concert Hall state with the phone app before testing one reversible change at a time.
- Verify exact Windows endpoint matching on Solo Buds and AirPods Pro 3. Advanced Apple controls still require a separate driver integration and are not included.

# Device implementation notes

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

The requested product is Solo Buds (A3150/A3151/A3152). The AutoEq URL points to Studio Buds, a different product. No measurement or filter from that URL is distributed or applied. Solo Buds lack active noise cancellation and transparency; the UI does not offer them. Windows volume/mute and optional Equalizer APO processing are available. Vendor battery/button communication is not implemented.

## AirPods Pro 3

Apple's identification page lists A3063/A3064/A3065 for AirPods Pro 3. The app accepts the exact name or an explicit saved model selection for a renamed pair. Generic “AirPods Pro” is ambiguous and is not inferred as generation 3.

NTPods Windows source/docs (`188e45df20259b616ce155be812086259d1a2488`) show that advanced Apple features require the AAP L2CAP channel and a kernel-mode bridge on Windows. This build includes no such driver/daemon integration. It exposes Windows audio controls and optional PC processing, with the limitations visible in the UI. No Apple packets are sent, no third-party driver is installed, and no Windows security setting changes.

## Windows EQ and boost

The optional APO configuration remains in `SoundCity/boost.txt` to preserve existing installations. Each entry is restricted to the selected playback GUID. EQ has ten fixed frequencies and gains in tenths of a dB. Its positive peak is subtracted from preamp gain to reserve headroom; explicit boost is added once. EQ and boost can be independently disabled, and saving one retains the other. Existing configurations are backed up before adding the include; externally edited managed files are rejected rather than overwritten.

Equalizer APO must be installed and enabled for the endpoint. Configuration presence is not evidence of active audio processing. Neither APO installation nor a listening test was performed during development.

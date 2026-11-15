using System.Globalization;
using System.Text.Json;

namespace SoundCity.Core;

public sealed class UgreenBackend
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly byte[] ReadTags = [1, 2, 4, 5, 6, 7, 8, 14, 15, 17, 19, 23, 27, 29, 31];
    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    // UGREEN Audio 2.7.0: EqualizerCustom's fallback bands for an empty saved curve.
    private static readonly int[] DefaultEqBands = [62, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public async Task<IReadOnlyList<DeviceSetting>> ReadAsync(Headphone device, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var session = await UgreenTransport.OpenAsync(device, token);
            return ParseSettings(await ReadFieldsAsync(session, token));
        }
        finally { gate.Release(); }
    }

    private static async Task<Dictionary<byte, byte[]>> ReadFieldsAsync(UgreenTransport session, CancellationToken token)
        => await ReadStatusFieldsAsync((payload, ct) => session.RequestAsync(1, payload, ct), token);

    internal static async Task<Dictionary<byte, byte[]>> ReadStatusFieldsAsync(Func<byte[], CancellationToken, Task<byte[]>> request, CancellationToken token)
    {
        var fields = new Dictionary<byte, byte[]>();
        var conflicts = new HashSet<byte>();
        // Keep replies below the device's single-frame size, including custom EQ data.
        foreach (var group in ReadTags.Chunk(4))
        {
            var response = await request(group.SelectMany(t => Ug3Protocol.Tlv(t, 0)).ToArray(), token);
            foreach (var field in Ug3Protocol.ParseTlvList(response).Where(f => ReadTags.Contains(f.Key)).GroupBy(f => f.Key))
            {
                var value = field.First().Value;
                if (field.Any(f => !f.Value.AsSpan().SequenceEqual(value)) || HasNestedConflict(field.Key, value))
                {
                    conflicts.Add(field.Key);
                    fields.Remove(field.Key);
                }
                else if (!conflicts.Contains(field.Key)) fields[field.Key] = value;
            }
        }
        // Never choose an arbitrary value for a conflicting repeated status. A
        // fresh, single-field query resolves races without resending a setting.
        foreach (var tag in conflicts.Order())
        {
            token.ThrowIfCancellationRequested();
            var response = await request(Ug3Protocol.Tlv(tag, 0), token);
            var matching = Ug3Protocol.ParseTlvList(response).Where(f => f.Key == tag).ToArray();
            if (matching.Length == 0 || matching.Any(f => !f.Value.AsSpan().SequenceEqual(matching[0].Value)) || HasNestedConflict(tag, matching[0].Value))
                throw new Ug3ConflictingStatusException(tag);
            fields[tag] = matching[0].Value;
        }
        return fields;
    }

    private static bool HasNestedConflict(byte tag, byte[] value)
    {
        if (tag is not (1 or 5 or 7)) return false;
        try { Ug3Protocol.ParseStatusTlv(value); return false; }
        catch (Ug3ConflictingStatusException) { return true; }
    }

    public async Task SetAsync(Headphone device, DeviceSetting prior, string input, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var session = await UgreenTransport.OpenAsync(device, token);
            var settings = ParseSettings(await ReadFieldsAsync(session, token));
            var current = settings.SingleOrDefault(s => s.Id == prior.Id && s.Editable)
                ?? throw new InvalidOperationException("This Max5s setting is no longer available. Refresh controls.");
            var validated = current.Validate(input);
            if (current.Type == "equalizer" && !EqBands(current).SequenceEqual(EqBands(prior)))
                throw new InvalidOperationException("The headphone EQ bands changed. Refresh controls before applying this curve.");
            var (command, payload) = EncodeSetting(current, validated);
            try { await session.RequestAsync(command, payload, token); }
            catch (Exception ex) { throw new IOException("The setting request was sent, but acknowledgement was not received. Refresh controls to check the headphone state.", ex); }
            await Task.Delay(1000, token);
            var readback = ParseSettings(await ReadFieldsAsync(session, token));
            var updated = readback.SingleOrDefault(s => s.Id == current.Id);
            var matches = updated is not null && (current.Type == "equalizer"
                ? updated.Value.ValueKind == JsonValueKind.Array && EqBands(current).SequenceEqual(EqBands(updated))
                  && updated.Value.EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(validated.Split(',').Select(int.Parse))
                : string.Equals(updated.CurrentText, validated, StringComparison.OrdinalIgnoreCase));
            if (!matches)
                throw new IOException("The headphones acknowledged the request, but reported a different value. Refresh controls to check their current state.");
            if (current.Type == "equalizer" && readback.SingleOrDefault(s => s.Id == "equalizerPreset")?.CurrentText != "160")
                throw new IOException("The custom curve was saved, but the headphones did not report Custom EQ as active. Refresh controls and select Custom in EQ preset.");
        }
        finally { gate.Release(); }
    }

    public static (byte Command, byte[] Payload) EncodeSetting(DeviceSetting setting, string input)
    {
        input = setting.Validate(input);
        if (setting.Id == "customEqualizer" && setting.Type == "equalizer")
        {
            var bands = EqBands(setting);
            var gains = input.Split(',').Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            if (!ValidEqBands(bands) || gains.Any(g => g is < -6 or > 6)) throw new ArgumentException("Invalid Max5s EQ curve.");
            var bytes = bands.SelectMany((frequency, i) => new byte[] { (byte)(frequency >> 8), (byte)frequency, (byte)(gains[i] + 6) }).ToArray();
            // UG3 CUSTOM_EQ (not the staging command unsupported through firmware 0.1.5).
            return (21, Ug3Protocol.Tlv(1, bytes));
        }
        var (command, tag) = setting.Id switch
        {
            "listeningMode" => (6, 1), "noiseCancellationDepth" => (6, 2), "noiseCancellationLevel" => (6, 3),
            "equalizerPreset" => (2, 1), "gameMode" => (5, 1), "spatialAudio" => (12, 1),
            "windNoiseReduction" => (16, 1), "multipoint" => (3, 1), "highQualityAudio" => (8, 1),
            "maximumVolumePrompt" => (25, 1), "promptLanguage" => (9, 1),
            "ancButtonSinglePress" => (14, 3), "ancButtonDoublePress" => (14, 7), "ancButtonLongPress" => (14, 15),
            "volumeUpLongPress" => (14, 13), "volumeDownLongPress" => (14, 14),
            _ => throw new ArgumentException("No verified Max5s command exists for this setting.")
        };
        var value = setting.Type == "toggle" ? (byte)(bool.Parse(input) ? 1 : 0) : byte.Parse(input, CultureInfo.InvariantCulture);
        return ((byte)command, Ug3Protocol.Tlv((byte)tag, value));
    }

    public static IReadOnlyList<DeviceSetting> ParseSettings(IReadOnlyDictionary<byte, byte[]> fields)
    {
        var settings = new List<DeviceSetting>();
        void Info(string id, object value) => settings.Add(new(id, "deviceInformation", "information", Json(new { }), Json(value)));
        void Choice(string id, string category, byte value, (byte Code, string Name)[] choices)
        {
            if (!choices.Any(c => c.Code == value)) { Info(id, $"{value} · unrecognized value"); return; }
            settings.Add(new(id, category, "select", Json(new { options = choices.Select(c => c.Code.ToString()).ToArray(), localizedOptions = choices.Select(c => c.Name).ToArray() }), Json(value.ToString())));
        }
        void Select(byte tag, string id, string category, params (byte, string)[] choices)
        {
            if (fields.TryGetValue(tag, out var value) && value.Length == 1) Choice(id, category, value[0], choices);
        }
        void Toggle(byte tag, string id, string category)
        {
            if (fields.TryGetValue(tag, out var value) && value.Length == 1 && value[0] <= 1)
                settings.Add(new(id, category, "toggle", Json(new { }), Json(value[0] == 1)));
        }

        Select(8, "listeningMode", "soundModes", (0, "Normal"), (1, "Noise cancelling"), (2, "Transparency"));
        Select(19, "noiseCancellationDepth", "soundModes", (0, "Adaptive"), (1, "Light"), (2, "Medium"), (3, "Deep"));
        Select(31, "noiseCancellationLevel", "soundModes", (0, "Level 1"), (1, "Level 2"), (2, "Level 3"), (3, "Level 4"), (4, "Level 5"));
        Toggle(23, "windNoiseReduction", "soundModes");
        var customEq = ParseEqualizer(fields);
        var presets = new List<(byte, string)> { (0, "Classic"), (1, "Jazz"), (2, "Electronic"), (3, "Pop"), (4, "Classical"), (5, "Rock"), (6, "Bass"), (7, "Treble") };
        if (customEq?.Value.ValueKind == JsonValueKind.Array) presets.Add((160, "Custom"));
        Select(4, "equalizerPreset", "equalizer", presets.ToArray());
        if (customEq is not null) settings.Add(customEq);
        Toggle(14, "spatialAudio", "sound");
        Toggle(6, "gameMode", "sound");
        Toggle(15, "multipoint", "connections");
        Toggle(17, "highQualityAudio", "connections");
        Toggle(29, "maximumVolumePrompt", "devicePreferences");
        if (fields.TryGetValue(7, out var prompt))
        {
            var prompts = Ug3Protocol.ParseStatusTlv(prompt);
            if (prompts.TryGetValue(1, out var language) && language.Length == 1)
                Choice("promptLanguage", "devicePreferences", language[0], [(0, "Tone"), (1, "English"), (2, "Chinese")]);
            if (prompts.TryGetValue(2, out var volume) && volume.Length == 1) Info("promptVolume", volume[0]);
        }
        if (fields.TryGetValue(5, out var buttons))
        {
            var buttonFields = Ug3Protocol.ParseStatusTlv(buttons);
            foreach (var (tag, id) in new (byte, string)[] { (3, "ancButtonSinglePress"), (7, "ancButtonDoublePress"), (15, "ancButtonLongPress"), (13, "volumeUpLongPress"), (14, "volumeDownLongPress") })
            {
                if (!buttonFields.TryGetValue(tag, out var value) || value.Length != 1) continue;
                (byte, string)[] choices = tag is 13 or 14 ? [(0, "None"), (3, "Previous track"), (4, "Next track")] : [(0, "None"), (9, "Noise control"), (8, "Game mode"), (16, "Music mode")];
                if (value[0] == 11) Info(id, "Voice assistant · current assignment");
                else Choice(id, "buttons", value[0], choices);
            }
        }
        if (fields.TryGetValue(1, out var battery))
        {
            var levels = Ug3Protocol.ParseStatusTlv(battery);
            if (levels.TryGetValue(1, out var value) && value.Length == 1 && (value[0] & 127) <= 100)
                Info("batteryLevel", $"{value[0] & 127}%");
        }
        if (fields.TryGetValue(2, out var firmware) && firmware.Length >= 3) Info("firmwareVersion", string.Join('.', firmware.Take(3)));
        if (settings.Count == 0) throw new IOException("The Max5s returned no recognized settings.");
        return settings;
    }

    private static int[] EqBands(DeviceSetting setting) => setting.Parameters.GetProperty("bandHz").EnumerateArray().Select(v => v.GetInt32()).ToArray();
    private static bool ValidEqBands(int[] bands) => bands.Length is >= 2 and <= 32 && bands.All(f => f is >= 20 and <= 20000)
        && bands.Zip(bands.Skip(1)).All(p => p.First < p.Second);

    private static DeviceSetting? ParseEqualizer(IReadOnlyDictionary<byte, byte[]> fields)
    {
        if (!fields.TryGetValue(27, out var payload)) return null;
        int[] bands, gains;
        if (payload.Length == 0)
        {
            bands = DefaultEqBands;
            // Null means a new draft, never a fabricated device reading.
            gains = [];
        }
        else
        {
            if (payload.Length % 3 != 0) return null;
            bands = payload.Chunk(3).Select(b => (b[0] << 8) | b[1]).ToArray();
            gains = payload.Chunk(3).Select(b => b[2] - 6).ToArray();
            if (!ValidEqBands(bands) || gains.Any(g => g is < -6 or > 6)) return null;
        }
        return new("customEqualizer", "equalizer", "equalizer", Json(new { bandHz = bands, fractionDigits = 0, min = -6, max = 6 }),
            gains.Length == 0 ? JsonSerializer.SerializeToElement<object?>(null) : Json(gains));
    }
}

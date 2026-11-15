using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;

namespace SoundCity.Core;

/// <summary>Documented Zik 3 settings only. A successful fresh read gates every write.</summary>
public sealed class ParrotBackend
{
    private readonly SemaphoreSlim gate = new(1, 1);
    internal static readonly string[] ReadPaths = [
        "/api/system/battery", "/api/software/version", "/api/audio/noise_control",
        "/api/audio/noise_control/enabled", "/api/audio/sound_effect",
        "/api/audio/equalizer/enabled", "/api/system/head_detection/enabled",
        "/api/system/auto_connection/enabled"
    ];

    public async Task<IReadOnlyList<DeviceSetting>> ReadAsync(Headphone device, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try { using var session = await ParrotTransport.OpenAsync(device, token); return await ReadSettingsAsync(session.RequestAsync, token); }
        finally { gate.Release(); }
    }

    internal static async Task<IReadOnlyList<DeviceSetting>> ReadSettingsAsync(Func<string, string?, CancellationToken, Task<XElement>> request, CancellationToken token)
    {
        var responses = new Dictionary<string, XElement>();
        var rejected = 0;
        foreach (var path in ReadPaths)
        {
            try { responses[path] = await request(path, null, token); }
            catch (ParrotRequestRejectedException) { rejected++; }
        }
        var settings = ParseSettings(responses).ToList();
        if (settings.Count == 0) throw new IOException("The Zik 3 returned no recognized settings. Export connection details for this firmware.");
        if (rejected > 0) settings.Add(new("unavailableSettings", "deviceInformation", "information", Json(new { }), Json($"{rejected} requests were rejected by this firmware; those controls are hidden.")));
        return settings;
    }

    public async Task SetAsync(Headphone device, DeviceSetting prior, string input, CancellationToken token)
    {
        await gate.WaitAsync(token);
        try
        {
            using var session = await ParrotTransport.OpenAsync(device, token);
            await ApplyAsync(session.RequestAsync, prior, input, token);
        }
        finally { gate.Release(); }
    }

    internal static async Task ApplyAsync(Func<string, string?, CancellationToken, Task<XElement>> request, DeviceSetting prior, string input, CancellationToken token)
    {
        var settings = await ReadSettingsAsync(request, token);
        var current = settings.SingleOrDefault(s => s.Id == prior.Id && s.Editable)
            ?? throw new InvalidOperationException("This Zik 3 control is no longer available. Refresh controls.");
        input = current.Validate(input);
        var (path, argument) = EncodeSetting(current, input);
        try { await request(path, argument, token); }
        catch (Exception ex) when (ex is not ParrotRequestRejectedException)
        { throw new IOException("The Parrot request may have been sent, but no acknowledgement was received. Refresh controls before retrying.", ex); }
        var readback = await ReadSettingsAsync(request, token);
        if (!string.Equals(readback.SingleOrDefault(s => s.Id == current.Id)?.CurrentText, input, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Zik 3 acknowledged the request but reported a different value. Refresh controls to check its state.");
    }

    public static (string Path, string Argument) EncodeSetting(DeviceSetting setting, string input)
    {
        input = setting.Validate(input);
        var path = setting.Id switch
        {
            "noiseControlEnabled" => "/api/audio/noise_control/enabled",
            "noiseControlMode" => "/api/audio/noise_control",
            "concertHallEnabled" => "/api/audio/sound_effect/enabled",
            "concertHallRoom" => "/api/audio/sound_effect/room_size",
            "concertHallAngle" => "/api/audio/sound_effect/angle",
            "equalizerEnabled" => "/api/audio/equalizer/enabled",
            "headDetection" => "/api/system/head_detection/enabled",
            "autoConnection" => "/api/system/auto_connection/enabled",
            _ => throw new ArgumentException("No documented Zik 3 command exists for this setting.")
        };
        var argument = setting.Id == "noiseControlMode" ? input switch
        {
            "anc1" => "anc&value=1", "anc2" => "anc&value=2", "off" => "off&value=1",
            "aoc1" => "aoc&value=1", "aoc2" => "aoc&value=2", _ => throw new ArgumentException("Unknown Parrot listening mode.")
        } : input;
        return (path, argument);
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    public static IReadOnlyList<DeviceSetting> ParseSettings(IReadOnlyDictionary<string, XElement> responses)
    {
        var result = new List<DeviceSetting>();
        XElement? Element(string path, params string[] children)
        {
            if (!responses.TryGetValue(path, out var value)) return null;
            foreach (var child in children) { value = value?.Element(child); }
            return value;
        }
        void Info(string id, object? value) { if (value is not null) result.Add(new(id, "deviceInformation", "information", Json(new { }), Json(value))); }
        void Toggle(string id, string category, XElement? element, string attribute = "enabled")
        {
            var value = (string?)element?.Attribute(attribute);
            if (bool.TryParse(value, out var flag)) result.Add(new(id, category, "toggle", Json(new { }), Json(flag)));
            else if (value is not null) Info(id, value + " (read only)");
        }
        void Choice(string id, string category, string? value, string[] choices, string[] labels)
        {
            if (value is null) return;
            if (!choices.Contains(value)) { Info(id, value + " (unrecognized value)"); return; }
            result.Add(new(id, category, "select", Json(new { options = choices, localizedOptions = labels }), Json(value)));
        }
        var battery = Element("/api/system/battery", "system", "battery");
        if (int.TryParse((string?)battery?.Attribute("percent"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent) && percent is >= 0 and <= 100) Info("batteryLevel", $"{percent}%");
        Info("batteryState", (string?)battery?.Attribute("state"));
        var software = Element("/api/software/version", "software");
        Info("firmwareVersion", (string?)software?.Attribute("sip6") ?? (string?)software?.Attribute("version"));
        var noise = Element("/api/audio/noise_control", "audio", "noise_control");
        var kind = (string?)noise?.Attribute("type");
        var level = (string?)noise?.Attribute("value");
        Choice("noiseControlMode", "soundModes", kind is null ? null : kind == "off" ? "off" : kind + level,
            ["off", "anc1", "anc2", "aoc1", "aoc2"], ["Off", "Noise cancellation", "Maximum noise cancellation", "Street mode", "Maximum street mode"]);
        Toggle("noiseControlEnabled", "soundModes", Element("/api/audio/noise_control/enabled", "audio", "noise_control"));
        var concert = Element("/api/audio/sound_effect", "audio", "sound_effect");
        Toggle("concertHallEnabled", "concertHall", concert);
        Choice("concertHallRoom", "concertHall", (string?)concert?.Attribute("room_size"), ["silent", "living", "jazz", "concert"], ["Silent room", "Living room", "Jazz club", "Concert hall"]);
        Choice("concertHallAngle", "concertHall", (string?)concert?.Attribute("angle"), ["30", "60", "90", "120", "150", "180"], ["30°", "60°", "90°", "120°", "150°", "180°"]);
        Toggle("equalizerEnabled", "equalizer", Element("/api/audio/equalizer/enabled", "audio", "equalizer"));
        Toggle("headDetection", "devicePreferences", Element("/api/system/head_detection/enabled", "system", "head_detection"));
        Toggle("autoConnection", "devicePreferences", Element("/api/system/auto_connection/enabled", "system", "auto_connection"));
        return result;
    }
}

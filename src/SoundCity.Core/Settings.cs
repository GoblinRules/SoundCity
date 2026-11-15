using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoundCity.Core;

public sealed record DeviceSetting(string Id, string Category, string Type, JsonElement Parameters, JsonElement Value)
{
    public string Label => Labels.Humanize(Id);
    public string CurrentText => Value.ValueKind switch
    {
        JsonValueKind.String => Value.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        JsonValueKind.Array => string.Join(", ", Value.EnumerateArray().Select(v => v.ToString())),
        _ => Value.ToString()
    };
    public string[] Options => Parameters.ValueKind == JsonValueKind.Object && Parameters.TryGetProperty("options", out var options)
        ? options.EnumerateArray().Select(v => v.GetString() ?? "").ToArray() : [];
    public string[] LocalizedOptions => Parameters.ValueKind == JsonValueKind.Object && Parameters.TryGetProperty("localizedOptions", out var options)
        ? options.EnumerateArray().Select(v => v.GetString() ?? "").ToArray() : Options;
    public bool Editable => Type is "toggle" or "i32Range" or "select" or "optionalSelect" or "modifiableSelect" or "multiSelect" or "multiSelectWithRemove" or "equalizer" or "action" or "importString" or "timeOfDay" or "hueColorPicker";

    public string Validate(string input)
    {
        switch (Type)
        {
            case "information": throw new ArgumentException("This setting is read only.");
            case "toggle":
                if (!bool.TryParse(input, out var toggle)) throw new ArgumentException("Choose on or off.");
                return toggle ? "true" : "false";
            case "select":
            case "optionalSelect":
                if (Type == "optionalSelect" && input.Length == 0) return "";
                if (!Options.Contains(input)) throw new ArgumentException("Choose an available option.");
                return input;
            case "modifiableSelect":
                if (input.StartsWith('+') && input.Length > 1) return input;
                if (input.StartsWith('-') && Options.Contains(input[1..])) return input;
                var name = input.StartsWith('\\') ? input[1..] : input;
                if (!Options.Contains(name)) throw new ArgumentException("Choose an existing profile or enter +name to save one.");
                return "\\" + name;
            case "multiSelectWithRemove":
                var target = input.StartsWith('-') || input.StartsWith('\\') ? input[1..] : input;
                if (!Options.Contains(target)) throw new ArgumentException("Choose an available device.");
                return input.StartsWith('-') ? input : "\\" + target;
            case "multiSelect":
                var values = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (values.Any(v => !Options.Contains(v))) throw new ArgumentException("Choose available options only.");
                return string.Join(',', values);
            case "equalizer":
                var bands = input.Split(',', StringSplitOptions.TrimEntries);
                if (bands.Length != Parameters.GetProperty("bandHz").GetArrayLength()) throw new ArgumentException("Provide one gain for every EQ band.");
                var gains = bands.Select(v => int.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                if (gains.Any(v => v < Parameters.GetProperty("min").GetInt32() || v > Parameters.GetProperty("max").GetInt32())) throw new ArgumentException("An EQ gain is outside the device's supported range.");
                return string.Join(',', gains);
            case "i32Range":
                var number = int.Parse(input, CultureInfo.InvariantCulture);
                var step = Parameters.GetProperty("step").GetInt32();
                if (number < Parameters.GetProperty("start").GetInt32() || number > Parameters.GetProperty("end").GetInt32() || step <= 0 || number % step != 0) throw new ArgumentException("Value is outside the supported range or step.");
                return number.ToString(CultureInfo.InvariantCulture);
            case "action": return "";
            case "timeOfDay":
                if (!TimeOnly.TryParseExact(input, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) throw new ArgumentException("Use 24-hour time, HH:mm.");
                return input;
            case "hueColorPicker":
                var hue = float.Parse(input, CultureInfo.InvariantCulture);
                if (!float.IsFinite(hue) || hue < 0 || hue > 360) throw new ArgumentException("Hue must be between 0 and 360.");
                return input;
            case "importString": return input;
            default: throw new ArgumentException("This control type is not supported by this SoundCity version.");
        }
    }
}

public static class Labels
{
    public static string Humanize(string value)
    {
        if (value.StartsWith("ancButton", StringComparison.Ordinal)) return "ANC button " + Humanize(value[9..]).ToLowerInvariant();
        if (value == "equalizerPreset") return "EQ preset";
        var text = Regex.Replace(value, "([a-z0-9])([A-Z])", "$1 $2");
        text = Regex.Replace(text, "([A-Z])([A-Z][a-z])", "$1 $2");
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }
}

public static class SettingsParser
{
    public static IReadOnlyList<DeviceSetting> Parse(string schemaJson, string valuesJson)
    {
        using var schema = JsonDocument.Parse(schemaJson);
        using var values = JsonDocument.Parse(valuesJson);
        var valueMap = values.RootElement.EnumerateArray().GroupBy(v => v.GetProperty("settingId").GetString()!).ToDictionary(g => g.Key, g => g.Last().GetProperty("value").GetProperty("value").Clone());
        var result = new List<DeviceSetting>();
        foreach (var category in schema.RootElement.EnumerateArray())
        foreach (var setting in category.GetProperty("settings").EnumerateArray())
        {
            var id = setting.GetProperty("settingId").GetString()!;
            result.Add(new(id, category.GetProperty("categoryId").GetString()!, setting.GetProperty("type").GetString()!,
                setting.TryGetProperty("setting", out var parameters) ? parameters.Clone() : default,
                valueMap.TryGetValue(id, out var value) ? value : default));
        }
        return result;
    }
}

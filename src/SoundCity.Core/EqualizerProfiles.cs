using System.Globalization;
using System.Text.Json;

namespace SoundCity.Core;

public sealed record EqualizerProfile(string Name, int[] BandHz, double[] GainsDb);

public sealed class EqualizerProfiles(string directory)
{
    public IReadOnlyList<EqualizerProfile> Read(string model)
    {
        var path = FilePath(model);
        if (!File.Exists(path)) return [];
        var profiles = JsonSerializer.Deserialize<EqualizerProfile[]>(File.ReadAllText(path)) ?? [];
        foreach (var profile in profiles) Validate(profile);
        return profiles;
    }

    public void Save(string model, EqualizerProfile profile)
    {
        Validate(profile);
        var profiles = Read(model).Where(p => !p.Name.Equals(profile.Name, StringComparison.OrdinalIgnoreCase)).Append(profile).OrderBy(p => p.Name).ToArray();
        Directory.CreateDirectory(directory);
        var path = FilePath(model);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }

    private string FilePath(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new ArgumentException("Invalid profile model.");
        return Path.Combine(directory, model + ".json");
    }

    public static void Validate(EqualizerProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 60 || profile.Name.Any(char.IsControl)) throw new ArgumentException("Use a profile name of 1 to 60 characters.");
        if (profile.BandHz is null || profile.GainsDb is null || profile.BandHz.Length is < 2 or > 32 || profile.BandHz.Length != profile.GainsDb.Length
            || profile.BandHz.Any(f => f is < 20 or > 24000) || !profile.BandHz.Zip(profile.BandHz.Skip(1)).All(p => p.First < p.Second)
            || profile.GainsDb.Any(g => !double.IsFinite(g) || g is < -24 or > 24)) throw new ArgumentException("Invalid EQ profile bands or gains.");
    }

    public static double[] Shape(string name, int[] bands)
    {
        // These are SoundCity starting points, not representations of vendor presets.
        double[] anchors = name switch
        {
            "Warm" => [3, 2, 0, -1, -2],
            "Bass lift" => [4, 3, 0, 0, 0],
            "Vocals" => [-2, -1, 2, 3, 0],
            "Bright" => [-1, 0, 0, 2, 3],
            _ => [0, 0, 0, 0, 0]
        };
        int[] hz = [62, 250, 1000, 4000, 16000];
        return bands.Select(f =>
        {
            if (f <= hz[0]) return anchors[0];
            if (f >= hz[^1]) return anchors[^1];
            var i = Enumerable.Range(0, hz.Length - 1).First(i => f >= hz[i] && f <= hz[i + 1]);
            var t = Math.Log((double)f / hz[i]) / Math.Log((double)hz[i + 1] / hz[i]);
            return anchors[i] + (anchors[i + 1] - anchors[i]) * t;
        }).ToArray();
    }
}

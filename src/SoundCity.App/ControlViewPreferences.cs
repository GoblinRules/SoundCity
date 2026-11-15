using System;
using System.IO;
using System.Text.Json;
using SoundCity.Core;

namespace SoundCity.App;

/// <summary>Display choices only; no audio settings or endpoint identifiers.</summary>
public sealed class ControlViewPreferences
{
    private sealed record Views(bool EqualizerSliders = false, bool BoostSliders = false);
    private Views values = new();
    private readonly string path;
    public static ControlViewPreferences Current { get; } = new(Path.Combine(AppPaths.DataDirectory, "control-views.json"));
    public ControlViewPreferences(string path)
    {
        this.path = path;
        try { if (File.Exists(path)) values = JsonSerializer.Deserialize<Views>(File.ReadAllText(path)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
    }
    public bool UsesSliders(bool boost) => boost ? values.BoostSliders : values.EqualizerSliders;
    public void Save(bool boost, bool sliders)
    {
        values = boost ? values with { BoostSliders = sliders } : values with { EqualizerSliders = sliders };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(values));
        File.Move(temporary, path, true);
    }
}

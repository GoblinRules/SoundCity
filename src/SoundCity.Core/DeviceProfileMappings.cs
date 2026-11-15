using System.Text.Json;

namespace SoundCity.Core;

/// <summary>Explicit local model selections for personally named headphones. Never guesses from a brand.</summary>
public sealed class DeviceProfileMappings(string file)
{
    public IReadOnlyDictionary<string, string> Read()
    {
        if (!File.Exists(file)) return new Dictionary<string, string>();
        var saved = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))
            ?? throw new IOException("The saved headphone model selections could not be read.");
        if (saved.Values.Any(id => !DeviceCatalog.All.Any(p => p.Id == id))) throw new IOException("A saved headphone model is no longer supported.");
        return saved;
    }

    public static DeviceProfile? Resolve(string name, ulong address, IReadOnlyDictionary<string, string> saved)
        => DeviceCatalog.Match(name) ?? (saved.TryGetValue(DeviceCatalog.FormatAddress(address), out var id) ? DeviceCatalog.All.SingleOrDefault(p => p.Id == id) : null);

    public void Save(ulong address, DeviceProfile? profile)
    {
        if (address is 0 or > 0xFFFFFFFFFFFF || (profile is not null && !DeviceCatalog.All.Contains(profile))) throw new ArgumentException("Choose a paired headphone and supported model.");
        var values = new Dictionary<string, string>(Read());
        var key = DeviceCatalog.FormatAddress(address);
        if (profile is null) values.Remove(key); else values[key] = profile.Id;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(values, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(file + ".tmp", file, true);
    }
}

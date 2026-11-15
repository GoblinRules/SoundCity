using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SoundCity.Core;

public sealed record VolumeBoostSetting(Guid Endpoint, double GainDb, bool Enabled,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int[]? EqGains = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool EqEnabled = false);

public enum ApoEndpointState { NotInstalled, EnhancementsDisabled, Configured, Unknown }

/// <summary>Optional Equalizer APO preamp. Never changes endpoint volume or installs an APO.</summary>
public sealed class EqualizerApo(string configDirectory)
{
    public static readonly int[] EqBands = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];
    private const string StatePrefix = "# SoundCity state: ";
    private const string Include = "Include: SoundCity\\boost.txt";
    private static readonly UTF8Encoding Utf8 = new(false);
    private string MainFile => Path.Combine(configDirectory, "config.txt");
    private string BoostFile => Path.Combine(configDirectory, "SoundCity", "boost.txt");

    public static string? FindConfigDirectory()
    {
        using var registry = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
        var paths = new[] { registry?.GetValue("ConfigPath") as string, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO", "config") };
        return paths.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p) && Path.IsPathFullyQualified(p) && File.Exists(Path.Combine(p, "config.txt")));
    }

    public static string? FindDeviceSetup()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EqualizerAPO");
        return new[] { Path.Combine(folder, "DeviceSelector.exe"), Path.Combine(folder, "Configurator.exe") }.FirstOrDefault(File.Exists);
    }

    public static Guid EndpointGuid(string endpointId)
    {
        var match = Regex.Match(endpointId, @"^\{0\.0\.0\.00000000\}\.\{(?<id>[0-9a-fA-F-]{36})\}$", RegexOptions.CultureInvariant);
        if (!match.Success || !Guid.TryParse(match.Groups["id"].Value, out var guid) || guid == Guid.Empty) throw new ArgumentException("This is not a Windows playback endpoint.");
        return guid;
    }

    // Registry configuration is not proof of live signal processing. Require a
    // post-mix APO, because SoundCity's EQ/boost file deliberately uses that stage.
    public static ApoEndpointState EndpointState(string endpointId)
    {
        var endpoint = EndpointGuid(endpointId);
        try
        {
            using var properties = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{endpoint:B}\FxProperties");
            return properties is null ? ApoEndpointState.NotInstalled : ParseEndpointState(properties.GetValueNames().ToDictionary(n => n, n => properties.GetValue(n), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { return ApoEndpointState.Unknown; }
    }

    internal static ApoEndpointState ParseEndpointState(IReadOnlyDictionary<string, object?> properties)
    {
        const string postMix = "{ec1cc9ce-faed-4822-828a-82a81a6f018f}";
        bool IsPostMix(object? value) => value is string text && string.Equals(text, postMix, StringComparison.OrdinalIgnoreCase)
            || value is string[] values && values.Any(v => string.Equals(v, postMix, StringComparison.OrdinalIgnoreCase));
        var installed = new[] { 2, 6, 7, 14, 15 }.Any(index => properties.TryGetValue($"{{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}},{index}", out var value) && IsPostMix(value));
        if (!installed) return ApoEndpointState.NotInstalled;
        if (properties.TryGetValue("{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5", out var disabled))
        {
            if (disabled is not int flag) return ApoEndpointState.Unknown;
            if (flag != 0) return ApoEndpointState.EnhancementsDisabled;
        }
        return ApoEndpointState.Configured;
    }

    public static string EndpointSetupMessage(ApoEndpointState state) => state switch
    {
        ApoEndpointState.NotInstalled => "SETUP NEEDED FOR THIS OUTPUT · Choose its stereo output or open device setup.",
        ApoEndpointState.EnhancementsDisabled => "AUDIO ENHANCEMENTS DISABLED · Enable them in Windows sound settings.",
        ApoEndpointState.Unknown => "OUTPUT SETUP COULD NOT BE VERIFIED · Check device setup.",
        _ => "EQUALIZER APO CONFIGURED FOR THIS OUTPUT"
    };

    public IReadOnlyList<VolumeBoostSetting> Read()
    {
        if (!File.Exists(BoostFile)) return [];
        var text = File.ReadAllText(BoostFile);
        var state = text.Split('\n').FirstOrDefault(l => l.StartsWith(StatePrefix, StringComparison.Ordinal))?.TrimEnd('\r');
        if (state is null) throw new IOException("The SoundCity boost file was edited externally. Restore it before applying a new boost.");
        var values = JsonSerializer.Deserialize<VolumeBoostSetting[]>(state[StatePrefix.Length..]) ?? [];
        if (BuildConfiguration(values).Replace("\r\n", "\n") != text.Replace("\r\n", "\n")) throw new IOException("The SoundCity boost file was edited externally. It has been left unchanged.");
        return values;
    }

    public void Save(VolumeBoostSetting setting)
    {
        if (!File.Exists(MainFile)) throw new IOException("Equalizer APO configuration was not found. Complete its setup first.");
        var values = Read().Where(v => v.Endpoint != setting.Endpoint).Append(setting).OrderBy(v => v.Endpoint).ToArray();
        var configuration = BuildConfiguration(values);
        var originalMain = File.ReadAllText(MainFile);
        var updatedMain = ConnectConfiguration(originalMain);
        Directory.CreateDirectory(Path.GetDirectoryName(BoostFile)!);
        var oldBoost = File.Exists(BoostFile) ? File.ReadAllText(BoostFile) : BuildConfiguration([]);
        if (updatedMain != originalMain)
            File.Copy(MainFile, Path.Combine(configDirectory, $"config.before-soundcity-{DateTime.UtcNow:yyyyMMddHHmmssfff}.txt"), false);
        WriteAtomic(BoostFile, configuration);
        try { if (updatedMain != originalMain) WriteAtomic(MainFile, updatedMain); }
        catch { WriteAtomic(BoostFile, oldBoost); throw; }
    }

    private static void WriteAtomic(string path, string text)
    {
        var temporary = path + ".soundcity.tmp";
        File.WriteAllText(temporary, text, Utf8);
        File.Move(temporary, path, true);
    }

    public static string ConnectConfiguration(string existing)
    {
        var matches = Regex.Matches(existing, @"(?im)^\s*Include:\s*SoundCity[\\/]boost\.txt\s*$");
        if (matches.Count > 1) throw new IOException("Multiple SoundCity boost includes were found. Remove the duplicate before applying boost.");
        return matches.Count == 1 ? existing : existing.TrimEnd('\r', '\n') + "\r\n\r\n# SoundCity volume boost\r\nDevice: all\r\nChannel: all\r\nStage: post-mix\r\n" + Include + "\r\n";
    }

    public static string BuildConfiguration(IReadOnlyList<VolumeBoostSetting> settings)
    {
        if (settings.Select(s => s.Endpoint).Distinct().Count() != settings.Count || settings.Any(s => s.Endpoint == Guid.Empty || !double.IsFinite(s.GainDb) || s.GainDb is < 0 or > 12 || s.GainDb * 2 != Math.Round(s.GainDb * 2)
            || (s.EqEnabled && s.EqGains is null) || (s.EqGains is not null && (s.EqGains.Length != EqBands.Length || s.EqGains.Any(g => g is < -120 or > 120)))))
            throw new ArgumentException("Choose a boost from 0 to 12 dB in 0.5 dB steps for a playback device.");
        var text = new StringBuilder(StatePrefix + JsonSerializer.Serialize(settings) + "\r\n# Managed by SoundCity. Change boost in the app.\r\n");
        foreach (var setting in settings.Where(s => s.Enabled || s.EqEnabled))
        {
            text.AppendLine($"Device: {setting.Endpoint:B}");
            text.AppendLine("Channel: all");
            text.AppendLine("Stage: post-mix");
            // Reserve headroom for boosted EQ bands. Explicit volume boost can consume that headroom.
            var headroom = setting.EqEnabled ? Math.Max(0, setting.EqGains!.Max() / 10.0) : 0;
            text.AppendLine($"Preamp: {((setting.Enabled ? setting.GainDb : 0) - headroom).ToString("0.0", CultureInfo.InvariantCulture)} dB");
            if (setting.EqEnabled) text.AppendLine("GraphicEQ: " + string.Join("; ", EqBands.Select((hz, i) => $"{hz} {(setting.EqGains![i] / 10.0).ToString("0.0", CultureInfo.InvariantCulture)}")));
        }
        text.AppendLine("Device: all");
        text.AppendLine("Channel: all");
        return text.ToString();
    }
}

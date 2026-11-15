using System.Text.RegularExpressions;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace SoundCity.Core;

public sealed record DeviceProfile(string Id, string Name, string Brand, string ModelCode, string? BackendModel, bool NeedsConfirmation = false)
{
    public HeadphoneBackend Backend => BackendModel is not null ? HeadphoneBackend.Soundcore : Id switch
    {
        "max5s" => HeadphoneBackend.Ugreen, "zik3" => HeadphoneBackend.Parrot, _ => HeadphoneBackend.WindowsOnly
    };
    public bool HasControls => Backend != HeadphoneBackend.WindowsOnly;
}

public enum HeadphoneBackend { WindowsOnly, Soundcore, Ugreen, Parrot }

public static class DeviceCatalog
{
    public static readonly DeviceProfile Liberty4Pro = new("liberty4pro", "Liberty 4 Pro", "Soundcore", "A3954", "SoundcoreA3954");
    public static readonly DeviceProfile LifeP3 = new("lifep3", "Life P3", "Soundcore", "A3939", "SoundcoreA3939", true);
    public static readonly DeviceProfile FitBuds = new("fitbuds", "FitBuds", "UGREEN", "WS213", null);
    public static readonly DeviceProfile Max5s = new("max5s", "Studio Max5s", "UGREEN", "HP208", null);
    public static readonly DeviceProfile Zik3 = new("zik3", "Zik 3", "Parrot", "Zik 3", null);
    public static readonly DeviceProfile SoloBuds = new("solobuds", "Solo Buds", "Beats", "A3150 / A3151 / A3152", null);
    public static readonly DeviceProfile AirPodsPro3 = new("airpodspro3", "AirPods Pro 3", "Apple", "A3063 / A3064 / A3065", null);
    public static readonly DeviceProfile[] All = [Liberty4Pro, LifeP3, FitBuds, Max5s, Zik3, SoloBuds, AirPodsPro3];

    public static DeviceProfile? Match(string name)
    {
        var normalized = Regex.Replace(name.ToLowerInvariant(), @"\s+", " ").Trim();
        // Exact names avoid confusing Liberty 4 / 4 NC, P3i, Max5c and Studio Pro.
        return normalized switch
        {
            "soundcore liberty 4 pro" => Liberty4Pro,
            "soundcore life p3" or "soundcore p3" => LifeP3,
            "ugreen fitbuds" => FitBuds,
            "ugreen studio max5s" or "ugreen hitune max5s" => Max5s,
            "parrot zik 3" or "parrot zik 3.0" or "zik 3" or "zik 3.0" => Zik3,
            "beats solo buds" => SoloBuds,
            "airpods pro 3" => AirPodsPro3,
            _ => null
        };
    }

    public static string FormatAddress(ulong address) => string.Join(":", Enumerable.Range(0, 6).Select(i => ((address >> ((5 - i) * 8)) & 255).ToString("X2")));
}

public sealed record Headphone(string Name, ulong BluetoothAddress, string WindowsId, bool Connected, DeviceProfile Profile)
{
    public string Address => DeviceCatalog.FormatAddress(BluetoothAddress);
    public string ConnectionLabel => Connected ? "Connected to Windows" : "Paired · not connected";
}

public sealed record ServiceInfo(string Uuid, string Name);
public sealed record ServiceReport(string DeviceName, string Model, string Connection, string Result, IReadOnlyList<ServiceInfo> Services);

public sealed record PairedDevice(string Name, ulong BluetoothAddress, string WindowsId, bool Connected);

public sealed class BluetoothDiscovery(DeviceProfileMappings? mappings = null)
{
    public async Task<IReadOnlyList<PairedDevice>> FindPairedAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<PairedDevice>();
        var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true),
            new[] { "System.Devices.Aep.DeviceAddress", "System.Devices.Aep.IsConnected" }, DeviceInformationKind.AssociationEndpoint).AsTask(cancellationToken);
        foreach (var info in devices)
        {
            if (!info.Properties.TryGetValue("System.Devices.Aep.DeviceAddress", out var addressValue)) continue;
            var addressText = addressValue?.ToString()?.Replace(":", "").Replace("-", "");
            if (!ulong.TryParse(addressText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var address)) continue;
            var connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) && value is true;
            results.Add(new(info.Name, address, info.Id, connected));
        }
        return results.DistinctBy(d => d.BluetoothAddress).OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToArray();
    }

    public async Task<IReadOnlyList<Headphone>> FindAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<Headphone>();
        var saved = mappings?.Read() ?? new Dictionary<string, string>();
        foreach (var info in await FindPairedAsync(cancellationToken))
        {
            var profile = DeviceProfileMappings.Resolve(info.Name, info.BluetoothAddress, saved);
            if (profile is null) continue;
            using var device = await BluetoothDevice.FromBluetoothAddressAsync(info.BluetoothAddress).AsTask(cancellationToken);
            if (device is null) continue;
            results.Add(new(info.Name, device.BluetoothAddress, info.WindowsId, device.ConnectionStatus == BluetoothConnectionStatus.Connected, profile));
        }
        return results.OrderByDescending(d => d.Connected).ThenBy(d => d.Name).ToArray();
    }

    public async Task<ServiceReport> InspectAsync(Headphone headphone, CancellationToken cancellationToken = default)
    {
        using var device = await BluetoothDevice.FromBluetoothAddressAsync(headphone.BluetoothAddress).AsTask(cancellationToken);
        if (device is null) throw new InvalidOperationException("Windows could not access this Bluetooth device.");
        // Service discovery only: never open an RFCOMM stream or send vendor packets.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var result = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached).AsTask(timeout.Token);
        // ConnectionServiceName can embed both host and headphone MAC addresses.
        // Store only protocol identifiers and generic names in shareable diagnostics.
        var services = result.Services.Select(s => new ServiceInfo(s.ServiceId.Uuid.ToString(), s.ServiceId.Uuid.ToString() switch
        {
            "00001101-0000-1000-8000-00805f9b34fb" => "Serial Port (SPP)",
            "0000111e-0000-1000-8000-00805f9b34fb" => "Hands-Free",
            "8b6814d3-6ce7-4498-9700-9312c1711f64" => "Parrot Zik 3 control",
            _ => "Bluetooth service"
        })).ToArray();
        foreach (var service in result.Services) service.Dispose();
        return new(headphone.Name, headphone.Profile.ModelCode, device.ConnectionStatus.ToString(), result.Error.ToString(), services);
    }
}

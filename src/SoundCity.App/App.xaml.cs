using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using SoundCity.Core;

namespace SoundCity.App;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length >= 2 && e.Args[0] == "--diagnose")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(80));
                var discovery = new BluetoothDiscovery(new DeviceProfileMappings(Path.Combine(AppPaths.DataDirectory, "device-models.json")));
                var devices = await discovery.FindAsync(timeout.Token);
                var report = new System.Collections.Generic.List<object>();
                foreach (var device in devices)
                {
                    object? services = null;
                    if (device.Connected)
                    {
                        try { services = await discovery.InspectAsync(device, timeout.Token); }
                        catch (Exception ex) { services = new { error = ex.Message }; }
                    }
                    report.Add(new { device.Name, model = device.Profile.ModelCode, device.Connected, requiresModelConfirmation = device.Profile.NeedsConfirmation, services });
                }
                var path = Path.GetFullPath(e.Args[1]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new { createdUtc = DateTimeOffset.UtcNow, devices = report }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex)
            {
                await File.WriteAllTextAsync(Path.GetFullPath(e.Args[1]), JsonSerializer.Serialize(new { error = ex.ToString() }));
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow(e.Args.Contains("--demo"), e.Args.Contains("--windows-eq"));
        MainWindow = window;
        window.Show();
    }
}

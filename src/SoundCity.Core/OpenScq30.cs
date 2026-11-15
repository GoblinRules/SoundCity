using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace SoundCity.Core;

public interface ICommandRunner
{
    Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}

public sealed class OpenScq30Runner(string executable, string databasePath) : ICommandRunner
{
    public const string ExpectedHash = "C4B24B7E59F56BBCFF0A240830A613CD51BEE2822808D5B6075C80087FE04BF0";
    private bool verified;

    public async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable)) throw new FileNotFoundException("Soundcore engine is missing. Run scripts/Build.ps1 to download the verified backend.", executable);
        if (!verified)
        {
            await using var file = File.OpenRead(executable);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
            if (hash != ExpectedHash) throw new InvalidOperationException("The Soundcore engine does not match the pinned v2.13.0 release. Run scripts/Get-Backend.ps1.");
            verified = true;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["OPENSCQ30_DATABASE_PATH"] = databasePath;
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the Soundcore engine.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch (InvalidOperationException) { }
            if (cancellationToken.IsCancellationRequested) throw;
            throw new TimeoutException("The headphones did not respond within 25 seconds. Check they are powered on and close any phone app controlling them.");
        }
        var output = await stdout;
        var error = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "The Soundcore engine failed to complete the command." : error.Trim());
        return output;
    }
}

public sealed class SoundcoreBackend(ICommandRunner runner)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task RegisterAsync(Headphone headphone, bool confirmed, CancellationToken cancellationToken = default)
    {
        if (headphone.Profile.BackendModel is null) throw new InvalidOperationException("This model has no verified Soundcore driver.");
        if (headphone.Profile.NeedsConfirmation && !confirmed) throw new InvalidOperationException("Confirm that this is Life P3 (A3939) before connecting its control driver.");
        await gate.WaitAsync(cancellationToken);
        try { await runner.RunAsync(["paired-devices", "add", "--mac-address", headphone.Address, "--model", headphone.Profile.BackendModel], cancellationToken); }
        finally { gate.Release(); }
    }

    public async Task<IReadOnlyList<DeviceSetting>> ReadAsync(string address, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try { return await ReadCoreAsync(address, cancellationToken); }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyList<DeviceSetting>> ReadCoreAsync(string address, CancellationToken cancellationToken)
    {
        var schema = await runner.RunAsync(["device", "--mac-address", address, "list-settings", "--json"], cancellationToken);
        using var document = JsonDocument.Parse(schema);
        var ids = document.RootElement.EnumerateArray().SelectMany(c => c.GetProperty("settings").EnumerateArray()).Select(s => s.GetProperty("settingId").GetString()!).Distinct().ToArray();
        if (ids.Length == 0) return [];
        var arguments = new List<string> { "device", "--mac-address", address, "setting", "--json" };
        foreach (var id in ids) { arguments.Add("--get"); arguments.Add(id); }
        var values = await runner.RunAsync(arguments, cancellationToken);
        return SettingsParser.Parse(schema, values);
    }

    public async Task SetAsync(string address, DeviceSetting setting, string input, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Availability can change after LDAC, multipoint or spatial audio is changed.
            // Re-read capabilities before every write; never reuse a stale command shape.
            var latest = await ReadCoreAsync(address, cancellationToken);
            var current = latest.FirstOrDefault(s => s.Id == setting.Id) ?? throw new InvalidOperationException("This control is no longer available. Refresh the device.");
            if (current.Type != setting.Type) throw new InvalidOperationException("The control changed. Refresh before applying it.");
            var value = current.Validate(input);
            await runner.RunAsync(["device", "--mac-address", address, "setting", "--set", current.Type == "action" ? current.Id : $"{current.Id}={value}"], cancellationToken);
        }
        finally { gate.Release(); }
    }
}

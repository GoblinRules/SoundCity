using System.Text.Json;
using SoundCity.Core;

var checks = 0;
void Check(bool condition, string label)
{
    if (!condition) throw new Exception("FAIL: " + label);
    checks++;
    Console.WriteLine("PASS: " + label);
}
async Task Reject(Func<Task> action, string label)
{
    try { await action(); }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { Check(true, label); return; }
    throw new Exception("FAIL: " + label);
}
JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

await NewDeviceChecks.RunAsync(Check);
AudioSetupChecks.Run(Check);

Check(DeviceCatalog.Match("soundcore Liberty 4 Pro") == DeviceCatalog.Liberty4Pro, "Exact Liberty 4 Pro mapping");
Check(DeviceCatalog.Match("Soundcore  P3") == DeviceCatalog.LifeP3, "P3 name normalization");
foreach (var name in new[] { "soundcore Liberty 4", "soundcore Liberty 4 NC", "Soundcore P3i", "UGREEN HiTune Max5c", "UGREEN Studio Pro", "Other FitBuds" })
    Check(DeviceCatalog.Match(name) is null, "Reject lookalike: " + name);
Check(DeviceCatalog.FormatAddress(0x0123456789AB) == "01:23:45:67:89:AB", "Address preserves leading zero");
var max5sAudio = new Headphone("UGREEN Studio Max5s", 1, "test", true, DeviceCatalog.Max5s);
Check(WindowsAudio.Matches("Headphones (UGREEN Studio Max5s)", max5sAudio), "Windows endpoint matches complete paired name");
Check(!WindowsAudio.Matches("Headphones (UGREEN Studio Max5c)", max5sAudio), "Windows audio rejects a different headphone model");
Check(!WindowsAudio.Matches("Speakers (Realtek Audio)", max5sAudio), "Windows audio never falls back to speakers");
Check(!WindowsAudio.Matches("Headphones (soundcore P3i)", new("soundcore P3", 1, "test", true, DeviceCatalog.LifeP3)), "Windows endpoint rejects lookalike model suffixes");
await Reject(() => { WindowsAudio.Set("unused", double.NaN, false); return Task.CompletedTask; }, "Non-finite volume rejected before accessing audio");
await Reject(() => { WindowsAudio.Set("unused", 101, false); return Task.CompletedTask; }, "Out-of-range volume rejected before accessing audio");
Check(Ug3Protocol.Crc("123456789"u8) == 0x29B1, "UG3 CRC matches the CCITT-FALSE standard vector");
var hp208Fields = Ug3Protocol.ParseTlv(Convert.FromHexString("01090101460201FF0301FF0206000105000000040100080100170100"));
foreach (var pair in Ug3Protocol.ParseTlv(Convert.FromHexString("050F03010907010B0F01080D01040E010306010007060101010201080E0100"))) hp208Fields[pair.Key] = pair.Value;
foreach (var pair in Ug3Protocol.ParseTlv(Convert.FromHexString("0F010011010013001701000A01001B001D01011F0104"))) hp208Fields[pair.Key] = pair.Value;
var hp208Settings = UgreenBackend.ParseSettings(hp208Fields);
Check(hp208Settings.Single(s => s.Id == "batteryLevel").CurrentText == "70%", "Captured HP208 nested battery parses without inventing case values");
Check(hp208Settings.Single(s => s.Id == "firmwareVersion").CurrentText == "0.1.5", "Captured HP208 firmware version decodes");
Check(hp208Settings.Single(s => s.Id == "listeningMode").CurrentText == "0", "Captured HP208 listening mode decodes");
Check(hp208Settings.Single(s => s.Id == "ancButtonDoublePress").Editable == false, "Out-of-profile button assignment stays read only");
Check(hp208Settings.All(s => s.Id != "noiseCancellationDepth"), "Absent HP208 ANC depth does not become a fabricated control");
var emptyEq = hp208Settings.Single(s => s.Id == "customEqualizer");
Check(emptyEq.Type == "equalizer" && emptyEq.Value.ValueKind == JsonValueKind.Null, "Empty HP208 curve exposes an editable draft, not fabricated live gains");
Check(emptyEq.Parameters.GetProperty("bandHz").EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { 62, 250, 500, 1000, 2000, 4000, 8000, 16000 }), "Max5s new curve uses the official app's eight fallback frequencies");
var eqPacket = UgreenBackend.EncodeSetting(emptyEq, "-6,0,0,0,0,0,0,6");
Check(eqPacket.Command == 21 && Convert.ToHexString(eqPacket.Payload) == "0118003E0000FA0601F40603E80607D0060FA0061F40063E800C", "Custom EQ packet uses UG3 command 21, big-endian frequencies and gain plus six");
await Reject(() => Task.FromResult(UgreenBackend.EncodeSetting(emptyEq, "-7,0,0,0,0,0,0,0")), "Custom EQ rejects gains outside the native range");
await Reject(() => Task.FromResult(UgreenBackend.EncodeSetting(emptyEq, "0,0,0")), "Custom EQ rejects an incomplete curve");
var eqFields = hp208Fields.ToDictionary(p => p.Key, p => p.Value);
eqFields[27] = Convert.FromHexString("003E0000FA0601F40603E80607D0060FA0061F40063E800C");
eqFields[4] = [160];
var savedEqSettings = UgreenBackend.ParseSettings(eqFields);
Check(savedEqSettings.Single(s => s.Id == "customEqualizer").Value.EnumerateArray().Select(v => v.GetInt32()).SequenceEqual(new[] { -6, 0, 0, 0, 0, 0, 0, 6 }), "Saved custom EQ gains decode including both range boundaries");
Check(savedEqSettings.Single(s => s.Id == "equalizerPreset").CurrentText == "160" && savedEqSettings.Single(s => s.Id == "equalizerPreset").Options.Contains("160"), "Saved custom mode stays selectable alongside factory presets");
eqFields[27] = [0, 62, 13];
Check(UgreenBackend.ParseSettings(eqFields).All(s => s.Id != "customEqualizer"), "Malformed or out-of-range EQ data never becomes editable");
eqFields.Remove(27);
Check(UgreenBackend.ParseSettings(eqFields).All(s => s.Id != "customEqualizer"), "An absent custom-EQ capability does not produce a control");
var modePacket = UgreenBackend.EncodeSetting(hp208Settings.Single(s => s.Id == "listeningMode"), "2");
Check(modePacket.Command == 6 && Convert.ToHexString(modePacket.Payload) == "010102", "UG3 transparency command agrees with official HP208 mapping");
await Reject(() => Task.FromResult(UgreenBackend.EncodeSetting(hp208Settings.Single(s => s.Id == "listeningMode"), "99")), "Unrecognized Max5s mode cannot be sent");
try { Ug3Protocol.ParseTlv(new byte[] { 1, 2, 70 }); throw new Exception("Expected truncated TLV rejection"); }
catch (IOException) { Check(true, "Truncated Max5s fields are rejected"); }
try { Ug3Protocol.ParseTlv(new byte[] { 1, 0, 1, 0 }); throw new Exception("Expected duplicate TLV rejection"); }
catch (IOException) { Check(true, "Duplicate handshake fields stay rejected"); }
Check(Ug3Protocol.ParseStatusTlv(Convert.FromHexString("0401060401061B001B00"))[4].SequenceEqual(new byte[] { 6 }), "Identical status repeats and empty optional fields are accepted");
try { Ug3Protocol.ParseStatusTlv(Convert.FromHexString("040100040106")); throw new Exception("Expected conflicting status rejection"); }
catch (Ug3ConflictingStatusException ex) { Check(ex.Tag == 4, "Conflicting status values are detected rather than arbitrarily chosen"); }
try { Ug3Protocol.ParseStatusTlv(Convert.FromHexString("0401060402")); throw new Exception("Expected truncated repeated field rejection"); }
catch (IOException) { Check(true, "A repeated field does not hide truncated status data"); }

byte[] StatusFixture(byte[] request) => Ug3Protocol.ParseTlv(request).Keys.Where(hp208Fields.ContainsKey).SelectMany(tag => Ug3Protocol.Tlv(tag, hp208Fields[tag])).ToArray();
var repeatCalls = 0;
var repeatedFields = await UgreenBackend.ReadStatusFieldsAsync((request, _) =>
{
    repeatCalls++;
    var response = StatusFixture(request);
    if (Ug3Protocol.ParseTlv(request).ContainsKey(4)) response = response.Concat(Ug3Protocol.Tlv(4, 0)).ToArray();
    return Task.FromResult(response);
}, CancellationToken.None);
Check(repeatCalls == 4 && UgreenBackend.ParseSettings(repeatedFields).Single(s => s.Id == "equalizerPreset").CurrentText == "0", "Normal repeated status fields complete the original read without a retry");

var retryRequests = new List<byte[]>();
var resolvedFields = await UgreenBackend.ReadStatusFieldsAsync((request, _) =>
{
    retryRequests.Add(request);
    if (retryRequests.Count > 4) return Task.FromResult(Ug3Protocol.Tlv(4, 6));
    var response = StatusFixture(request);
    if (Ug3Protocol.ParseTlv(request).ContainsKey(4)) response = response.Concat(Ug3Protocol.Tlv(4, 6)).ToArray();
    return Task.FromResult(response);
}, CancellationToken.None);
Check(retryRequests.Count == 5 && retryRequests[^1].SequenceEqual(new byte[] { 4, 1, 0 }) && resolvedFields[4].SequenceEqual(new byte[] { 6 }), "A conflicting preset triggers exactly one targeted read and uses its fresh value");

var nestedFields = hp208Fields.ToDictionary(p => p.Key, p => p.Value);
nestedFields[1] = nestedFields[1].Concat(Ug3Protocol.Tlv(1, 70)).ToArray();
nestedFields[5] = nestedFields[5].Concat(Ug3Protocol.Tlv(3, 9)).ToArray();
nestedFields[7] = nestedFields[7].Concat(Ug3Protocol.Tlv(1, 1)).ToArray();
Check(UgreenBackend.ParseSettings(nestedFields).Single(s => s.Id == "batteryLevel").CurrentText == "70%", "Identical nested battery, button and prompt fields parse successfully");
var nestedRetryRequests = new List<byte[]>();
var resolvedNested = await UgreenBackend.ReadStatusFieldsAsync((request, _) =>
{
    nestedRetryRequests.Add(request);
    if (nestedRetryRequests.Count > 4) return Task.FromResult(Ug3Protocol.Tlv(1, Ug3Protocol.Tlv(1, 69)));
    var values = Ug3Protocol.ParseTlv(request).Keys.Where(hp208Fields.ContainsKey).SelectMany(tag =>
        Ug3Protocol.Tlv(tag, tag == 1 ? hp208Fields[tag].Concat(Ug3Protocol.Tlv(1, 69)).ToArray() : hp208Fields[tag])).ToArray();
    return Task.FromResult(values);
}, CancellationToken.None);
Check(nestedRetryRequests.Count == 5 && nestedRetryRequests[^1].SequenceEqual(new byte[] { 1, 1, 0 }) && UgreenBackend.ParseSettings(resolvedNested).Single(s => s.Id == "batteryLevel").CurrentText == "69%", "Conflicting nested fields re-query their parent status once");

var unresolvedCalls = 0;
try
{
    await UgreenBackend.ReadStatusFieldsAsync((request, _) =>
    {
        unresolvedCalls++;
        return Task.FromResult(Ug3Protocol.ParseTlv(request).ContainsKey(4) ? Convert.FromHexString("040100040106") : StatusFixture(request));
    }, CancellationToken.None);
    throw new Exception("Expected unresolved status conflict");
}
catch (Ug3ConflictingStatusException) { Check(unresolvedCalls == 5, "Persistent conflicts stop after one read retry without a retry loop"); }

const string schema = """[{"categoryId":"soundModes","settings":[{"settingId":"ambientSoundMode","type":"select","setting":{"options":["Normal","Transparency"]}},{"settingId":"batteryLevelLeft","type":"information"}]}]""";
const string values = """[{"settingId":"ambientSoundMode","value":{"type":"string","value":"Normal"}},{"settingId":"batteryLevelLeft","value":{"type":"string","value":"89/100"}}]""";
var parsed = SettingsParser.Parse(schema, values);
Check(parsed[0].CurrentText == "Normal" && parsed[1].CurrentText == "89/100", "Parse actual tagged OpenSCQ30 values");
Check(SettingsParser.Parse(schema, """[{"settingId":"ambientSoundMode","value":{"type":"string","value":"Normal"}},{"settingId":"ambientSoundMode","value":{"type":"string","value":"Normal"}}]""")[0].CurrentText == "Normal", "Repeated settings across categories do not break parsing");
await Reject(() => Task.FromResult(parsed[0].Validate("unsupported")), "Reject unsupported choices");
await Reject(() => Task.FromResult(parsed[1].Validate("0")), "Information is never writable");

var eq = new DeviceSetting("volumeAdjustments", "equalizer", "equalizer", Json("""{"bandHz":[100,200],"fractionDigits":1,"min":-120,"max":134}"""), Json("[0,0]"));
Check(eq.Validate("-120,134") == "-120,134", "EQ boundary values accepted");
await Reject(() => Task.FromResult(eq.Validate("-121,0")), "EQ lower bound enforced");
await Reject(() => Task.FromResult(eq.Validate("0,135")), "EQ upper bound enforced");
await Reject(() => Task.FromResult(eq.Validate("0")), "EQ band count enforced");
var range = new DeviceSetting("limitHighVolumeDbLimit", "sound", "i32Range", Json("""{"start":75,"end":100,"step":5}"""), Json("80"));
Check(range.Validate("85") == "85", "Read real flattened range schema");
await Reject(() => Task.FromResult(range.Validate("86")), "Range step enforced");
var profile = new DeviceSetting("customEqualizerProfile", "equalizer", "modifiableSelect", Json("""{"options":["-Studio","+Bass"]}"""), Json("null"));
Check(profile.Validate("\\-Studio") == "\\-Studio", "Escape profile names beginning with operation characters");
await Reject(() => Task.FromResult(profile.Validate("+")), "Reject empty profile name");

var fake = new FakeRunner(schema, values);
var backend = new SoundcoreBackend(fake);
var provisional = new Headphone("Soundcore P3", 1, "test", false, DeviceCatalog.LifeP3);
await Reject(() => backend.RegisterAsync(provisional, false), "Provisional P3 cannot be registered without model confirmation");
Check(fake.Calls.Count == 0, "P3 guard sends no command");
await Reject(() => backend.RegisterAsync(provisional with { Profile = DeviceCatalog.FitBuds }, true), "Ugreen cannot use a Soundcore driver");
Check(fake.Calls.Count == 0, "Ugreen guard sends no command");
await backend.ReadAsync("00:00:00:00:00:01");
Check(fake.Calls.All(a => !a.Contains("--set")), "Refresh is entirely read-only");
await backend.SetAsync("00:00:00:00:00:01", parsed[0], "Transparency");
Check(fake.Calls.Last().Contains("ambientSoundMode=Transparency"), "Validated setting becomes one argument");
fake.Schema = """[{"categoryId":"soundModes","settings":[{"settingId":"batteryLevelLeft","type":"information"}]}]""";
var writes = fake.Calls.Count(a => a.Contains("--set"));
await Reject(() => backend.SetAsync("00:00:00:00:00:01", parsed[0], "Normal"), "Stale unavailable controls are rejected");
Check(writes == fake.Calls.Count(a => a.Contains("--set")), "Stale control does not send a write");
fake.Schema = schema;
await Task.WhenAll(backend.ReadAsync("00:00:00:00:00:01"), backend.ReadAsync("00:00:00:00:00:01"));
Check(fake.MaximumConcurrent == 1, "Bluetooth command sessions are serialized");

var root = new DirectoryInfo(Environment.CurrentDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "global.json"))) root = root.Parent;
if (root is null) throw new Exception("Run tests inside the SoundCity checkout.");
var artifacts = Path.Combine(root.FullName, "artifacts");
Directory.CreateDirectory(artifacts);
var profileDirectory = Path.Combine(artifacts, "eq-tests-" + Guid.NewGuid().ToString("N"));
var profileStore = new EqualizerProfiles(profileDirectory);
profileStore.Save("max5s", new("My curve", [62, 1000, 16000], [-6, 0, 6]));
profileStore.Save("max5s", new("My curve", [62, 1000, 16000], [1, 0, -1]));
Check(profileStore.Read("max5s").Count == 1 && profileStore.Read("max5s")[0].GainsDb[0] == 1, "Named profiles round-trip and replace by name");
Check(profileStore.Read("lifep3").Count == 0, "Saved EQ profiles stay scoped to a model");
await Reject(() => { profileStore.Save("../escape", new("My curve", [62, 1000], [0, 0])); return Task.CompletedTask; }, "Profile storage rejects paths as model names");
await Reject(() => { profileStore.Save("max5s", new("Bad", [62, 1000], [0, double.NaN])); return Task.CompletedTask; }, "Profile storage rejects non-finite gains");
var endpointGuid = Guid.Parse("e4ad80a2-4201-477e-b98d-567812349abc");
Check(EqualizerApo.EndpointGuid("{0.0.0.00000000}.{" + endpointGuid + "}") == endpointGuid, "APO boost scopes to the exact Windows playback GUID");
await Reject(() => Task.FromResult(EqualizerApo.EndpointGuid("all\nPreamp: 99 dB")), "APO rejects device-pattern injection");
var cultureBefore = System.Globalization.CultureInfo.CurrentCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
var boostConfiguration = EqualizerApo.BuildConfiguration([new(endpointGuid, 3.5, true)]);
System.Globalization.CultureInfo.CurrentCulture = cultureBefore;
Check(boostConfiguration.Contains("Preamp: 3.5 dB") && boostConfiguration.Contains("Device: {" + endpointGuid + "}"), "Boost uses invariant gain values and an exact output selector");
Check(!EqualizerApo.BuildConfiguration([new(endpointGuid, 3.5, false)]).Contains("Preamp:"), "Disabled boost adds no audio gain");
await Reject(() => Task.FromResult(EqualizerApo.BuildConfiguration([new(endpointGuid, 12.5, true)])), "Boost gain limit is validated before writing configuration");
var existingConfig = "Preamp: -2 dB\r\nInclude: my-filters.txt\r\nDevice: Microphone\r\n";
var linkedConfig = EqualizerApo.ConnectConfiguration(existingConfig);
Check(linkedConfig.StartsWith(existingConfig) && EqualizerApo.ConnectConfiguration(linkedConfig) == linkedConfig && linkedConfig.Contains("Device: all\r\nChannel: all\r\nStage: post-mix"), "APO setup preserves existing filters and includes boost once in the output stage");
var apoTestDir = Path.Combine(profileDirectory, "apo");
Directory.CreateDirectory(apoTestDir); File.WriteAllText(Path.Combine(apoTestDir, "config.txt"), existingConfig);
var apo = new EqualizerApo(apoTestDir);
apo.Save(new(endpointGuid, 3.5, true));
Check(apo.Read().Single() == new VolumeBoostSetting(endpointGuid, 3.5, true) && Directory.GetFiles(apoTestDir, "config.before-soundcity-*.txt").Length == 1, "APO configuration round-trips with the original configuration backed up");
apo.Save(new(endpointGuid, 0, false));
Check(!File.ReadAllText(Path.Combine(apoTestDir, "SoundCity", "boost.txt")).Contains("Preamp:"), "Turning boost off removes its gain without touching other filters");
var executable = Path.Combine(root.FullName, "tools", "openscq30", "openscq30.exe");
if (args.Contains("--engine"))
{
    var demoRunner = new OpenScq30Runner(executable, Path.Combine(artifacts, "engine-tests.sqlite"));
    await demoRunner.RunAsync(["paired-devices", "add", "--mac-address", "00:00:00:00:00:01", "--model", "SoundcoreA3954", "--demo"]);
    var demoBackend = new SoundcoreBackend(demoRunner);
    var all = await demoBackend.ReadAsync("00:00:00:00:00:01");
    Check(all.Count > 25 && all.Any(s => s.Type == "equalizer"), "Pinned engine returns full Liberty 4 Pro controls");
    Check(all.Single(s => s.Id == "ambientSoundMode").Options.Contains("Transparency"), "Real engine choices parse correctly");
    Check(all.Single(s => s.Id == "batteryLevelLeft").CurrentText.Contains('/'), "Real engine battery value parses");
    await demoBackend.SetAsync("00:00:00:00:00:01", all.Single(s => s.Id == "volumeAdjustments"), "0,0,0,0,0,0,0,0");
    Check(true, "EQ write accepted by simulated engine");
    var readback = await demoRunner.RunAsync(["device", "--mac-address", "00:00:00:00:00:01", "setting", "--set", "ambientSoundMode=Normal", "--get", "ambientSoundMode", "--json"]);
    Check(readback.Contains("Normal"), "Simulated mode change read back in same session");
    await demoRunner.RunAsync(["paired-devices", "add", "--mac-address", "00:00:00:00:00:02", "--model", "SoundcoreA3939", "--demo"]);
    var p3 = await demoBackend.ReadAsync("00:00:00:00:00:02");
    Check(p3.Any(s => s.Id == "ambientSoundMode") && p3.Any(s => s.Type == "equalizer"), "Pinned engine returns Life P3 controls");
    await demoBackend.SetAsync("00:00:00:00:00:02", p3.First(s => s.Id == "ambientSoundMode"), "Transparency");
    Check(true, "P3 mode write accepted by simulated engine");
}
if (args.Contains("--live-read"))
{
    var found = await new BluetoothDiscovery().FindAsync();
    Console.WriteLine("Discovered: " + string.Join(", ", found.Select(d => $"{d.Name} ({(d.Connected ? "connected" : "offline")})")));
    var liberty = found.FirstOrDefault(d => d.Profile == DeviceCatalog.Liberty4Pro && d.Connected);
    if (liberty is null) Console.WriteLine("SKIP: No connected Liberty 4 Pro. No live commands sent.");
    else
    {
        var liveBackend = new SoundcoreBackend(new OpenScq30Runner(executable, AppPaths.Database));
        await liveBackend.RegisterAsync(liberty, false);
        var current = await liveBackend.ReadAsync(liberty.Address);
        var safe = current.Where(s => !s.Id.Contains("serial", StringComparison.OrdinalIgnoreCase) && !s.Id.Contains("Devices", StringComparison.OrdinalIgnoreCase)).Select(s => new { s.Id, s.Category, s.Type, value = s.CurrentText });
        await File.WriteAllTextAsync(Path.Combine(artifacts, "liberty-readonly.json"), JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true }));
        Check(current.Any(s => s.Id == "batteryLevelLeft"), "Live Liberty 4 Pro read-only status");
        Console.WriteLine("Live controls: " + current.Count);
    }
}

if (args.Contains("--audio-read"))
{
    var outputs = WindowsAudio.ReadOutputs();
    Check(outputs.Count > 0 && outputs.All(o => o.Volume >= 0 && o.Volume <= 100), "Live Windows output volumes are readable");
    foreach (var output in outputs) Console.WriteLine($"AUDIO: {output.Name} | {output.Volume}% | muted={output.Muted} | default={output.IsDefault}");
}

if (args.Contains("--ugreen-sdp"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var found = await new BluetoothDiscovery().FindAsync(deadline.Token);
    var max5s = found.Single(d => d.Profile == DeviceCatalog.Max5s && d.Connected);
    using var device = await Windows.Devices.Bluetooth.BluetoothDevice.FromBluetoothAddressAsync(max5s.BluetoothAddress).AsTask(deadline.Token);
    var result = await device.GetRfcommServicesForIdAsync(Windows.Devices.Bluetooth.Rfcomm.RfcommServiceId.SerialPort).AsTask(deadline.Token);
    foreach (var service in result.Services)
    {
        using (service)
        {
            var attributes = await service.GetSdpRawAttributesAsync().AsTask(deadline.Token);
            Console.WriteLine($"SPP protection: {service.ProtectionLevel}, maximum: {service.MaxProtectionLevel}");
            foreach (var attribute in attributes.Where(p => p.Key is 4 or 256))
            {
                using var reader = Windows.Storage.Streams.DataReader.FromBuffer(attribute.Value);
                var bytes = new byte[attribute.Value.Length]; reader.ReadBytes(bytes);
                Console.WriteLine($"SDP {attribute.Key}: {Convert.ToHexString(bytes)}");
            }
        }
    }
}

if (args.Contains("--ugreen-probe"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(12));
    var found = await new BluetoothDiscovery().FindAsync(deadline.Token);
    var max5s = found.Single(d => d.Profile == DeviceCatalog.Max5s && d.Connected);
    using var transport = await UgreenTransport.OpenAsync(max5s, deadline.Token);
    var status = await transport.QueryStatusRawAsync(deadline.Token);
    await File.WriteAllTextAsync(Path.Combine(artifacts, "max5s-status.hex"), Convert.ToHexString(status));
    Console.WriteLine("READ-ONLY STATUS FRAME: " + Convert.ToHexString(status));
    byte[] tags = [5, 6, 7, 14, 15, 17, 19, 23, 27, 29, 31, 32];
    foreach (var group in tags.Chunk(4))
    {
        var payload = await transport.RequestAsync(1, group.SelectMany(t => Ug3Protocol.Tlv(t, 0)).ToArray(), deadline.Token);
        Console.WriteLine("INFO FIELDS: " + Convert.ToHexString(payload));
    }
}

if (args.Contains("--ugreen-read"))
{
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    var found = await new BluetoothDiscovery().FindAsync(deadline.Token);
    var max5s = found.Single(d => d.Profile == DeviceCatalog.Max5s && d.Connected);
    var liveSettings = await new UgreenBackend().ReadAsync(max5s, deadline.Token);
    Check(liveSettings.Any(s => s.Id == "batteryLevel") && liveSettings.Any(s => s.Id == "listeningMode") && liveSettings.Any(s => s.Id == "equalizerPreset"), "Live HP208 encrypted session returns battery, noise mode and EQ");
    foreach (var setting in liveSettings) Console.WriteLine($"MAX5S: {setting.Label} = {setting.CurrentText} ({setting.Type})");
}

Console.WriteLine($"All {checks} checks passed.");

sealed class FakeRunner(string schema, string values) : ICommandRunner
{
    public string Schema { get; set; } = schema;
    public List<string[]> Calls { get; } = [];
    private int active;
    public int MaximumConcurrent { get; private set; }
    public async Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        MaximumConcurrent = Math.Max(MaximumConcurrent, Interlocked.Increment(ref active));
        try
        {
            Calls.Add(arguments.ToArray());
            await Task.Delay(2, cancellationToken);
            return arguments.Contains("list-settings") ? Schema : arguments.Contains("--get") ? values : "OK";
        }
        finally { Interlocked.Decrement(ref active); }
    }
}

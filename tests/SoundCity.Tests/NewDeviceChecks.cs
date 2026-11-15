using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SoundCity.Core;

internal static class NewDeviceChecks
{
    internal static async Task RunAsync(Action<bool, string> check)
    {
        async Task Throws<T>(Func<Task> action, string label) where T : Exception
        {
            try { await action(); } catch (T) { check(true, label); return; }
            throw new Exception("FAIL: " + label);
        }
        check(DeviceCatalog.Match(" Parrot Zik 3.0 ") == DeviceCatalog.Zik3 && DeviceCatalog.Zik3.Backend == HeadphoneBackend.Parrot, "Zik 3 names route only to Parrot");
        check(DeviceCatalog.Match("Beats Solo Buds") == DeviceCatalog.SoloBuds && !DeviceCatalog.SoloBuds.HasControls, "Solo Buds exposes Windows audio without vendor commands");
        check(DeviceCatalog.Match("AirPods Pro 3") == DeviceCatalog.AirPodsPro3 && !DeviceCatalog.AirPodsPro3.HasControls, "AirPods Pro 3 exposes Windows audio without substituting a protocol");
        foreach (var name in new[] { "Parrot Zik", "Parrot Zik 2.0", "AirPods Pro", "AirPods Pro 2", "AirPods 3", "Listener's AirPods Pro", "Beats Studio Buds", "Beats Solo 3" })
            check(DeviceCatalog.Match(name) is null, "No model inference for " + name);
        var folder = Path.Combine(Path.GetTempPath(), "SoundCity-model-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var maps = new DeviceProfileMappings(Path.Combine(folder, "models.json"));
            maps.Save(0x123456789ABC, DeviceCatalog.AirPodsPro3);
            check(DeviceProfileMappings.Resolve("Listener's AirPods Pro", 0x123456789ABC, maps.Read()) == DeviceCatalog.AirPodsPro3, "Explicit model selection survives storage for renamed headphones");
            check(DeviceProfileMappings.Resolve("Listener's AirPods Pro", 0x123456789ABD, maps.Read()) is null, "A model selection cannot leak to another device");
            check(DeviceProfileMappings.Resolve("UGREEN Studio Max5s", 0x123456789ABC, maps.Read()) == DeviceCatalog.Max5s, "Exact known model takes precedence over stale manual selection");
            maps.Save(0x123456789ABC, null);
            check(maps.Read().Count == 0, "User can forget a model selection");
            var airpods = new Headphone("Listener's AirPods Pro", 1, "test", true, DeviceCatalog.AirPodsPro3);
            check(WindowsAudio.Matches("Headphones (Listener's AirPods Pro)", airpods) && !WindowsAudio.Matches("Headphones (Other AirPods Pro)", airpods), "Renamed headphones retain exact Windows output isolation");

            var endpoint = Guid.NewGuid();
            var gains = new[] { 0, 0, 35, 0, -15, 0, 0, 0, 0, 0 };
            var eq = new VolumeBoostSetting(endpoint, 0, false, gains, true);
            var configuration = EqualizerApo.BuildConfiguration([eq]);
            check(configuration.Contains($"Device: {endpoint:B}") && configuration.Contains("Preamp: -3.5 dB") && configuration.Contains("125 3.5; 250 0.0; 500 -1.5"), "Windows EQ targets one endpoint and reserves positive-band headroom");
            check(EqualizerApo.BuildConfiguration([eq with { GainDb = 2, Enabled = true }]).Contains("Preamp: -1.5 dB"), "Explicit boost combines with EQ headroom once");
            check(!EqualizerApo.BuildConfiguration([eq with { EqEnabled = false }]).Contains("GraphicEQ:"), "Disabling Windows EQ removes processing while retaining the saved curve");
            check(!EqualizerApo.BuildConfiguration([new(endpoint, 2, true)]).Contains("EqGains"), "Existing boost-only state stays backward compatible");
            await Throws<ArgumentException>(() => Task.FromResult(EqualizerApo.BuildConfiguration([eq with { EqGains = [0, 0] }])), "Windows EQ rejects incomplete curves");
            await Throws<ArgumentException>(() => Task.FromResult(EqualizerApo.BuildConfiguration([eq with { EqGains = gains.Select(_ => 121).ToArray() }])), "Windows EQ rejects gains outside its range");
            await Throws<ArgumentException>(() => Task.FromResult(EqualizerApo.BuildConfiguration([eq with { EqGains = null }])), "Enabled EQ requires an actual curve");
            File.WriteAllText(Path.Combine(folder, "config.txt"), "# Existing user configuration\nPreamp: -1 dB\n");
            var apo = new EqualizerApo(folder);
            apo.Save(new(endpoint, 2, true));
            apo.Save(apo.Read().Single() with { EqGains = gains, EqEnabled = true });
            check(apo.Read().Single().GainDb == 2 && apo.Read().Single().Enabled, "Saving EQ preserves configured volume boost");
            apo.Save(apo.Read().Single() with { GainDb = 3 });
            check(apo.Read().Single().EqGains!.SequenceEqual(gains) && apo.Read().Single().EqEnabled, "Saving boost preserves configured EQ");
            check(File.ReadAllText(Path.Combine(folder, "config.txt")).Contains("Preamp: -1 dB"), "Windows processing preserves existing user configuration");
        }
        finally { Directory.Delete(folder, true); }

        var request = ParrotProtocol.Request("/api/system/battery");
        check(BinaryPrimitives.ReadUInt16BigEndian(request) == request.Length && request[2] == 0x80 && Encoding.ASCII.GetString(request, 3, request.Length - 3) == "GET /api/system/battery/get", "Parrot framing uses total byte length and documented GET syntax");
        var frame = Frame("<answer path='/api/system/battery/get'><system><battery percent='73' state='in_use'/></system></answer>");
        check(ParrotProtocol.ParseResponse(frame, "/api/system/battery").Element("system")?.Element("battery")?.Attribute("percent")?.Value == "73", "Parse Parrot XML after the seven-byte header");
        var notification = Frame("<notify path='/api/system/battery/get'/>");
        using (var stream = new FragmentedStream(notification.Concat(frame).ToArray()))
        {
            check((await ParrotProtocol.ReadFrameAsync(stream, default)).SequenceEqual(notification) && (await ParrotProtocol.ReadFrameAsync(stream, default)).SequenceEqual(frame), "Parrot reads preserve boundaries across fragmented and coalesced frames");
        }
        using (var stream = new FragmentedStream(frame[..^1]))
            await Throws<EndOfStreamException>(() => ParrotProtocol.ReadFrameAsync(stream, default), "Truncated Parrot frames cannot fabricate a response");
        using (var stream = new FragmentedStream([0, 2, 0x80]))
            await Throws<IOException>(() => ParrotProtocol.ReadFrameAsync(stream, default), "Parrot rejects invalid frame lengths");
        await Throws<IOException>(() => Task.FromResult(ParrotProtocol.ParseResponse(frame, "/api/audio/noise_control")), "Parrot rejects answers for a different request");
        await Throws<ParrotRequestRejectedException>(() => Task.FromResult(ParrotProtocol.ParseResponse(Frame("<answer path='/api/system/battery/get' error='true'/>"), "/api/system/battery")), "Parrot errors cannot be treated as successful reads");
        await Throws<XmlException>(() => Task.FromResult(ParrotProtocol.ParseResponse(Frame("<!DOCTYPE answer [<!ENTITY x SYSTEM 'file:///unused'>]><answer path='/api/system/battery/get'>&x;</answer>"), "/api/system/battery")), "Parrot XML prohibits DTDs and external entities");
        await Throws<ArgumentException>(() => Task.FromResult(ParrotProtocol.Request("/api/system/battery\nSET")), "Parrot paths reject command injection");

        var responses = Fixture();
        var parsed = ParrotBackend.ParseSettings(responses);
        check(parsed.Single(s => s.Id == "batteryLevel").CurrentText == "73%" && parsed.Single(s => s.Id == "noiseControlMode").CurrentText == "anc2", "Parrot settings reflect simulated reported battery and mode");
        var noise = parsed.Single(s => s.Id == "noiseControlMode");
        var encoded = ParrotBackend.EncodeSetting(noise, "aoc1");
        check(encoded.Path == "/api/audio/noise_control" && encoded.Argument == "aoc&value=1", "Street mode encodes the documented Zik 3 value");
        check(Encoding.ASCII.GetString(ParrotProtocol.Request(encoded.Path, encoded.Argument)[3..]) == "SET /api/audio/noise_control/set?arg=aoc&value=1", "Parrot SET keeps separate protocol arguments intact");
        await Throws<ArgumentException>(() => Task.FromResult(ParrotBackend.EncodeSetting(noise, "adaptive")), "Parrot rejects unsupported noise modes");
        check(ParrotBackend.ParseSettings(new Dictionary<string, XElement>()).Count == 0, "Absent Parrot values stay absent");
        responses["/api/audio/noise_control"].Element("audio")!.Element("noise_control")!.SetAttributeValue("type", "new-mode");
        check(!ParrotBackend.ParseSettings(responses).Single(s => s.Id == "noiseControlMode").Editable, "Unrecognized Parrot mode is read only");

        responses = Fixture();
        var writes = 0;
        Task<XElement> Simulate(string path, string? value, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (value is not null)
            {
                writes++;
                if (path == "/api/system/head_detection/enabled") responses[path].Element("system")!.Element("head_detection")!.SetAttributeValue("enabled", value);
            }
            return Task.FromResult(new XElement(responses[path]));
        }
        var head = parsed.Single(s => s.Id == "headDetection");
        await ParrotBackend.ApplyAsync(Simulate, head, "false", default);
        check(writes == 1, "Parrot change verifies readback with exactly one write");
        await Throws<IOException>(() => ParrotBackend.ApplyAsync(Simulate, noise, "off", default), "Parrot refuses success when readback disagrees");
        check(writes == 2, "Parrot does not replay an unconfirmed setting");
        responses.Remove("/api/system/head_detection/enabled");
        Task<XElement> Unsupported(string path, string? value, CancellationToken token)
        {
            if (!responses.ContainsKey(path)) throw new ParrotRequestRejectedException(path);
            return Simulate(path, value, token);
        }
        var partial = await ParrotBackend.ReadSettingsAsync(Unsupported, default);
        check(partial.All(s => s.Id != "headDetection") && partial.Any(s => s.Id == "unavailableSettings"), "Rejected optional Parrot reads retain other controls and explain omissions");
        await Throws<InvalidOperationException>(() => ParrotBackend.ApplyAsync(Unsupported, head, "true", default), "Fresh capability reads block stale Parrot writes");
        check(writes == 2, "Stale control rejection never sends a setting");
    }

    private static byte[] Frame(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        var result = new byte[7 + bytes.Length];
        BinaryPrimitives.WriteUInt16BigEndian(result, (ushort)result.Length);
        result[2] = 0x80; result[3] = 1; result[4] = 1;
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(5), (ushort)bytes.Length);
        bytes.CopyTo(result, 7);
        return result;
    }

    private static Dictionary<string, XElement> Fixture()
    {
        var values = new[] {
            "<system><battery percent='73' state='in_use'/></system>", "<software sip6='3.07'/>",
            "<audio><noise_control type='anc' value='2'/></audio>", "<audio><noise_control enabled='true'/></audio>",
            "<audio><sound_effect enabled='false' room_size='jazz' angle='60'/></audio>", "<audio><equalizer enabled='true'/></audio>",
            "<system><head_detection enabled='true'/></system>", "<system><auto_connection enabled='false'/></system>"
        };
        return ParrotBackend.ReadPaths.Select((p, i) => (p, xml: XElement.Parse($"<answer path='{p}/get'>{values[i]}</answer>"))).ToDictionary(x => x.p, x => x.xml);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
    }
}

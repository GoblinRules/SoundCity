using SoundCity.Core;

internal static class AudioSetupChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var fitbuds = new Headphone("UGREEN FitBuds", 1, "test", true, DeviceCatalog.FitBuds);
        var max5s = new Headphone("UGREEN Studio Max5s", 2, "test", true, DeviceCatalog.Max5s);
        var handsFree = new AudioOutput("hands-free", "Headset (UGREEN FitBuds Hands-Free AG Audio)", 44, false, false);
        var stereo = new AudioOutput("stereo", "Headphones (UGREEN FitBuds Stereo)", 82, false, false);
        var speakers = new AudioOutput("speakers", "Speakers (Realtek Audio)", 50, false, true);
        check(WindowsAudio.ForHeadphone([handsFree, speakers, stereo], fitbuds).SequenceEqual([stereo, handsFree]), "Headphone output selection prefers stereo and excludes speakers");
        check(WindowsAudio.ForHeadphone([handsFree with { IsDefault = true }, stereo], fitbuds)[0].Id == handsFree.Id, "Volume selection follows the actual default headphone output");
        check(WindowsAudio.ForHeadphone([handsFree with { IsDefault = true }, stereo], fitbuds, o => o.Id == stereo.Id)[0] == stereo, "Processing selection prefers the configured stereo output over unconfigured hands-free");
        check(WindowsAudio.ForHeadphone([speakers, stereo], max5s).Length == 0, "Output selection never substitutes another model or speakers");
        var defaultMax5s = new AudioOutput("max5s", "Headphones (UGREEN Studio Max5s Stereo)", 90, false, true);
        check(WindowsAudio.DefaultHeadphone([fitbuds, max5s], [stereo, defaultMax5s]) == max5s, "Startup follows the headphone used as the Windows default");
        check(WindowsAudio.DefaultHeadphone([fitbuds, max5s], [speakers, stereo]) is null, "Default speaker does not imply a selected headphone");
        check(WindowsAudio.DefaultHeadphone([max5s with { Connected = false }], [defaultMax5s]) is null, "Startup does not treat a disconnected headphone as playing");

        const string effect = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d},6";
        const string disable = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";
        const string postMix = "{ec1cc9ce-faed-4822-828a-82a81a6f018f}";
        var properties = new Dictionary<string, object?>();
        check(EqualizerApo.ParseEndpointState(properties) == ApoEndpointState.NotInstalled, "An endpoint without APO effects requires setup");
        properties[effect] = "{eacd2258-fcac-4ff4-b36d-419e924a6d79}";
        check(EqualizerApo.ParseEndpointState(properties) == ApoEndpointState.NotInstalled, "A pre-mix effect alone cannot enable post-mix EQ controls");
        properties[effect] = postMix.ToUpperInvariant();
        check(EqualizerApo.ParseEndpointState(properties) == ApoEndpointState.Configured, "Post-mix APO registration is recognized case-insensitively");
        properties[effect] = new[] { "{11111111-1111-1111-1111-111111111111}", postMix };
        properties[disable] = 0;
        check(EqualizerApo.ParseEndpointState(properties) == ApoEndpointState.Configured, "Multi-effect registration with enhancements enabled is recognized");
        properties[disable] = 1;
        check(EqualizerApo.ParseEndpointState(properties) == ApoEndpointState.EnhancementsDisabled, "Disabled Windows enhancements block EQ and boost");
        properties[disable] = "unknown";
        check(EqualizerApo.ParseEndpointState(properties) == ApoEndpointState.Unknown, "Unrecognized enhancement state does not report working setup");
    }
}

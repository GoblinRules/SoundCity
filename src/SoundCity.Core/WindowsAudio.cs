using System.Runtime.InteropServices;

namespace SoundCity.Core;

public sealed record AudioOutput(string Id, string Name, double Volume, bool Muted, bool IsDefault);

/// <summary>Windows Core Audio endpoint controls. Never falls back to another output.</summary>
public static class WindowsAudio
{
    public static AudioOutput[] ForHeadphone(IEnumerable<AudioOutput> outputs, Headphone headphone, Func<AudioOutput, bool>? processingConfigured = null)
        => outputs.Where(o => Matches(o.Name, headphone))
            .OrderByDescending(o => processingConfigured?.Invoke(o) ?? false)
            .ThenByDescending(o => o.IsDefault)
            .ThenBy(o => o.Name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase))
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToArray();

    public static Headphone? DefaultHeadphone(IEnumerable<Headphone> headphones, IEnumerable<AudioOutput> outputs)
        => headphones.FirstOrDefault(d => d.Connected && outputs.Any(o => o.IsDefault && Matches(o.Name, d)));

    public static IReadOnlyList<AudioOutput> ReadOutputs()
    {
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDeviceCollection? collection = null;
        IMMDevice? defaultDevice = null;
        try
        {
            string? defaultId = null;
            if (enumerator.GetDefaultAudioEndpoint(0, 1, out defaultDevice) >= 0)
                Marshal.ThrowExceptionForHR(defaultDevice.GetId(out defaultId));
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out var count));
            var outputs = new List<AudioOutput>();
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(i, out var device));
                IPropertyStore? properties = null;
                IAudioEndpointVolume? volume = null;
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out var id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { Format = new("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
                    string name;
                    try { name = value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "Audio output" : "Audio output"; }
                    finally { PropVariantClear(ref value); }
                    volume = Activate(device);
                    Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out var level));
                    Marshal.ThrowExceptionForHR(volume.GetMute(out var muted));
                    outputs.Add(new(id, name, Math.Round(level * 100, 1), muted, id == defaultId));
                }
                finally { Release(volume); Release(properties); Release(device); }
            }
            return outputs;
        }
        finally { Release(defaultDevice); Release(collection); Release(enumerator); }
    }

    public static bool Matches(string endpointName, Headphone headphone)
    {
        // Match the complete paired name, optionally inside Windows' endpoint label.
        // Never use broad brand/model fragments (e.g. P3 also matches P3i).
        var name = endpointName.Trim();
        return name.Equals(headphone.Name, StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("(" + headphone.Name + ")", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("(" + headphone.Name + " Stereo)", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith("(" + headphone.Name + " Hands-Free AG Audio)", StringComparison.OrdinalIgnoreCase);
    }

    public static void Set(string endpointId, double percent, bool muted)
    {
        if (!double.IsFinite(percent) || percent < 0 || percent > 100) throw new ArgumentOutOfRangeException(nameof(percent));
        var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
        IMMDevice? device = null;
        IAudioEndpointVolume? volume = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.GetDevice(endpointId, out device));
            Marshal.ThrowExceptionForHR(device.GetState(out var state));
            if (state != 1) throw new InvalidOperationException("This audio output is no longer connected. Refresh devices and try again.");
            volume = Activate(device);
            Marshal.ThrowExceptionForHR(volume.GetMute(out var wasMuted));
            var context = Guid.Empty;
            // Mute first; when unmuting, set the requested level before sound resumes.
            if (muted && !wasMuted) Marshal.ThrowExceptionForHR(volume.SetMute(true, ref context));
            Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar((float)(percent / 100), ref context));
            if (!muted && wasMuted) Marshal.ThrowExceptionForHR(volume.SetMute(false, ref context));
        }
        finally { Release(volume); Release(device); Release(enumerator); }
    }

    private static IAudioEndpointVolume Activate(IMMDevice device)
    {
        var iid = typeof(IAudioEndpointVolume).GUID;
        Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out var result));
        return (IAudioEndpointVolume)result;
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection collection);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float db, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}

using System.Security.Cryptography;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace SoundCity.Core;

/// <summary>UG3 framing, as used by the exact HP208 profile in UGREEN Audio 2.7.0.</summary>
public static class Ug3Protocol
{
    public static ushort Crc(ReadOnlySpan<byte> bytes)
    {
        ushort crc = 0xFFFF;
        foreach (var b in bytes)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++) crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }

    public static byte[] Frame(byte command, byte sequence, ReadOnlySpan<byte> payload, bool encrypted, bool tlv = true)
    {
        if (payload.Length > 240) throw new ArgumentException("UG3 request exceeds the supported single-frame size.");
        var frame = new byte[payload.Length + 8];
        frame[0] = 0xAA; frame[1] = (byte)(0x30 | (tlv ? 0x80 : 0) | (encrypted ? 0x40 : 0));
        frame[2] = sequence; frame[3] = command;
        frame[4] = (byte)(payload.Length >> 8); frame[5] = (byte)payload.Length;
        payload.CopyTo(frame.AsSpan(6));
        var crc = Crc(payload); frame[^2] = (byte)(crc >> 8); frame[^1] = (byte)crc;
        return frame;
    }

    public static IReadOnlyList<KeyValuePair<byte, byte[]>> ParseTlvList(ReadOnlySpan<byte> payload)
    {
        var result = new List<KeyValuePair<byte, byte[]>>();
        for (var i = 0; i < payload.Length;)
        {
            if (i + 2 > payload.Length) throw new IOException("Truncated UG3 field header.");
            var tag = payload[i++]; var size = payload[i++];
            if (i + size > payload.Length) throw new IOException("Truncated UG3 field value.");
            result.Add(new(tag, payload.Slice(i, size).ToArray()));
            i += size;
        }
        return result;
    }

    // Handshake fields must remain unique. Status responses are ordered TLV lists
    // in the vendor protocol and may repeat fields; those are resolved separately.
    public static Dictionary<byte, byte[]> ParseTlv(ReadOnlySpan<byte> payload)
    {
        var result = new Dictionary<byte, byte[]>();
        foreach (var field in ParseTlvList(payload))
            if (!result.TryAdd(field.Key, field.Value)) throw new IOException($"Duplicate UG3 handshake field 0x{field.Key:X2}.");
        return result;
    }

    public static Dictionary<byte, byte[]> ParseStatusTlv(ReadOnlySpan<byte> payload)
    {
        var result = new Dictionary<byte, byte[]>();
        foreach (var field in ParseTlvList(payload))
        {
            if (result.TryGetValue(field.Key, out var previous) && !previous.AsSpan().SequenceEqual(field.Value))
                throw new Ug3ConflictingStatusException(field.Key);
            result[field.Key] = field.Value;
        }
        return result;
    }

    public static byte[] Tlv(byte tag, params byte[] data)
    {
        if (data.Length > 255) throw new ArgumentException("UG3 field is too long.");
        return new byte[] { tag, (byte)data.Length }.Concat(data).ToArray();
    }

    public static byte[] DeriveKey(byte[] random1, byte[] random2)
    {
        if (random1.Length != 16 || random2.Length != 16) throw new ArgumentException("UG3 nonces must contain 16 bytes.");
        // Protocol constant from the public vendor app, not a user credential.
        byte[] seed = [117, 71, 29, 88, 250, 79, 30, 228, 178, 69, 175, 64, 160, 231, 205, 99];
        return SHA256.HashData(seed.Concat(random1).Concat(random2).ToArray());
    }

    public static byte[] Encrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create(); aes.Key = key;
        return aes.EncryptCbc(data, key.AsSpan(16, 16), PaddingMode.PKCS7);
    }
    public static byte[] Decrypt(byte[] key, byte[] data)
    {
        using var aes = Aes.Create(); aes.Key = key;
        return aes.DecryptCbc(data, key.AsSpan(16, 16), PaddingMode.PKCS7);
    }
}

public sealed class Ug3ConflictingStatusException(byte tag) : IOException("The headphones returned inconsistent settings. Refresh controls to read their current state again.")
{
    public byte Tag { get; } = tag;
}

/// <summary>One encrypted, bounded HP208 control session over its advertised SPP service.</summary>
public sealed class UgreenTransport : IDisposable
{
    private readonly StreamSocket socket;
    private readonly DataReader reader;
    private readonly DataWriter writer;
    private readonly BluetoothDevice bluetooth;
    private readonly RfcommDeviceService service;
    private readonly List<byte> received = [];
    private byte[]? key;
    private byte sequence;

    private UgreenTransport(StreamSocket socket, BluetoothDevice bluetooth, RfcommDeviceService service)
    {
        this.socket = socket; this.bluetooth = bluetooth; this.service = service;
        reader = new(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
        writer = new(socket.OutputStream);
    }

    public static async Task<UgreenTransport> OpenAsync(Headphone device, CancellationToken token)
    {
        if (device.Profile != DeviceCatalog.Max5s) throw new InvalidOperationException("UG3 support is restricted to Studio Max5s HP208.");
        var bluetooth = await BluetoothDevice.FromBluetoothAddressAsync(device.BluetoothAddress).AsTask(token)
            ?? throw new InvalidOperationException("Windows could not access the Max5s.");
        StreamSocket? socket = null;
        RfcommDeviceService? service = null;
        UgreenTransport? session = null;
        try
        {
            var services = await bluetooth.GetRfcommServicesForIdAsync(RfcommServiceId.SerialPort, BluetoothCacheMode.Cached).AsTask(token);
            service = services.Services.FirstOrDefault();
            foreach (var extra in services.Services.Skip(1)) extra.Dispose();
            if (services.Error != BluetoothError.Success || service is null) throw new IOException("The Max5s control service is unavailable. Connect the headphones and retry.");
            socket = new StreamSocket();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(token);
            session = new(socket, bluetooth, service);
            var random1 = RandomNumberGenerator.GetBytes(16);
            var response = await session.RequestAsync(255, Ug3Protocol.Tlv(1, random1).Concat(Ug3Protocol.Tlv(2)).ToArray(), token);
            var fields = Ug3Protocol.ParseTlv(response);
            if (!fields.TryGetValue(1, out var random2) || !fields.TryGetValue(2, out var proof)) throw new IOException("The Max5s did not complete its encryption handshake.");
            session.key = Ug3Protocol.DeriveKey(random1, random2);
            var actual = Ug3Protocol.Decrypt(session.key, proof);
            if (!CryptographicOperations.FixedTimeEquals(actual, random1.Concat(random2).ToArray())) throw new IOException("The Max5s encryption handshake could not be verified.");
            return session;
        }
        catch
        {
            if (session is not null) session.Dispose();
            else { socket?.Dispose(); service?.Dispose(); bluetooth.Dispose(); }
            throw;
        }
    }

    public async Task<byte[]> RequestAsync(byte command, byte[] payload, CancellationToken token, bool tlv = true)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var seq = ++sequence;
        var encrypted = key is not null && command != 255;
        var wire = encrypted ? Ug3Protocol.Encrypt(key!, payload) : payload;
        writer.WriteBytes(Ug3Protocol.Frame(command, seq, wire, encrypted, tlv));
        await writer.StoreAsync().AsTask(timeout.Token);
        var totalBytes = 0;
        while (totalBytes < 8192)
        {
            while (received.Count >= 8)
            {
                if (received[0] != 0xAA) throw new IOException("Unexpected Max5s protocol header.");
                var count = (received[4] << 8) | received[5];
                if (count > 4096) throw new IOException("Max5s response is too large.");
                if (received.Count < count + 8) break;
                var frame = received.Take(count + 8).ToArray(); received.RemoveRange(0, count + 8);
                // Ignore unrelated notifications; only this request's reply can complete it.
                if (frame[2] != seq || frame[3] != command || (frame[1] & 6) != 2) continue;
                if ((frame[1] & 8) != 0) throw new IOException("This Max5s response requires unsupported fragmentation.");
                var data = frame.AsSpan(6, count).ToArray();
                if (Ug3Protocol.Crc(data) != ((frame[^2] << 8) | frame[^1])) throw new IOException("Max5s response checksum failed.");
                if ((frame[1] & 64) != 0)
                {
                    if (key is null) throw new IOException("Unexpected encrypted handshake response.");
                    data = Ug3Protocol.Decrypt(key, data);
                }
                if (data.Length == 0 || data[0] != 0) throw new IOException($"The Max5s rejected this request (status {(data.Length == 0 ? "missing" : data[0].ToString())}).");
                return data[1..];
            }
            var size = await reader.LoadAsync(512).AsTask(timeout.Token);
            if (size == 0) throw new IOException("The Max5s closed its settings connection. Close the phone's UGREEN control screen, then retry.");
            var bytes = new byte[size]; reader.ReadBytes(bytes); received.AddRange(bytes); totalBytes += bytes.Length;
        }
        throw new IOException("No matching Max5s response arrived within the receive limit.");
    }

    public Task<byte[]> QueryStatusRawAsync(CancellationToken token) => RequestAsync(1, new byte[] { 1, 1, 0, 2, 1, 0, 4, 1, 0, 8, 1, 0 }, token);
    public void Dispose()
    {
        if (key is not null) CryptographicOperations.ZeroMemory(key);
        reader.DetachStream(); writer.DetachStream(); reader.Dispose(); writer.Dispose(); socket.Dispose(); service.Dispose(); bluetooth.Dispose();
    }
}

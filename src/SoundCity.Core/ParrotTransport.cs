using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace SoundCity.Core;

/// <summary>Zik 3 RFCOMM protocol. See THIRD-PARTY-NOTICES.txt for protocol references.</summary>
public static class ParrotProtocol
{
    public static readonly Guid ServiceId = new("8b6814d3-6ce7-4498-9700-9312c1711f64");
    public static byte[] Request(string path, string? argument = null)
    {
        if (!path.StartsWith("/api/", StringComparison.Ordinal) || path.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '_')))
            throw new ArgumentException("Invalid Parrot resource path.");
        if (argument?.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '&' or '=' or '-')) == true)
            throw new ArgumentException("Invalid Parrot setting argument.");
        var body = Encoding.ASCII.GetBytes(argument is null ? $"GET {path}/get" : $"SET {path}/set?arg={argument}");
        if (body.Length > 1024) throw new ArgumentException("Parrot request is too large.");
        var frame = new byte[body.Length + 3];
        BinaryPrimitives.WriteUInt16BigEndian(frame, (ushort)frame.Length);
        frame[2] = 0x80;
        body.CopyTo(frame, 3);
        return frame;
    }

    // Stream reads must handle fragmentation and multiple frames in one receive.
    internal static async Task<byte[]> ReadFrameAsync(Stream stream, CancellationToken token)
    {
        var header = new byte[3];
        await stream.ReadExactlyAsync(header, token);
        var length = BinaryPrimitives.ReadUInt16BigEndian(header);
        if (length is < 3 or > 16384) throw new IOException("Invalid Parrot response size.");
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), token);
        return frame;
    }

    public static XElement ParseResponse(byte[] frame, string expectedPath, bool write = false)
    {
        if (frame.Length < 8 || BinaryPrimitives.ReadUInt16BigEndian(frame) != frame.Length || frame[2] != 0x80)
            throw new IOException("Invalid Parrot response frame.");
        // Response payload has a four-byte prefix before its UTF-8 XML (seven-byte total header).
        // The prefix's undocumented fields are not used to guess device state.
        using var data = new MemoryStream(frame, 7, frame.Length - 7, false);
        using var xml = XmlReader.Create(data, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16384 });
        var root = XElement.Load(xml);
        if (root.Name == "notify") return root;
        if (root.Name != "answer" || (string?)root.Attribute("path") != expectedPath + (write ? "/set" : "/get"))
            throw new IOException("The Parrot response did not match the requested setting.");
        var error = (string?)root.Attribute("error");
        if (error is not (null or "false" or "0")) throw new ParrotRequestRejectedException(expectedPath);
        return root;
    }
}

public sealed class ParrotRequestRejectedException(string path) : IOException($"The Zik 3 does not accept this request: {path}");

internal sealed class ParrotTransport : IDisposable
{
    private readonly StreamSocket socket;
    private readonly BluetoothDevice bluetooth;
    private readonly RfcommDeviceService service;
    private readonly Stream input, output;

    private ParrotTransport(StreamSocket socket, BluetoothDevice bluetooth, RfcommDeviceService service)
    {
        this.socket = socket; this.bluetooth = bluetooth; this.service = service;
        input = socket.InputStream.AsStreamForRead(); output = socket.OutputStream.AsStreamForWrite();
    }

    public static async Task<ParrotTransport> OpenAsync(Headphone device, CancellationToken token)
    {
        if (device.Profile != DeviceCatalog.Zik3) throw new InvalidOperationException("The Parrot driver is restricted to Zik 3.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var bluetooth = await BluetoothDevice.FromBluetoothAddressAsync(device.BluetoothAddress).AsTask(timeout.Token)
            ?? throw new IOException("Windows could not access the Zik 3.");
        StreamSocket? socket = null;
        RfcommDeviceService? service = null;
        ParrotTransport? session = null;
        try
        {
            var result = await bluetooth.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(ParrotProtocol.ServiceId), BluetoothCacheMode.Cached).AsTask(timeout.Token);
            service = result.Services.FirstOrDefault();
            foreach (var extra in result.Services.Skip(1)) extra.Dispose();
            if (result.Error != BluetoothError.Success || service is null) throw new IOException("The Zik 3 control service is unavailable. Connect it by Bluetooth and close the phone's Parrot control app, then retry.");
            socket = new();
            await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication).AsTask(timeout.Token);
            session = new(socket, bluetooth, service);
            await session.output.WriteAsync(new byte[] { 0, 3, 0 }, timeout.Token);
            await session.output.FlushAsync(timeout.Token);
            var response = await ParrotProtocol.ReadFrameAsync(session.input, timeout.Token);
            if (!response.AsSpan().SequenceEqual(new byte[] { 0, 3, 2 })) throw new IOException("The Zik 3 did not acknowledge the control session.");
            return session;
        }
        catch
        {
            if (session is not null) session.Dispose();
            else { socket?.Dispose(); service?.Dispose(); bluetooth.Dispose(); }
            throw;
        }
    }

    public async Task<XElement> RequestAsync(string path, string? argument, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await output.WriteAsync(ParrotProtocol.Request(path, argument), timeout.Token);
        await output.FlushAsync(timeout.Token);
        for (var count = 0; count < 32; count++)
        {
            var response = ParrotProtocol.ParseResponse(await ParrotProtocol.ReadFrameAsync(input, timeout.Token), path, argument is not null);
            if (response.Name == "answer") return response;
        }
        throw new IOException("Too many Parrot notifications arrived without a matching response. Refresh controls.");
    }

    public void Dispose() { input.Dispose(); output.Dispose(); socket.Dispose(); service.Dispose(); bluetooth.Dispose(); }
}

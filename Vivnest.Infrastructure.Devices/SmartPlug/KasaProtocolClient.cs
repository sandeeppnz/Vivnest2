using System.Net.Sockets;
using System.Text;

namespace Vivnest.Infrastructure.SmartPlug;

// The legacy TP-Link/Kasa local protocol: plain TCP on port 9999, no TLS,
// no auth - just a length-prefixed payload XOR-obfuscated with a rolling
// key starting at 171. Deliberately not the newer Tapo/KLAP protocol
// (see RtspCamera's ffmpeg path and the Tapo motion-detection investigation
// in decision-log.md) - this older plug line never adopted that handshake.
internal static class KasaProtocolClient
{
    private const int Port = 9999;
    private const byte InitialKey = 171;

    // A real sysinfo+emeter response is a few KB. The length prefix is a
    // raw int off an unauthenticated LAN socket, and it used to be handed
    // straight to `new byte[length]` - so anything answering port 9999
    // could claim a 2GB response and drive the allocator (on a Raspberry
    // Pi) into the ground, or send a negative length for an unhelpful
    // OverflowException. 1MB is three orders of magnitude above any real
    // response while still failing fast on garbage.
    private const int MaxResponseLength = 1024 * 1024;

    // port is overridable for tests only (a localhost listener on an
    // ephemeral port); every production caller uses the protocol's fixed
    // 9999.
    public static async Task<string> SendCommandAsync(
        string host,
        string commandJson,
        TimeSpan timeout,
        CancellationToken cancellationToken,
        int port = Port)
    {
        using var client = new TcpClient();
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCts.Token);

        await client.ConnectAsync(host, port, linkedCts.Token);

        using var stream = client.GetStream();

        var payload = Encrypt(commandJson);
        var lengthPrefix = BitConverter.GetBytes(payload.Length);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthPrefix);

        await stream.WriteAsync(lengthPrefix, linkedCts.Token);
        await stream.WriteAsync(payload, linkedCts.Token);

        var lengthBuffer = new byte[4];
        await ReadExactAsync(stream, lengthBuffer, linkedCts.Token);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBuffer);

        var responseLength = BitConverter.ToInt32(lengthBuffer, 0);

        if (responseLength is < 0 or > MaxResponseLength)
        {
            throw new IOException(
                $"Kasa device claimed a response of {responseLength} bytes " +
                $"(limit {MaxResponseLength}); refusing to allocate it.");
        }

        var responseBuffer = new byte[responseLength];

        await ReadExactAsync(stream, responseBuffer, linkedCts.Token);

        return Decrypt(responseBuffer);
    }

    private static async Task ReadExactAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;

        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            if (read == 0)
                throw new IOException("Connection closed before the expected response was fully received.");

            offset += read;
        }
    }

    private static byte[] Encrypt(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var key = InitialKey;

        for (var i = 0; i < bytes.Length; i++)
        {
            var c = (byte)(bytes[i] ^ key);
            key = c;
            bytes[i] = c;
        }

        return bytes;
    }

    private static string Decrypt(byte[] ciphertext)
    {
        var key = InitialKey;
        var bytes = new byte[ciphertext.Length];

        for (var i = 0; i < ciphertext.Length; i++)
        {
            var c = ciphertext[i];
            bytes[i] = (byte)(c ^ key);
            key = c;
        }

        return Encoding.UTF8.GetString(bytes);
    }
}

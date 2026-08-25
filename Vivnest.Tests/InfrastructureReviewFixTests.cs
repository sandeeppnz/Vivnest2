using System.Net;
using System.Net.Sockets;
using System.Text;
using Vivnest.Infrastructure.Camera;
using Vivnest.Infrastructure.SmartPlug;

namespace Vivnest.Tests;

// Defects from the 2026-08-24 review of Vivnest.Infrastructure(.Devices).
public class InfrastructureReviewFixTests
{
    // ---- the RTSP password must not survive into error text ---------------

    // ffmpeg echoes its input URL - rtsp://user:password@host/... - in its
    // error output, and RtspCamera embeds that output in an exception
    // message that travels to runtime.LastError, the heartbeat's Error
    // column, the DeviceOffline Telegram notification and the persisted
    // failure event. The scrub is what stands between one failed capture
    // and the camera password in a chat message.
    [Fact]
    public void TheEscapedPasswordIsScrubbedFromFfmpegOutput()
    {
        // What ffmpeg actually echoes: the URL as we built it, escaped.
        var stderr =
            "Input #0, rtsp, from 'rtsp://admin:1Pass%40word@192.168.50.166:554/stream1':\n" +
            "rtsp://admin:1Pass%40word@192.168.50.166:554/stream1: Connection refused";

        var scrubbed = RtspCamera.ScrubCredentials(stderr, "admin", "1Pass@word");

        Assert.DoesNotContain("1Pass%40word", scrubbed);
        Assert.DoesNotContain("1Pass@word", scrubbed);
        Assert.DoesNotContain("admin", scrubbed);

        // The rest of the diagnosis survives - scrubbing must not eat the
        // message that explains the failure.
        Assert.Contains("Connection refused", scrubbed);
        Assert.Contains("192.168.50.166", scrubbed);
    }

    [Fact]
    public void TheRawPasswordIsScrubbedEvenWhenItNeedsNoEscaping()
    {
        var scrubbed = RtspCamera.ScrubCredentials(
            "auth failed for user admin with hunter2", "admin", "hunter2");

        Assert.DoesNotContain("hunter2", scrubbed);
        Assert.DoesNotContain("admin", scrubbed);
    }

    [Fact]
    public void EmptyCredentialsScrubNothingAndBreakNothing()
    {
        const string stderr = "Connection to rtsp://192.168.50.166:554/stream1 failed";

        Assert.Equal(stderr, RtspCamera.ScrubCredentials(stderr, "", ""));
    }

    // ---- a Kasa device cannot make the Agent allocate what it claims ------

    // The length prefix comes off an unauthenticated LAN socket and used to
    // be handed straight to `new byte[length]` - anything answering the
    // port could claim a 2GB response. Driven against a real TCP listener
    // so the whole framing path (write, length prefix, ReadExact) runs.
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    public async Task AnAbsurdResponseLengthIsRefusedNotAllocated(int claimedLength)
    {
        using var listener = StartListener(out var port);

        var serve = ServeOneRequestAsync(listener, respondWithLength: claimedLength, body: []);

        var ex = await Assert.ThrowsAsync<IOException>(() =>
            KasaProtocolClient.SendCommandAsync(
                "127.0.0.1", "{}", TimeSpan.FromSeconds(5), CancellationToken.None, port));

        Assert.Contains("refusing to allocate", ex.Message);

        await serve;
    }

    // The cap must not break real traffic: a legitimate framed, XOR-ciphered
    // response round-trips. This also pins the cipher and framing against
    // the same listener.
    [Fact]
    public async Task ARealSizedResponseStillRoundTrips()
    {
        const string responseJson = """{"system":{"get_sysinfo":{"relay_state":1}}}""";
        var cipherBody = XorCipher(Encoding.UTF8.GetBytes(responseJson));

        using var listener = StartListener(out var port);

        var serve = ServeOneRequestAsync(listener, respondWithLength: cipherBody.Length, body: cipherBody);

        var response = await KasaProtocolClient.SendCommandAsync(
            "127.0.0.1", """{"system":{"get_sysinfo":{}}}""", TimeSpan.FromSeconds(5), CancellationToken.None, port);

        Assert.Equal(responseJson, response);

        await serve;
    }

    // =======================================================================

    private static TcpListener StartListener(out int port)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;

        return listener;
    }

    private static async Task ServeOneRequestAsync(
        TcpListener listener, int respondWithLength, byte[] body)
    {
        using var client = await listener.AcceptTcpClientAsync();
        using var stream = client.GetStream();

        // Drain the request: 4-byte big-endian length, then that many bytes.
        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(lengthBuffer);

        var requestLength = BitConverter.ToInt32(lengthBuffer, 0);
        await stream.ReadExactlyAsync(new byte[requestLength]);

        // Respond with whatever length the test scripted - which is the
        // point: the CLAIMED length and the actual body can disagree.
        var responsePrefix = BitConverter.GetBytes(respondWithLength);

        if (BitConverter.IsLittleEndian)
            Array.Reverse(responsePrefix);

        await stream.WriteAsync(responsePrefix);

        if (body.Length > 0)
            await stream.WriteAsync(body);
    }

    // The Kasa "encryption": XOR with a rolling key starting at 171, the
    // key being the previous CIPHERTEXT byte. Reimplemented here (10 lines)
    // rather than exposed from the client, so the test proves the client
    // against the protocol, not against itself.
    private static byte[] XorCipher(byte[] plain)
    {
        var result = new byte[plain.Length];
        byte key = 171;

        for (var i = 0; i < plain.Length; i++)
        {
            result[i] = (byte)(plain[i] ^ key);
            key = result[i];
        }

        return result;
    }
}

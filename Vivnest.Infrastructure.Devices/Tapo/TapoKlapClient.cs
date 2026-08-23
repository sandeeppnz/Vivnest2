using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Vivnest.Infrastructure.Tapo;

// TP-Link's "KLAP" protocol (v2 handshake hashes), used by newer Tapo-branded
// devices - hubs (H100), some plugs/bulbs, and, separately, cameras (where a
// firmware bug blocks it - see the Tapo motion-detection investigation in
// decision-log.md). Verified against a real H100 hub before this was written:
// a throwaway spike reproduced this exact handshake/encryption and
// successfully read a live T100 child sensor's state.
//
// Handshake (per device, once per client instance):
//   1. POST random local_seed to /app/handshake1, get back remote_seed (16
//      bytes) + server_hash (32 bytes) = sha256(local_seed + remote_seed + auth_hash).
//      auth_hash = sha256(sha1(username) + sha1(password)). Matching the hash
//      confirms the device accepted these credentials.
//   2. POST sha256(remote_seed + local_seed + auth_hash) to /app/handshake2.
//      200 means the device is now expecting the derived session key below.
// Session key/iv/sig are derived once from local_seed/remote_seed/auth_hash
// and reused for every request in this session; each request increments a
// sequence number that's mixed into both the IV and the request signature.
public sealed class TapoKlapClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _username;
    private readonly string _password;

    private byte[]? _key;
    private byte[]? _iv;
    private byte[]? _sig;
    private int _seq;
    private bool _handshakeDone;

    public TapoKlapClient(string host, string username, string password, TimeSpan timeout)
    {
        _username = username;
        _password = password;

        var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri($"http://{host}/app/"),
            Timeout = timeout,
        };
    }

    public async Task<JsonElement> SendAsync(
        string method,
        object? @params,
        CancellationToken cancellationToken)
    {
        var doc = await SendRawAsync(BuildRequest(method, @params), cancellationToken);
        return doc.RootElement.GetProperty("result");
    }

    // Wraps the request in the hub's control_child envelope, addressed at a
    // specific child device id, and unwraps the child's response - a T100
    // (or any other hub-paired sensor) has no network presence of its own,
    // so every child request actually goes to the hub.
    public async Task<JsonElement> SendChildRequestAsync(
        string childDeviceId,
        string method,
        object? @params,
        CancellationToken cancellationToken)
    {
        var controlChildParams = new
        {
            device_id = childDeviceId,
            requestData = new { method, @params },
        };

        var doc = await SendRawAsync(
            BuildRequest("control_child", controlChildParams),
            cancellationToken);

        return doc.RootElement
            .GetProperty("result")
            .GetProperty("responseData")
            .GetProperty("result");
    }

    private static object BuildRequest(string method, object? @params)
    {
        return @params is null
            ? new { method, request_time_milis = NowMillis(), terminal_uuid = NewTerminalUuid() }
            : new { method, request_time_milis = NowMillis(), terminal_uuid = NewTerminalUuid(), @params };
    }

    private async Task<JsonDocument> SendRawAsync(object request, CancellationToken cancellationToken)
    {
        if (!_handshakeDone)
        {
            await HandshakeAsync(cancellationToken);
        }

        var json = JsonSerializer.Serialize(request);
        var (payload, seq) = Encrypt(json);

        var response = await _http.PostAsync(
            $"request?seq={seq}",
            new ByteArrayContent(payload),
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            // A 403 here means the session the device knows about no longer
            // matches ours (e.g. it rebooted) - not treated as a hard
            // failure, callers get a clear exception either way since this
            // client is used fresh per read, not retried internally.
            throw new InvalidOperationException(
                $"Tapo device rejected request with {(int)response.StatusCode}.");
        }

        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var decrypted = Decrypt(responseBytes, seq);

        return JsonDocument.Parse(decrypted);
    }

    private async Task HandshakeAsync(CancellationToken cancellationToken)
    {
        var authHash = Sha256(Concat(Sha1(Encoding.UTF8.GetBytes(_username)), Sha1(Encoding.UTF8.GetBytes(_password))));

        var localSeed = RandomNumberGenerator.GetBytes(16);

        var h1Response = await _http.PostAsync(
            "handshake1",
            new ByteArrayContent(localSeed),
            cancellationToken);

        if (!h1Response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Tapo handshake1 failed with {(int)h1Response.StatusCode}.");
        }

        var h1Body = await h1Response.Content.ReadAsByteArrayAsync(cancellationToken);

        if (h1Body.Length != 48)
        {
            throw new InvalidOperationException(
                $"Tapo handshake1 returned an unexpected response length ({h1Body.Length}).");
        }

        var remoteSeed = h1Body[0..16];
        var serverHash = h1Body[16..48];

        var expectedHash = Sha256(Concat(localSeed, remoteSeed, authHash));

        if (!expectedHash.SequenceEqual(serverHash))
        {
            throw new InvalidOperationException(
                "Tapo handshake1 hash mismatch - check the account email/password.");
        }

        var h2Payload = Sha256(Concat(remoteSeed, localSeed, authHash));

        var h2Response = await _http.PostAsync(
            "handshake2",
            new ByteArrayContent(h2Payload),
            cancellationToken);

        if (!h2Response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Tapo handshake2 failed with {(int)h2Response.StatusCode}.");
        }

        _key = Sha256(Concat(Encoding.ASCII.GetBytes("lsk"), localSeed, remoteSeed, authHash))[0..16];

        var ivFull = Sha256(Concat(Encoding.ASCII.GetBytes("iv"), localSeed, remoteSeed, authHash));
        _iv = ivFull[0..12];
        _seq = BinaryPrimitives.ReadInt32BigEndian(ivFull[28..32]);

        _sig = Sha256(Concat(Encoding.ASCII.GetBytes("ldk"), localSeed, remoteSeed, authHash))[0..28];

        _handshakeDone = true;
    }

    private byte[] BuildIvSeq(int seq)
    {
        var seqBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(seqBytes, seq);
        return Concat(_iv!, seqBytes);
    }

    private (byte[] Payload, int Seq) Encrypt(string json)
    {
        _seq += 1;
        var ivSeq = BuildIvSeq(_seq);

        using var aes = Aes.Create();
        aes.Key = _key!;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = ivSeq;

        using var encryptor = aes.CreateEncryptor();
        var plain = Encoding.UTF8.GetBytes(json);
        var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);

        var seqBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(seqBytes, _seq);
        var signature = Sha256(Concat(_sig!, seqBytes, cipher));

        return (Concat(signature, cipher), _seq);
    }

    private string Decrypt(byte[] data, int seq)
    {
        var ivSeq = BuildIvSeq(seq);

        using var aes = Aes.Create();
        aes.Key = _key!;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.IV = ivSeq;

        using var decryptor = aes.CreateDecryptor();
        var cipher = data[32..];
        var plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plain);
    }

    private static long NowMillis() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private static string NewTerminalUuid() =>
        Convert.ToBase64String(MD5.HashData(Guid.NewGuid().ToByteArray()));

    private static byte[] Sha1(byte[] input) => SHA1.HashData(input);

    private static byte[] Sha256(byte[] input) => SHA256.HashData(input);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    public void Dispose() => _http.Dispose();
}

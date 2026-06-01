using System.Net.Sockets;
using Maple.Adapters.V113.Crypto;
using Maple.Tools.PacketDecoder;
using Maple.Versioning;

namespace Maple.Tools.HeadlessClient;

/// <summary>
/// 單一 TCP 連線 + stateful cipher pair。
///
/// cipher 方向（與 PacketStreamDecoder 一致）：
///   c2s 加密 → recv cipher（recvIv, version 113）
///   s2c 解密 → send cipher（sendIv, version 0xFF6E）
/// </summary>
public sealed class MapleConnection : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly IPacketCipher _recv; // c2s: encrypt outgoing
    private readonly IPacketCipher _send; // s2c: decrypt incoming

    private MapleConnection(TcpClient tcp, NetworkStream stream, IPacketCipher recv, IPacketCipher send)
    {
        _tcp   = tcp;
        _stream = stream;
        _recv  = recv;
        _send  = send;
    }

    /// <summary>
    /// 連線 → 讀未加密 getHello（2-byte payloadLen + payload）→ 解析 IV → 建 cipher pair。
    /// </summary>
    public static async Task<MapleConnection> ConnectAsync(
        string host, int port, CancellationToken ct = default)
    {
        var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, ct);
        var stream = tcp.GetStream();

        // getHello 格式：[short payloadLen LE][payload...]
        var lenBuf = new byte[2];
        await stream.ReadExactlyAsync(lenBuf, ct);
        int payloadLen = lenBuf[0] | (lenBuf[1] << 8);

        // 重組完整 hello（含 2-byte prefix）供 HelloPacketParser
        var helloFull = new byte[2 + payloadLen];
        lenBuf.CopyTo(helloFull, 0);
        await stream.ReadExactlyAsync(helloFull.AsMemory(2, payloadLen), ct);

        var (recvIv, sendIv) = HelloPacketParser.Parse(helloFull);

        var (recv, send) = new V113CipherFactory().CreateSessionPair(recvIv, sendIv);
        return new MapleConnection(tcp, stream, recv, send);
    }

    /// <summary>
    /// 送出明文 payload（前 2 bytes 為 opcode LE）。
    /// 內部：WriteHeader（recv cipher，不推進 IV）→ Crypt（推進 IV）→ 寫 TCP。
    /// </summary>
    public async Task SendAsync(byte[] payload, CancellationToken ct = default)
    {
        var body = (byte[])payload.Clone(); // 不改動呼叫端 buffer
        var framed = new byte[body.Length + 4];
        _recv.WriteHeader(framed.AsSpan(0, 4), body.Length); // c2s 用 recv
        _recv.Crypt(body);
        body.CopyTo(framed, 4);
        await _stream.WriteAsync(framed, ct);
    }

    /// <summary>
    /// 讀一個 s2c 封包並解密，回傳明文 payload（前 2 bytes 為 opcode LE）。
    /// cipher desync 時拋 InvalidDataException。
    /// </summary>
    public async Task<byte[]> ReceiveAsync(CancellationToken ct = default)
    {
        var header = new byte[4];
        await _stream.ReadExactlyAsync(header, ct);

        if (!_send.Check(header))
            throw new InvalidDataException(
                $"s2c cipher desync: header={Convert.ToHexString(header)}");

        int length = _send.ReadLength(header);
        var body = new byte[length];
        await _stream.ReadExactlyAsync(body, ct);
        _send.Crypt(body); // s2c 用 send，同時推進 IV
        return body;
    }

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync();
        _tcp.Dispose();
        if (_recv is IDisposable rd) rd.Dispose();
        if (_send is IDisposable sd) sd.Dispose();
    }
}

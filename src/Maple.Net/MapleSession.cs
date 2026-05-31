using System.Net.Sockets;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Net;

/// <summary>
/// 一條客戶端連線：負責 socket I/O 與 v113 封包 framing。
/// 握手前用 <see cref="SendRawAsync"/>（未加密），啟用 cipher 後用 <see cref="SendAsync"/>（4-byte 頭 + 加密）。
/// 接收迴圈 <see cref="RunAsync"/>：讀 4-byte 頭 → 驗證 → 取長度 → 讀 body → 解密 → 回呼。
/// M1 用 NetworkStream + ReadExactly（簡單正確；Pipelines 為日後優化，見設計書 §0.5「先求能動」）。
/// </summary>
public sealed class MapleSession : IAsyncDisposable
{
    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly ILogger _log;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    private IPacketCipher? _recv;
    private IPacketCipher? _send;

    public MapleSession(Socket socket, ILogger log)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: true);
        _log = log;
    }

    public string Remote => _socket.RemoteEndPoint?.ToString() ?? "?";

    /// <summary>啟用雙向 cipher（握手送出後呼叫）。</summary>
    public void SetCiphers(IPacketCipher recv, IPacketCipher send)
    {
        _recv = recv;
        _send = send;
    }

    /// <summary>未加密原樣送出（握手 getHello 專用）。</summary>
    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>加密送出：4-byte 頭（當前 send IV）+ crypt(body)。對 cipher 狀態加鎖序列化。</summary>
    public async Task SendAsync(byte[] packet, CancellationToken ct)
    {
        if (_send is null) throw new InvalidOperationException("send cipher 尚未啟用");

        await _sendLock.WaitAsync(ct);
        try
        {
            var framed = new byte[packet.Length + 4];
            _send.WriteHeader(framed.AsSpan(0, 4), packet.Length);
            _send.Crypt(packet);
            packet.CopyTo(framed.AsSpan(4));
            await _stream.WriteAsync(framed, ct);
            await _stream.FlushAsync(ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>接收迴圈：每個解密後的封包呼叫一次 <paramref name="onPacket"/>（body 含 2-byte opcode 開頭）。</summary>
    public async Task RunAsync(Func<byte[], MapleSession, CancellationToken, Task> onPacket, CancellationToken ct)
    {
        if (_recv is null) throw new InvalidOperationException("recv cipher 尚未啟用");

        var header = new byte[4];
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _stream.ReadExactlyAsync(header, ct);
            }
            catch (EndOfStreamException)
            {
                _log.LogInformation("連線關閉 {Remote}", Remote);
                return;
            }

            if (!_recv.Check(header))
            {
                _log.LogWarning("封包頭驗證失敗 {Remote}，關閉連線（可能 cipher/版本不符）", Remote);
                return;
            }

            int length = _recv.ReadLength(header);
            if (length is <= 0 or > 0x10000)
            {
                _log.LogWarning("不合理的封包長度 {Length} {Remote}，關閉", length, Remote);
                return;
            }

            var body = new byte[length];
            await _stream.ReadExactlyAsync(body, ct);
            _recv.Crypt(body);

            await onPacket(body, this, ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        await _stream.DisposeAsync();
    }
}

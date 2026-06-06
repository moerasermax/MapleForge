using System.Net.Sockets;
using System.Threading.Channels;
using Maple.Versioning;
using Microsoft.Extensions.Logging;

namespace Maple.Net;

/// <summary>
/// 一條客戶端連線：負責 socket I/O 與 v113 封包 framing。
/// 握手前用 <see cref="SendRawAsync"/>（未加密），啟用 cipher 後用 <see cref="SendAsync"/>（4-byte 頭 + 加密）。
/// 接收迴圈 <see cref="RunAsync"/>：讀 4-byte 頭 → 驗證 → 取長度 → 讀 body → 解密 → 回呼。
/// M1 用 NetworkStream + ReadExactly（簡單正確；Pipelines 為日後優化，見設計書 §0.5「先求能動」）。
///
/// ⭐ 送出路徑（效能稽核 P0 修補，2026-06-06，見任務歷程 07）：
///   每連線一個「有界 outbound Channel + 單一背景 pump」。<see cref="SendAsync"/> 入列即返回，
///   pump 是唯一的寫者：依序做 framing→cipher.Crypt→寫 socket。如此：
///   ①絕不原地加密呼叫者的 byte[]（廣播時同一封包餵多人，原地 mutate 會讓第 2+ 收件人收到二次加密垃圾）；
///   ②慢/惡意收件人不阻塞廣播迴圈與其他收件人；佇列滿（客戶端不收）＝主動斷線該連線，不拖垮 server；
///   ③cipher 狀態（IV 演化）由單一 pump 序列化推進，免鎖。
/// </summary>
public sealed class MapleSession : IAsyncDisposable
{
    /// <summary>每連線 outbound 佇列容量。滿了＝客戶端不讀（慢/惡意）→ 主動斷線，不丟封包也不阻塞 server。</summary>
    private const int OutboundQueueCapacity = 256;

    private readonly Socket _socket;
    private readonly NetworkStream _stream;
    private readonly ILogger _log;

    private readonly Channel<byte[]> _outbound = Channel.CreateBounded<byte[]>(
        new BoundedChannelOptions(OutboundQueueCapacity)
        {
            SingleReader = true,            // 唯一 consumer＝pump
            SingleWriter = false,           // 多個來源可送（自己的 handler + 其他連線的廣播）
            FullMode = BoundedChannelFullMode.Wait,
            AllowSynchronousContinuations = false,
        });
    private readonly CancellationTokenSource _pumpCts = new();
    private Task? _pump;
    private int _aborted;

    private IPacketCipher? _recv;
    private IPacketCipher? _send;
    private PacketCaptureWriter? _capture;

    public MapleSession(Socket socket, ILogger log)
    {
        _socket = socket;
        _stream = new NetworkStream(socket, ownsSocket: true);
        _log = log;
    }

    public string Remote => _socket.RemoteEndPoint?.ToString() ?? "?";

    /// <summary>啟用雙向 cipher（握手送出後呼叫）。同時啟動 outbound pump（送出側自此走佇列）。</summary>
    public void SetCiphers(IPacketCipher recv, IPacketCipher send)
    {
        _recv = recv;
        _send = send;
        _pump ??= PumpOutboundAsync();
    }

    /// <summary>
    /// 啟用 server 端封包擷取（封包擷取模式・第二刀）。預設關閉，僅 env MAPLEFORGE_CAPTURE=1 時生效。
    /// 須在 <see cref="SetCiphers"/> 後、握手 IV 已知時呼叫。診斷用，失敗不影響連線。
    /// </summary>
    public void EnableCapture(byte[] recvIv, byte[] sendIv)
        => _capture = PacketCaptureWriter.TryCreateFromEnv(Remote, recvIv, sendIv);

    /// <summary>未加密原樣送出（握手 getHello 專用，cipher 啟用前；不走佇列）。</summary>
    public async Task SendRawAsync(ReadOnlyMemory<byte> bytes, CancellationToken ct)
    {
        await _stream.WriteAsync(bytes, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>
    /// 加密送出：把 plaintext 入 outbound 佇列即返回（實際 framing+加密+寫 socket 由 pump 序列化執行）。
    /// 入列後絕不修改 <paramref name="packet"/>（廣播會共用同一 byte[]，pump 只讀不改、加密寫進各自複本）。
    /// 佇列已滿＝對端不收 → 主動斷線該連線（回傳已完成，後續送出對已關閉連線為 no-op）。
    /// </summary>
    public Task SendAsync(byte[] packet, CancellationToken ct)
    {
        if (_send is null) throw new InvalidOperationException("send cipher 尚未啟用");

        if (_outbound.Writer.TryWrite(packet))
            return Task.CompletedTask;

        // TryWrite 失敗＝佇列滿（對端不讀）或已 Complete（連線關閉中）。前者主動斷線，後者已在收尾。
        if (Volatile.Read(ref _aborted) == 0)
        {
            _log.LogWarning("送出佇列已滿（對端不讀），主動斷線 {Remote}", Remote);
            Abort();
        }
        return Task.CompletedTask;
    }

    /// <summary>單一 pump：依序取出佇列封包 → 4-byte 頭 + 加密複本 → 寫 socket。cipher IV 由此單寫者序列化推進。</summary>
    private async Task PumpOutboundAsync()
    {
        try
        {
            await foreach (var packet in _outbound.Reader.ReadAllAsync(_pumpCts.Token))
            {
                // 加密寫進 per-send 複本，絕不 mutate 共用的 packet。
                var framed = new byte[packet.Length + 4];
                _send!.WriteHeader(framed.AsSpan(0, 4), packet.Length);
                packet.CopyTo(framed.AsSpan(4));
                _send.Crypt(framed.AsSpan(4));
                await _stream.WriteAsync(framed, _pumpCts.Token);
                await _stream.FlushAsync(_pumpCts.Token);
            }
        }
        catch (Exception ex) when (
            ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // 對端斷線 / 連線收尾：預期事件，乾淨退出。
            _log.LogInformation("送包迴圈結束 {Remote}", Remote);
        }
    }

    /// <summary>主動中止連線（佇列滿/慢讀）：停收新封包、取消 pump、關 socket 讓接收迴圈一併收尾。冪等。</summary>
    private void Abort()
    {
        if (Interlocked.Exchange(ref _aborted, 1) != 0) return;
        _outbound.Writer.TryComplete();
        _pumpCts.Cancel();
        try { _socket.Close(); } catch { /* 已關 */ }
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
                byte[]? encryptedCopy = _capture is null ? null : (byte[])body.Clone();
                _recv.Crypt(body);
                if (encryptedCopy is not null) _capture!.WriteRecv(header, encryptedCopy, body);

                await onPacket(body, this, ct);
            }
            // 客戶端正常/突然斷線（乾淨 EOF、TCP reset、socket 已棄置）＝預期事件，
            // 記 info 後乾淨收尾；真錯誤（解析 bug 等）不在此列，繼續向上拋給 listener 記 Error。
            catch (Exception ex) when (ex is EndOfStreamException or IOException or SocketException or ObjectDisposedException)
            {
                _log.LogInformation("連線關閉 {Remote}", Remote);
                return;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 關閉順序（見任務歷程 07，Gemini 建議）：先停收新封包並 drain pump，最後才 Dispose stream/socket，
        // 避免 pump 寫到一半底層 stream 被提早釋放噴 ObjectDisposedException。
        _outbound.Writer.TryComplete();
        if (_pump is not null)
        {
            // 優雅 drain：讓 pump 把剩餘封包寫完後自然結束；逾時則取消，避免卡在半死連線上。
            var finished = await Task.WhenAny(_pump, Task.Delay(TimeSpan.FromSeconds(5)));
            if (finished != _pump) _pumpCts.Cancel();
            try { await _pump; } catch { /* 已在 pump 內記錄 */ }
        }

        _pumpCts.Dispose();
        _capture?.Dispose();
        await _stream.DisposeAsync();
    }
}

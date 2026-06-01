using System.Globalization;

namespace Maple.Net;

/// <summary>
/// server 端封包擷取（封包擷取模式・第二刀，client→server 方向，有 ground truth）。
/// 把每個收到的封包「原始加密 bytes(含4-byte頭) + server 解密後 bytes + 握手 IV」寫成 NDJSON，
/// 供離線 <c>Maple.Tools.PacketDecoder</c> 重放解密並雙軌對照（離線解密 == server 內建解密）。
///
/// 預設關閉；僅當環境變數 <c>MAPLEFORGE_CAPTURE=1</c> 時啟用（避免平常跑也寫檔）。
/// 純診斷工具：失敗一律靜默略過，絕不影響連線。
/// </summary>
public sealed class PacketCaptureWriter : IDisposable
{
    private readonly StreamWriter _w;
    private int _seq;
    private readonly object _lock = new();
    private bool _disposed;

    private PacketCaptureWriter(StreamWriter w) => _w = w;

    /// <summary>依環境變數決定是否建立擷取器；未啟用回 null。</summary>
    public static PacketCaptureWriter? TryCreateFromEnv(string remote, byte[] recvIv, byte[] sendIv)
    {
        if (Environment.GetEnvironmentVariable("MAPLEFORGE_CAPTURE") is not "1") return null;
        try
        {
            var dir = Environment.GetEnvironmentVariable("MAPLEFORGE_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Directory.GetCurrentDirectory(), "captures");
            Directory.CreateDirectory(dir);
            var safe = remote.Replace(':', '_').Replace('.', '-');
            var path = Path.Combine(dir, $"capture_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}_{safe}.ndjson");
            var w = new StreamWriter(path, append: false) { AutoFlush = true };
            var writer = new PacketCaptureWriter(w);
            writer.WriteLine(
                $"{{\"type\":\"session\",\"recv_iv\":\"{Hex(recvIv)}\",\"send_iv\":\"{Hex(sendIv)}\",\"remote\":\"{remote}\",\"ts_start\":{DateTime.UtcNow.Ticks}}}");
            return writer;
        }
        catch { return null; }   // 診斷工具不可影響連線
    }

    /// <summary>記一個 client→server 封包：raw_enc = [4-byte 頭][加密 body]；dec = server 解密後 body。</summary>
    public void WriteRecv(ReadOnlySpan<byte> header, ReadOnlySpan<byte> encryptedBody, ReadOnlySpan<byte> decryptedBody)
    {
        try
        {
            lock (_lock)
            {
                if (_disposed) return;
                int seq = _seq++;
                Span<byte> framed = encryptedBody.Length <= 4096
                    ? stackalloc byte[header.Length + encryptedBody.Length]
                    : new byte[header.Length + encryptedBody.Length];
                header.CopyTo(framed);
                encryptedBody.CopyTo(framed[header.Length..]);
                _w.WriteLine(
                    $"{{\"type\":\"pkt\",\"seq\":{seq},\"dir\":\"c2s\",\"raw_enc\":\"{Hex(framed)}\",\"dec\":\"{Hex(decryptedBody)}\"}}");
            }
        }
        catch { /* 診斷工具不可影響連線 */ }
    }

    public void Dispose()
    {
        try
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                _w.WriteLine($"{{\"type\":\"end\",\"total\":{_seq}}}");
                _w.Dispose();
            }
        }
        catch { /* ignore */ }
    }

    private void WriteLine(string s) => _w.WriteLine(s);

    private static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).ToLowerInvariant();
}

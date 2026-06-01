using System.Text.Json;
using Maple.Adapters.V113.Crypto;
using Maple.Versioning;

namespace Maple.Tools.PacketDecoder;

/// <summary>
/// windower NDJSON 雙向解碼結果。
/// c2s：已解密，有 server 解密 log ground truth 可對照（位元級可驗證）。
/// s2c：僅 reframe，標記 <b>unverified</b>（無 ground truth，禁止升黃金測試，不斷言解密後語意）。
/// </summary>
public sealed record WindowerDecodeResult(
    byte[] RecvIv,
    byte[] SendIv,
    IReadOnlyList<DecodedPacket> C2sPackets,
    IReadOnlyList<byte[]> S2cRawFrames,
    byte[]? C2sRemainder,
    byte[]? S2cRemainder
);

/// <summary>
/// 消費 windower NDJSON（raw TCP chunk 格式）→ 重組 TCP 流 → reframe → 解碼。
///
/// NDJSON 格式：
///   {"type":"session","pid":..,"socket":..,"ts_start":..}
///   {"type":"chunk","seq":N,"dir":"c2s"|"s2c","ts":..,"raw_hex":"..","api":"recv|send|WSARecv|WSASend","ret":N}
///
/// 解碼流程：
///   1. 按 (dir, seq) 把 chunk bytes 依序累積成連續 s2c / c2s TCP 流。
///   2. s2c 流開頭 = getHello（2-byte payloadLen 前綴，未加密）→ 抽取 recvIv/sendIv。
///   3. getHello 之後的 s2c 與全部 c2s 用 <see cref="TcpStreamReframer.Reframe"/> 切成 framed 封包。
///   4. c2s framed 封包交 <see cref="PacketStreamDecoder.DecodeClientToServer"/> 解密。
/// </summary>
public sealed class WindowerNdjsonDecoder
{
    private readonly IVersionCipherFactory _factory;

    public WindowerNdjsonDecoder(IVersionCipherFactory? factory = null)
        => _factory = factory ?? new V113CipherFactory();

    public WindowerDecodeResult DecodeRawStream(string ndjson)
    {
        var c2sChunks = new SortedDictionary<int, byte[]>();
        var s2cChunks = new SortedDictionary<int, byte[]>();

        foreach (var line in ndjson.Split('\n',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!line.StartsWith('{')) continue;
            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;

                if (!root.TryGetProperty("type", out var typeProp) ||
                    typeProp.GetString() != "chunk") continue;

                if (!root.TryGetProperty("ret", out var retProp) ||
                    retProp.GetInt32() <= 0) continue;

                if (!root.TryGetProperty("raw_hex", out var hexProp)) continue;
                string hex = hexProp.GetString() ?? "";
                if (hex.Length == 0) continue;

                int seq = root.GetProperty("seq").GetInt32();
                string dir = root.GetProperty("dir").GetString() ?? "";
                (dir == "c2s" ? c2sChunks : s2cChunks)[seq] = Convert.FromHexString(hex);
            }
            catch (JsonException) { continue; }
        }

        byte[] s2cStream = ConcatChunks(s2cChunks.Values);
        byte[] c2sStream = ConcatChunks(c2sChunks.Values);

        if (s2cStream.Length < 2)
            throw new InvalidDataException(
                "s2c 流不足：無法讀取 getHello payloadLen（至少需要 2 bytes）");

        int helloPayloadLen = s2cStream[0] | (s2cStream[1] << 8);
        int helloTotalLen = helloPayloadLen + 2;
        if (s2cStream.Length < helloTotalLen)
            throw new InvalidDataException(
                $"getHello 不完整（宣告 {helloTotalLen} bytes，實際只有 {s2cStream.Length}）");

        var (recvIv, sendIv) = HelloPacketParser.Parse(s2cStream.AsSpan(0, helloTotalLen));

        var (s2cFrames, s2cRem) = TcpStreamReframer.Reframe(s2cStream.AsSpan(helloTotalLen));
        var (c2sFrames, c2sRem) = TcpStreamReframer.Reframe(c2sStream.AsSpan());

        var decoded = new PacketStreamDecoder(_factory).DecodeClientToServer(recvIv, sendIv, c2sFrames);

        return new WindowerDecodeResult(
            RecvIv: recvIv,
            SendIv: sendIv,
            C2sPackets: decoded,
            S2cRawFrames: s2cFrames,
            C2sRemainder: c2sRem.Length > 0 ? c2sRem : null,
            S2cRemainder: s2cRem.Length > 0 ? s2cRem : null
        );
    }

    private static byte[] ConcatChunks(IEnumerable<byte[]> chunks)
    {
        using var ms = new MemoryStream();
        foreach (var c in chunks) ms.Write(c);
        return ms.ToArray();
    }
}

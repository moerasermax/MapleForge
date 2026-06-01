using Maple.Adapters.V113.Crypto;
using Maple.Tools.PacketDecoder;
using Maple.Versioning;

namespace Maple.Tools.PacketDecoder.Tests;

/// <summary>
/// DecodeRawStream + getHello IV parser 測試。
///
/// 用合成資料（真 V113 cipher 自行造加密流 → 切成不規則 TCP chunk → 組成 windower NDJSON）
/// 斷言 reframe + 解碼閉環，不依賴真實 windower 擷取。
///
/// 守則：
/// - c2s 可做位元級斷言（server 解密 log 有 ground truth）。
/// - s2c 僅斷言「reframe 機制正確」「不 crash」「封包數吻合」，
///   禁止斷言解密後語意（unverified，無 ground truth）。
/// </summary>
public class DecodeRawStreamTests
{
    private static readonly byte[] RecvIv = { 0x46, 0x72, 0x7A, 0x11 };
    private static readonly byte[] SendIv = { 0x52, 0x30, 0x78, 0x22 };
    private static readonly V113CipherFactory Factory = new();

    // ── getHello IV parser ────────────────────────────────────────────────────

    [Fact]
    public void HelloParser_extracts_recv_and_send_iv()
    {
        byte[] hello = BuildHello(RecvIv, SendIv);
        var (rv, sv) = HelloPacketParser.Parse(hello);
        Assert.Equal(RecvIv, rv);
        Assert.Equal(SendIv, sv);
    }

    [Fact]
    public void HelloParser_rejects_too_short_packet()
    {
        Assert.ThrowsAny<Exception>(() => HelloPacketParser.Parse(new byte[5]));
    }

    [Fact]
    public void HelloParser_rejects_truncated_iv()
    {
        // payloadLen=14 but raw only has 12 bytes → ivOffset+8 > raw.Length
        byte[] truncated = BuildHello(RecvIv, SendIv)[..12];
        Assert.ThrowsAny<Exception>(() => HelloPacketParser.Parse(truncated));
    }

    // ── TcpStreamReframer ─────────────────────────────────────────────────────

    [Fact]
    public void Reframer_exact_boundary_yields_all_complete()
    {
        var (enc, _) = Factory.CreateSessionPair(RecvIv, SendIv);
        byte[] frame0 = Frame(enc, new byte[] { 0x01, 0x00, 0xAA });
        byte[] frame1 = Frame(enc, new byte[] { 0x02, 0x00, 0xBB, 0xCC });
        byte[] stream = ConcatBytes(frame0, frame1);

        var (complete, remainder) = TcpStreamReframer.Reframe(stream);

        Assert.Equal(2, complete.Count);
        Assert.Empty(remainder);
        Assert.Equal(frame0, complete[0]);
        Assert.Equal(frame1, complete[1]);
    }

    [Fact]
    public void Reframer_incomplete_tail_goes_to_remainder()
    {
        var (enc, _) = Factory.CreateSessionPair(RecvIv, SendIv);
        byte[] frame = Frame(enc, new byte[] { 0x01, 0x00, 0xFF });
        byte[] stream = ConcatBytes(frame, frame[..2]); // 2nd packet cut mid-header

        var (complete, remainder) = TcpStreamReframer.Reframe(stream);

        Assert.Single(complete);
        Assert.Equal(2, remainder.Length);
    }

    [Fact]
    public void Reframer_empty_stream_yields_empty_and_empty_remainder()
    {
        var (complete, remainder) = TcpStreamReframer.Reframe(ReadOnlySpan<byte>.Empty);
        Assert.Empty(complete);
        Assert.Empty(remainder);
    }

    [Fact]
    public void Reframer_handles_irregular_tcp_splits()
    {
        // 模擬 TCP 分段：把三個完整封包的連續流，以不規則的大小分成多個 chunk，
        // 每次 Reframe(remainder + newChunk)，最終應還原出全部三個封包。
        var (enc, _) = Factory.CreateSessionPair(RecvIv, SendIv);
        var plains = new[]
        {
            new byte[] { 0x01, 0x00, 0xAA, 0xBB },
            new byte[] { 0x02, 0x00, 0xCC },
            new byte[] { 0x03, 0x00, 0xDD, 0xEE, 0xFF },
        };
        byte[] stream = ConcatAll(plains.Select(p => Frame(enc, p)));

        // 故意把 stream 切成不整齊大小（含跨 header/body 邊界）
        int[] splitSizes = { 3, 7, 2, 5, 999 };
        var allComplete = new List<byte[]>();
        byte[] remainder = Array.Empty<byte>();
        int pos = 0;

        foreach (int size in splitSizes)
        {
            int take = Math.Min(size, stream.Length - pos);
            if (take <= 0) break;

            byte[] chunk = ConcatBytes(remainder, stream[pos..(pos + take)]);
            pos += take;

            var (complete, rem) = TcpStreamReframer.Reframe(chunk);
            allComplete.AddRange(complete);
            remainder = rem;
        }

        Assert.Equal(3, allComplete.Count);
    }

    // ── WindowerNdjsonDecoder end-to-end ──────────────────────────────────────

    [Fact]
    public void DecodeRawStream_c2s_bitlevel_correct_with_tcp_fragmentation()
    {
        // 準備 cipher：recvCipher 模擬客戶端送出（= server recv cipher）
        var (recvCipher, sendCipher) = Factory.CreateSessionPair(RecvIv, SendIv);

        var plains = new[]
        {
            new byte[] { 0x09, 0x00, 0xDE, 0xAD },
            new byte[] { 0x17, 0x00, 0x00 },
            new byte[] { 0x35, 0x00, 0xBE, 0xEF, 0x01, 0x02 },
        };
        byte[] c2sStream = ConcatAll(plains.Select(p => Frame(recvCipher, p)));

        // 2 個 s2c 加密封包（unverified，只測 reframe 機制）
        byte[] s2cFrame1 = Frame(sendCipher, new byte[] { 0x0F, 0x00, 0x11, 0x22 });
        byte[] s2cFrame2 = Frame(sendCipher, new byte[] { 0x0F, 0x00, 0x33 });
        byte[] s2cStream = ConcatBytes(BuildHello(RecvIv, SendIv),
                                       ConcatBytes(s2cFrame1, s2cFrame2));

        string ndjson = BuildNdjson(
            s2cChunks: SplitAt(s2cStream, new[] { 5, 8 }),
            c2sChunks: SplitAt(c2sStream, new[] { 6, 4 }));

        var result = new WindowerNdjsonDecoder(Factory).DecodeRawStream(ndjson);

        // IV 抽取正確
        Assert.Equal(RecvIv, result.RecvIv);
        Assert.Equal(SendIv, result.SendIv);

        // c2s 位元級還原（ground truth）
        Assert.Equal(plains.Length, result.C2sPackets.Count);
        for (int i = 0; i < plains.Length; i++)
        {
            Assert.Equal(plains[i], result.C2sPackets[i].Payload);
            Assert.Equal("c2s", result.C2sPackets[i].Dir);
        }

        // s2c：僅斷言 reframe 機制（不斷言解密內容，unverified）
        Assert.Equal(2, result.S2cRawFrames.Count);

        // 完整消費完，無殘餘
        Assert.Null(result.C2sRemainder);
        Assert.Null(result.S2cRemainder);
    }

    [Fact]
    public void DecodeRawStream_incomplete_c2s_tail_goes_to_remainder()
    {
        var (recvCipher, _) = Factory.CreateSessionPair(RecvIv, SendIv);
        byte[] frame = Frame(recvCipher, new byte[] { 0x01, 0x00, 0xAA });
        byte[] c2sStream = ConcatBytes(frame, frame[..3]); // 最後一個封包截斷

        string ndjson = BuildNdjson(
            s2cChunks: new[] { BuildHello(RecvIv, SendIv) },
            c2sChunks: new[] { c2sStream });

        var result = new WindowerNdjsonDecoder(Factory).DecodeRawStream(ndjson);

        Assert.Single(result.C2sPackets);
        Assert.NotNull(result.C2sRemainder);
        Assert.Equal(3, result.C2sRemainder!.Length);
    }

    [Fact]
    public void DecodeRawStream_skips_chunks_with_negative_ret()
    {
        // ret=-1（WSA_IO_PENDING）的 chunk 應被忽略；只有 ret>0 的 chunk 進入解碼
        var (recvCipher, _) = Factory.CreateSessionPair(RecvIv, SendIv);
        byte[] hello = BuildHello(RecvIv, SendIv);
        byte[] goodC2s = Frame(recvCipher, new byte[] { 0x01, 0x00, 0xAA });

        string ndjson = string.Join("\n", new[]
        {
            "{\"type\":\"session\",\"pid\":1,\"socket\":100,\"ts_start\":0}",
            // 第一個 s2c chunk 失敗（ret=-1），應被跳過
            "{\"type\":\"chunk\",\"seq\":1,\"dir\":\"s2c\",\"ts\":0,\"raw_hex\":\"\",\"api\":\"recv\",\"ret\":-1}",
            // 第二個 s2c chunk 有效（含 getHello）
            $"{{\"type\":\"chunk\",\"seq\":2,\"dir\":\"s2c\",\"ts\":0,\"raw_hex\":\"{Convert.ToHexString(hello)}\",\"api\":\"recv\",\"ret\":{hello.Length}}}",
            // c2s chunk
            $"{{\"type\":\"chunk\",\"seq\":3,\"dir\":\"c2s\",\"ts\":0,\"raw_hex\":\"{Convert.ToHexString(goodC2s)}\",\"api\":\"send\",\"ret\":{goodC2s.Length}}}",
        });

        var result = new WindowerNdjsonDecoder(Factory).DecodeRawStream(ndjson);

        Assert.Equal(RecvIv, result.RecvIv);
        Assert.Single(result.C2sPackets);
        Assert.Equal(new byte[] { 0x01, 0x00, 0xAA }, result.C2sPackets[0].Payload);
    }

    [Fact]
    public void DecodeRawStream_hello_spanning_multiple_chunks()
    {
        // getHello 跨兩個 TCP chunk（前 6 bytes 一個 chunk，剩餘另一個）
        byte[] hello = BuildHello(RecvIv, SendIv);
        int split = 6;
        byte[] chunk1 = hello[..split];
        byte[] chunk2 = hello[split..];

        string ndjson = BuildNdjson(
            s2cChunks: new[] { chunk1, chunk2 },
            c2sChunks: Array.Empty<byte[]>());

        var result = new WindowerNdjsonDecoder(Factory).DecodeRawStream(ndjson);

        Assert.Equal(RecvIv, result.RecvIv);
        Assert.Equal(SendIv, result.SendIv);
        Assert.Empty(result.C2sPackets);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    /// <summary>把明文封裝成 [4-byte header][加密 body]（模擬客戶端用相同 cipher 送出）。</summary>
    private static byte[] Frame(IPacketCipher cipher, byte[] plain)
    {
        var framed = new byte[plain.Length + 4];
        cipher.WriteHeader(framed.AsSpan(0, 4), plain.Length);
        var body = (byte[])plain.Clone();
        cipher.Crypt(body);
        body.CopyTo(framed.AsSpan(4));
        return framed;
    }

    /// <summary>
    /// 手動建構 getHello bytes（反推自 V113LoginPackets.Hello 建構碼）：
    ///   [short payloadLen=14 LE][short version=113 LE][short patchLen=1 LE]['1'][recvIv 4B][sendIv 4B][byte locale=6]
    /// </summary>
    private static byte[] BuildHello(byte[] recvIv, byte[] sendIv)
    {
        // payload: version(2) + patchStrLen(2) + '1'(1) + recvIv(4) + sendIv(4) + locale(1) = 14 bytes
        byte[] payload = {
            0x71, 0x00,                                      // version = 113 LE
            0x01, 0x00,                                      // patchStrLen = 1 LE
            (byte)'1',                                       // patch
            recvIv[0], recvIv[1], recvIv[2], recvIv[3],
            sendIv[0], sendIv[1], sendIv[2], sendIv[3],
            0x06,                                            // locale = 6
        };
        // 前綴 payloadLen（2 bytes LE）
        return new byte[] { (byte)payload.Length, 0x00 }.Concat(payload).ToArray();
    }

    private static byte[] ConcatBytes(byte[] a, byte[] b)
    {
        var result = new byte[a.Length + b.Length];
        a.CopyTo(result, 0);
        b.CopyTo(result, a.Length);
        return result;
    }

    private static byte[] ConcatAll(IEnumerable<byte[]> parts)
    {
        using var ms = new MemoryStream();
        foreach (var p in parts) ms.Write(p);
        return ms.ToArray();
    }

    private static IEnumerable<byte[]> SplitAt(byte[] data, int[] sizes)
    {
        int pos = 0;
        foreach (int sz in sizes)
        {
            int take = Math.Min(sz, data.Length - pos);
            if (take <= 0) break;
            yield return data[pos..(pos + take)];
            pos += take;
        }
        if (pos < data.Length)
            yield return data[pos..];
    }

    // ── DecodeRawStream s2c 解密 ──────────────────────────────────────────────

    [Fact]
    public void DecodeRawStream_s2c_decoded_bitlevel_with_tcp_fragmentation()
    {
        // send cipher 模擬 server 送出，recv cipher 模擬 client 送出
        var (recvCipher, sendCipher) = Factory.CreateSessionPair(RecvIv, SendIv);

        var s2cPlains = new[]
        {
            new byte[] { 0x0B, 0x00, 0xAA, 0xBB },
            new byte[] { 0xC8, 0x00, 0x33, 0x44, 0x55 },
        };
        // s2c stream = getHello + 加密封包序列（以 send cipher 加密，模擬 server 送出）
        byte[] s2cStream = ConcatBytes(
            BuildHello(RecvIv, SendIv),
            ConcatAll(s2cPlains.Select(p => Frame(sendCipher, p))));

        // c2s 隨便一包（以 recv cipher 加密，模擬 client 送出）
        byte[] c2sFrame = Frame(recvCipher, new byte[] { 0x17, 0x00 });

        string ndjson = BuildNdjson(
            s2cChunks: SplitAt(s2cStream, new[] { 7, 11 }),
            c2sChunks: new[] { c2sFrame });

        var result = new WindowerNdjsonDecoder(Factory).DecodeRawStream(ndjson);

        // S2cPackets 合成 round-trip → 位元級可斷言（機制驗證）
        Assert.Equal(s2cPlains.Length, result.S2cPackets.Count);
        for (int i = 0; i < s2cPlains.Length; i++)
        {
            Assert.Equal(s2cPlains[i], result.S2cPackets[i].Payload);
            Assert.Equal("s2c", result.S2cPackets[i].Dir);
        }

        // S2cRawFrames 仍保留（相容）
        Assert.Equal(s2cPlains.Length, result.S2cRawFrames.Count);

        // c2s 解密不受影響
        Assert.Single(result.C2sPackets);
        Assert.Equal(new byte[] { 0x17, 0x00 }, result.C2sPackets[0].Payload);
    }

    private static string BuildNdjson(IEnumerable<byte[]> s2cChunks, IEnumerable<byte[]> c2sChunks)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{\"type\":\"session\",\"pid\":1,\"socket\":100,\"ts_start\":0}");

        int seq = 1;
        foreach (var chunk in s2cChunks)
        {
            if (chunk.Length == 0) continue;
            sb.AppendLine(
                $"{{\"type\":\"chunk\",\"seq\":{seq++},\"dir\":\"s2c\",\"ts\":0," +
                $"\"raw_hex\":\"{Convert.ToHexString(chunk)}\",\"api\":\"recv\",\"ret\":{chunk.Length}}}");
        }
        foreach (var chunk in c2sChunks)
        {
            if (chunk.Length == 0) continue;
            sb.AppendLine(
                $"{{\"type\":\"chunk\",\"seq\":{seq++},\"dir\":\"c2s\",\"ts\":0," +
                $"\"raw_hex\":\"{Convert.ToHexString(chunk)}\",\"api\":\"send\",\"ret\":{chunk.Length}}}");
        }
        return sb.ToString();
    }
}

using Maple.Adapters.V113.Crypto;
using Maple.Tools.PacketDecoder;
using Maple.Versioning;

namespace Maple.Tools.PacketDecoder.Tests;

/// <summary>
/// 封包擷取模式 MVP 第一刀的黃金測試：證明「擷取(加密原始流)+握手IV → 離線解碼 → 位元級還原」閉環。
/// 用真 V113 cipher（OFB 對稱，已 byte 級驗證）模擬 client→server 加密流，再用解碼器還原並逐 byte 斷言。
/// 不需真客戶端（純 client→server 方向、有確定性 ground truth）。
/// </summary>
public class DecoderGoldenTests
{
    private static readonly byte[] RecvIv = { 0x12, 0x34, 0x56, 0x78 };
    private static readonly byte[] SendIv = { 0x9A, 0xBC, 0xDE, 0xF0 };

    /// <summary>用「與 server recv 同 IV/版本」的 cipher 把明文封成 [4-byte header][加密 body]（模擬客戶端送出）。</summary>
    private static byte[] Frame(IPacketCipher enc, byte[] plain)
    {
        var framed = new byte[plain.Length + 4];
        enc.WriteHeader(framed.AsSpan(0, 4), plain.Length);  // WriteHeader 不推進 IV
        var body = (byte[])plain.Clone();
        enc.Crypt(body);                                      // Crypt 推進 IV
        body.CopyTo(framed.AsSpan(4));
        return framed;
    }

    [Fact]
    public void Decodes_multi_packet_stream_bitlevel()
    {
        var factory = new V113CipherFactory();
        var (encryptor, _) = factory.CreateSessionPair(RecvIv, SendIv);  // 模擬客戶端 send（= server recv 同參數）

        var plains = new[]
        {
            new byte[] { 0x01, 0x00, 0xAA, 0xBB },                       // opcode 0x0001
            new byte[] { 0x17, 0x00, 0x00, 0x00 },                       // opcode 0x0017（類心跳）
            new byte[] { 0x34, 0x00, 0xDE, 0xAD, 0xBE, 0xEF, 0x01 },     // opcode 0x0034
        };
        var framed = plains.Select(p => Frame(encryptor, p)).ToList();

        var decoded = new PacketStreamDecoder(factory).DecodeClientToServer(RecvIv, SendIv, framed);

        Assert.Equal(plains.Length, decoded.Count);
        for (int i = 0; i < plains.Length; i++)
        {
            Assert.Equal(plains[i], decoded[i].Payload);                                  // 位元級還原
            Assert.Equal((ushort)(plains[i][0] | (plains[i][1] << 8)), decoded[i].Opcode); // opcode 正確
            Assert.Equal(i, decoded[i].Seq);
        }
    }

    [Fact]
    public void Wrong_iv_is_detected_by_header_check()
    {
        var factory = new V113CipherFactory();
        var (encryptor, _) = factory.CreateSessionPair(RecvIv, SendIv);
        var framed = new List<byte[]> { Frame(encryptor, new byte[] { 0x01, 0x00, 0x05 }) };

        // 用錯的 IV 解 → header 驗證應失敗（證明 IV 失步可被早期偵測，不會默默解出垃圾）
        var wrongIv = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        Assert.ThrowsAny<Exception>(() =>
            new PacketStreamDecoder(factory).DecodeClientToServer(wrongIv, SendIv, framed));
    }

    [Fact]
    public void Truncated_packet_is_rejected()
    {
        var factory = new V113CipherFactory();
        var (encryptor, _) = factory.CreateSessionPair(RecvIv, SendIv);
        var full = Frame(encryptor, new byte[] { 0x01, 0x00, 0x05, 0x06 });
        var truncated = full[..(full.Length - 2)];  // 砍掉尾巴 2 byte → 長度不符

        Assert.ThrowsAny<Exception>(() =>
            new PacketStreamDecoder(factory).DecodeClientToServer(RecvIv, SendIv, new List<byte[]> { truncated }));
    }
}

using Maple.Adapters.V113.Crypto;
using Maple.Tools.PacketDecoder;
using Maple.Versioning;

namespace Maple.Tools.PacketDecoder.Tests;

/// <summary>
/// PacketStreamDecoder.DecodeServerToClient 合成 round-trip 測試。
///
/// 用 send cipher 自行加密一段 s2c 流（server 端視角），再用解碼器還原，位元級斷言。
/// 合成資料 = 機制驗證，可斷言位元級。
/// 真實 server s2c 無 ground truth → 一律標 unverified，禁止斷言解密後語意。
///
/// Cipher 方向：s2c 由 server send cipher 加密（sendIv）；windower 客戶端錄到加密 bytes；
/// DecodeServerToClient 用相同 send cipher 對稱解密（OFB）。
/// </summary>
public class S2cDecodeTests
{
    private static readonly byte[] RecvIv = { 0x12, 0x34, 0x56, 0x78 };
    private static readonly byte[] SendIv = { 0x9A, 0xBC, 0xDE, 0xF0 };

    /// <summary>用 cipher 把明文封成 [4-byte header][加密 body]（模擬 server 送出）。</summary>
    private static byte[] Frame(IPacketCipher cipher, byte[] plain)
    {
        var framed = new byte[plain.Length + 4];
        cipher.WriteHeader(framed.AsSpan(0, 4), plain.Length);
        var body = (byte[])plain.Clone();
        cipher.Crypt(body);
        body.CopyTo(framed.AsSpan(4));
        return framed;
    }

    [Fact]
    public void Decodes_s2c_multi_packet_stream_bitlevel()
    {
        var factory = new V113CipherFactory();
        var (_, send) = factory.CreateSessionPair(RecvIv, SendIv); // send = server 送出 cipher

        var plains = new[]
        {
            new byte[] { 0x0B, 0x00, 0xAA, 0xBB },              // opcode 0x000B
            new byte[] { 0xC8, 0x00, 0x00, 0x00 },              // opcode 0x00C8
            new byte[] { 0x5A, 0x02, 0xDE, 0xAD, 0xBE, 0xEF },  // opcode 0x025A
        };
        var framed = plains.Select(p => Frame(send, p)).ToList();

        var decoded = new PacketStreamDecoder(factory).DecodeServerToClient(RecvIv, SendIv, framed);

        Assert.Equal(plains.Length, decoded.Count);
        for (int i = 0; i < plains.Length; i++)
        {
            Assert.Equal(plains[i], decoded[i].Payload);                                    // 位元級還原
            Assert.Equal((ushort)(plains[i][0] | (plains[i][1] << 8)), decoded[i].Opcode); // opcode 正確
            Assert.Equal("s2c", decoded[i].Dir);
            Assert.Equal(i, decoded[i].Seq);
        }
    }

    [Fact]
    public void S2c_wrong_send_iv_is_detected_by_header_check()
    {
        var factory = new V113CipherFactory();
        var (_, send) = factory.CreateSessionPair(RecvIv, SendIv);
        var framed = new List<byte[]> { Frame(send, new byte[] { 0x01, 0x00, 0x05 }) };

        // 用錯的 sendIv → header 驗證失敗（IV 失步早期偵測，不會默默解出垃圾）
        var wrongIv = new byte[] { 0x00, 0x00, 0x00, 0x00 };
        Assert.ThrowsAny<Exception>(() =>
            new PacketStreamDecoder(factory).DecodeServerToClient(RecvIv, wrongIv, framed));
    }

    [Fact]
    public void S2c_truncated_packet_is_rejected()
    {
        var factory = new V113CipherFactory();
        var (_, send) = factory.CreateSessionPair(RecvIv, SendIv);
        var full = Frame(send, new byte[] { 0x01, 0x00, 0x05, 0x06 });
        var truncated = full[..(full.Length - 2)]; // 砍尾 2 byte → 長度不符

        Assert.ThrowsAny<Exception>(() =>
            new PacketStreamDecoder(factory).DecodeServerToClient(RecvIv, SendIv, new List<byte[]> { truncated }));
    }
}

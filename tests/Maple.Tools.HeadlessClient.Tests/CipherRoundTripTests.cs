using Maple.Adapters.V113.Crypto;
using Maple.Versioning;

namespace Maple.Tools.HeadlessClient.Tests;

/// <summary>
/// cipher round-trip 離線測試：不需要 live server。
/// 驗證 MapleAesOfb（透過 IPacketCipher 介面）的對稱加解密與 header framing 正確性。
/// </summary>
public class CipherRoundTripTests
{
    // 固定 IV（模擬 getHello 取得的值）
    private static readonly byte[] RecvIv = [0x46, 0x72, 0x7A, 0x52];
    private static readonly byte[] SendIv = [0x52, 0x30, 0x78, 0x12];

    private static (IPacketCipher Recv, IPacketCipher Send) MakePair()
        => new V113CipherFactory().CreateSessionPair(RecvIv, SendIv);

    // ── AES-OFB 對稱性 ────────────────────────────────────────────────────────

    [Fact]
    public void Crypt_IsSymmetric_SameIvEncryptThenDecryptGivesOriginal()
    {
        // 兩個使用相同初始 IV 的 cipher，第一個加密、第二個解密，結果等於原始
        var (c1, _) = MakePair();
        var (c2, _) = MakePair();

        byte[] original = [0x01, 0x00, 0x08, 0x00, 0x74, 0x65, 0x73, 0x74, 0x75, 0x73, 0x65, 0x72];
        byte[] data = (byte[])original.Clone();

        c1.Crypt(data); // encrypt
        c2.Crypt(data); // decrypt（AES-OFB 對稱）

        Assert.Equal(original, data);
    }

    [Fact]
    public void Crypt_EncryptedDataDiffersFromPlaintext()
    {
        var (cipher, _) = MakePair();
        byte[] data = [0x01, 0x00, 0x08, 0x00, 0x74, 0x65, 0x73, 0x74, 0x75, 0x73, 0x65, 0x72];
        byte[] original = (byte[])data.Clone();

        cipher.Crypt(data);

        Assert.NotEqual(original, data); // 確認加密後確實不同（非 no-op）
    }

    // ── Header framing ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    [InlineData(100)]
    [InlineData(0x1234)]
    public void WriteHeader_ReadLength_RoundTrip(int expectedLen)
    {
        var (recv, _) = MakePair();
        var header = new byte[4];
        recv.WriteHeader(header, expectedLen);
        Assert.Equal(expectedLen, recv.ReadLength(header));
    }

    [Fact]
    public void WriteHeader_Check_PassesWithSameCipherState()
    {
        // 同 IV、同 version 的兩個 cipher：一個 WriteHeader，另一個 Check 應通過
        var (writer, _) = MakePair();
        var (checker, _) = MakePair();

        var header = new byte[4];
        writer.WriteHeader(header, 42);

        Assert.True(checker.Check(header));
    }

    [Fact]
    public void Check_FailsAfterIvDrift()
    {
        // 如果 cipher 已 Crypt 過（IV 推進），用舊 header 做 Check 應失敗
        var (c1, _) = MakePair();
        var (c2, _) = MakePair();

        var header = new byte[4];
        c1.WriteHeader(header, 10);

        // 讓 c2 的 IV 推進一次
        byte[] dummy = new byte[10];
        c2.Crypt(dummy);

        Assert.False(c2.Check(header)); // IV 已偏移，header 不再吻合
    }

    // ── c2s 完整 frame round-trip ─────────────────────────────────────────────

    [Fact]
    public void FullFrame_ClientSendsServerReceives_RoundTrip()
    {
        // 模擬：client 用 recv cipher 加密，server 用相同初始狀態的 recv cipher 解密
        var (clientRecv, _) = MakePair();
        var (serverRecv, _) = MakePair(); // 同樣 recvIv 起始

        byte[] payload = [0x01, 0x00, 0x08, 0x00, 0x74, 0x65, 0x73, 0x74, 0x75, 0x73, 0x65, 0x72];

        // Client 送出
        var body = (byte[])payload.Clone();
        var header = new byte[4];
        clientRecv.WriteHeader(header, body.Length);
        clientRecv.Crypt(body);

        // Server 收到
        Assert.True(serverRecv.Check(header));
        int decodedLen = serverRecv.ReadLength(header);
        serverRecv.Crypt(body); // decrypt

        Assert.Equal(payload.Length, decodedLen);
        Assert.Equal(payload, body);
    }

    [Fact]
    public void FullFrame_MultiplePackets_IvStaysInSync()
    {
        // 連送三個封包，兩端 IV 應持續同步
        var (c1, _) = MakePair();
        var (c2, _) = MakePair();

        for (int i = 0; i < 3; i++)
        {
            byte[] payload = [0x00, 0x00, (byte)i, 0x01, 0x02, 0x03, 0x04, 0x05];

            var body = (byte[])payload.Clone();
            var header = new byte[4];
            c1.WriteHeader(header, body.Length);
            c1.Crypt(body);

            Assert.True(c2.Check(header), $"封包 #{i} Check 失敗（IV desync）");
            c2.Crypt(body);
            Assert.Equal(payload, body);
        }
    }
}

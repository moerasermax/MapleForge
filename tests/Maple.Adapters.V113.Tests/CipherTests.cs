using Maple.Adapters.V113.Crypto;
using Maple.Versioning;

namespace Maple.Adapters.V113.Tests;

public class CipherTests
{
    private static readonly byte[] SampleIv = { 0x52, 0x30, 0x78, 0x14 };

    private static MapleAesOfb New(short version = 113, byte[]? iv = null)
        => new(iv ?? (byte[])SampleIv.Clone(), version);

    [Theory]
    [InlineData(2)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(0x5AF)]   // 一塊邊界前
    [InlineData(0x5B0)]   // 剛好第一塊長度
    [InlineData(0x5B1)]   // 跨第一塊邊界
    [InlineData(5000)]    // 多塊
    public void Crypt_RoundTrip_RecoversPlaintext(int size)
    {
        var enc = New();
        var dec = New(); // 相同 iv + version → 相同 keystream

        var original = new byte[size];
        for (int i = 0; i < size; i++) original[i] = (byte)(i * 7 + 3);
        var buffer = (byte[])original.Clone();

        enc.Crypt(buffer);
        Assert.False(buffer.AsSpan().SequenceEqual(original), "加密後不應等於原文");

        dec.Crypt(buffer);
        Assert.True(buffer.AsSpan().SequenceEqual(original), "解密後應還原原文");
    }

    [Fact]
    public void Crypt_SequentialPackets_IvEvolvesConsistently()
    {
        // 連續多個封包：兩側 IV 必須以相同方式演化（驗證 getNewIv/funnyShit）。
        var enc = New();
        var dec = New();

        for (int p = 0; p < 8; p++)
        {
            var original = new byte[20 + p * 13];
            for (int i = 0; i < original.Length; i++) original[i] = (byte)(p * 31 + i);
            var buffer = (byte[])original.Clone();

            enc.Crypt(buffer);
            dec.Crypt(buffer);

            Assert.True(buffer.AsSpan().SequenceEqual(original), $"第 {p} 個封包未能還原（IV 演化不一致）");
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(100)]
    [InlineData(0x5B4)]
    [InlineData(0xFFFF)]
    public void Header_WriteThenRead_RecoversLength(int length)
    {
        var cipher = New();
        Span<byte> header = stackalloc byte[4];
        cipher.WriteHeader(header, length);

        int recovered = cipher.ReadLength(header);
        Assert.Equal(length, recovered);
    }

    [Fact]
    public void Header_SelfCheck_Passes()
    {
        // 用送出 cipher 產生的頭，應通過同一 cipher 的 Check（header ^ iv == version）。
        var cipher = New(version: unchecked((short)(0xFFFF - 113)));
        Span<byte> header = stackalloc byte[4];
        cipher.WriteHeader(header, 123);

        Assert.True(cipher.Check(header));
    }

    [Fact]
    public void Factory_ProducesTwoIndependentCiphers()
    {
        var factory = new V113CipherFactory();
        byte[] recvIv = { 1, 2, 3, 4 };
        byte[] sendIv = { 9, 8, 7, 6 };

        var (recv, send) = factory.CreateSessionPair(recvIv, sendIv);

        Assert.NotNull(recv);
        Assert.NotNull(send);
        Assert.NotSame(recv, send);
    }
}

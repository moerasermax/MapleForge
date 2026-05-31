using Maple.Adapters.V113.Crypto;

namespace Maple.Adapters.V113.Tests;

/// <summary>
/// L2 黃金真值測試：斷言 C# cipher 與「舊 Java 預言機」(tools.MapleAESOFB) 逐 byte 相同。
/// 向量由 tools/oracle/GoldenVectors.java 對固定輸入 iv=52307814 產生。
/// 舊 Java 當年能與真客戶端通訊 → 通過此測試 ≡ 與真客戶端 bit 級相容。
/// </summary>
public class GoldenVectorTests
{
    private static readonly byte[] Iv = { 0x52, 0x30, 0x78, 0x14 };

    private static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b);

    [Fact]
    public void NextIv_MatchesJavaOracle()
    {
        Assert.Equal("45833C71", Hex(MapleAesOfb.ComputeNextIv(Iv)));
    }

    [Fact]
    public void RecvHeader_MatchesJavaOracle()
    {
        var c = new MapleAesOfb(Iv, 113);
        Span<byte> h = stackalloc byte[4];

        c.WriteHeader(h, 20);
        Assert.Equal("09141D14", Hex(h));

        var c2 = new MapleAesOfb(Iv, 113);
        c2.WriteHeader(h, 1460);
        Assert.Equal("0914BD11", Hex(h));
    }

    [Fact]
    public void SendHeader_VersionSwapped_MatchesJavaOracle()
    {
        var s = new MapleAesOfb(Iv, unchecked((short)(0xFFFF - 113)));
        Span<byte> h = stackalloc byte[4];
        s.WriteHeader(h, 20);
        Assert.Equal("F6EBE2EB", Hex(h));
    }

    [Fact]
    public void Crypt32_MatchesJavaOracle()
    {
        var c = new MapleAesOfb(Iv, 113);
        var data = new byte[32];
        for (int i = 0; i < 32; i++) data[i] = (byte)i;

        c.Crypt(data);

        Assert.Equal("F1A7761627CB0C7DF850B550257E9C046C7F069E84D112219B9ED80764B57B12", Hex(data));
        Assert.Equal("45833C71", Hex(c.CurrentIv));
    }

    [Fact]
    public void CryptAcrossBlockBoundary_MatchesJavaOracle()
    {
        const int n = 0x5B1; // 1457，跨第一塊 0x5B0 邊界
        var c = new MapleAesOfb(Iv, 113);
        var data = new byte[n];
        for (int i = 0; i < n; i++) data[i] = (byte)((i * 7 + 3) & 0xFF);

        c.Crypt(data);

        Assert.Equal("F2AC650D3CE8274ECB1BF60B7E2DF767", Hex(data.AsSpan(0, 16)));
        Assert.Equal("6312107A7D274FC69A8DE43714F74822", Hex(data.AsSpan(n - 16, 16)));
        Assert.Equal("45833C71", Hex(c.CurrentIv));
    }
}

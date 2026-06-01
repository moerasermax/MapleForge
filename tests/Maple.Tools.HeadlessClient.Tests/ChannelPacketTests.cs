using Maple.Tools.HeadlessClient;

namespace Maple.Tools.HeadlessClient.Tests;

/// <summary>
/// 頻道協定相關封包與 s2c 解析的離線測試。
/// Ground truth: V113ChannelConnectionHandler.HandlePlayerLoggedInAsync（charId = reader.ReadInt()）
///               V113LoginConnectionHandler.HandleCharSelectAsync（charId = reader.ReadInt()）
///               V113LoginPackets.CharList / ServerIp 建構碼。
/// </summary>
public class ChannelPacketTests
{
    // ── C2S 封包建構 ─────────────────────────────────────────────────────────

    [Fact]
    public void SelectChar_PacketBytes_Correct()
    {
        // Ground truth: V113RecvOp.CharSelect = 0x06，handler: reader.ReadInt() → charId
        // 格式：[short 0x0006 LE][int charId LE]
        byte[] packet = C2S.CharSelect(42);
        Assert.Equal([0x06, 0x00, 0x2A, 0x00, 0x00, 0x00], packet);
    }

    [Fact]
    public void SelectChar_TotalLength_Is6Bytes()
    {
        Assert.Equal(6, C2S.CharSelect(1).Length);
    }

    [Fact]
    public void PlayerLoggedIn_PacketBytes_Correct()
    {
        // Ground truth: V113ChannelRecvOp.PlayerLoggedIn = 0x07，handler: reader.ReadInt() → charId
        // 格式：[short 0x0007 LE][int charId LE]
        byte[] packet = C2S.PlayerLoggedIn(42);
        Assert.Equal([0x07, 0x00, 0x2A, 0x00, 0x00, 0x00], packet);
    }

    [Fact]
    public void PlayerLoggedIn_TotalLength_Is6Bytes()
    {
        Assert.Equal(6, C2S.PlayerLoggedIn(1).Length);
    }

    [Fact]
    public void PlayerLoggedIn_CharIdIsLittleEndian()
    {
        // charId = 0x01020304 LE → bytes [04, 03, 02, 01]
        byte[] packet = C2S.PlayerLoggedIn(0x01020304);
        Assert.Equal(0x07, packet[0]);
        Assert.Equal(0x00, packet[1]);
        Assert.Equal(0x04, packet[2]);
        Assert.Equal(0x03, packet[3]);
        Assert.Equal(0x02, packet[4]);
        Assert.Equal(0x01, packet[5]);
    }

    // ── S2CReader: ParseFirstCharId ──────────────────────────────────────────
    //
    // CharList layout（V113LoginPackets.CharList 建構碼推導）：
    //   [0..1] opcode 0x0003 LE
    //   [2]    byte 0
    //   [3..6] int 1000000 LE (= 0x000F4240)
    //   [7]    byte count
    //   [8..11] first charId LE  (WriteCharStats 第一欄)

    [Fact]
    public void ParseFirstCharId_OneCharacter_ReturnsCharId()
    {
        var payload = BuildCharListPayload(count: 1, firstCharId: 42);
        Assert.Equal(42, S2CReader.ParseFirstCharId(payload));
    }

    [Fact]
    public void ParseFirstCharId_ZeroChars_ReturnsZero()
    {
        var payload = BuildCharListPayload(count: 0, firstCharId: 0);
        Assert.Equal(0, S2CReader.ParseFirstCharId(payload));
    }

    [Fact]
    public void ParseFirstCharId_TooShort_ReturnsZero()
    {
        Assert.Equal(0, S2CReader.ParseFirstCharId(new byte[8]));
        Assert.Equal(0, S2CReader.ParseFirstCharId(new byte[11]));
    }

    [Fact]
    public void ParseFirstCharId_LargeCharId_Correct()
    {
        // charId 1000001 = 0x000F4241
        var payload = BuildCharListPayload(count: 1, firstCharId: 1_000_001);
        Assert.Equal(1_000_001, S2CReader.ParseFirstCharId(payload));
    }

    // ── S2CReader: ParseChannelAddress ───────────────────────────────────────
    //
    // ServerIp layout（V113LoginPackets.ServerIp 建構碼推導）：
    //   [0..1]  opcode 0x0004 LE
    //   [2..3]  short 0
    //   [4..7]  4 bytes ip
    //   [8..9]  short port LE
    //   [10..13] int charId LE
    //   [14..18] 5 zero bytes

    [Fact]
    public void ParseChannelAddress_ParsesLocalhostAndPort8585()
    {
        byte[] payload = BuildServerIpPayload(
            ip: [127, 0, 0, 1], port: 8585, charId: 42);

        var (ip, port) = S2CReader.ParseChannelAddress(payload);

        Assert.Equal("127.0.0.1", ip);
        Assert.Equal(8585, port);
    }

    [Fact]
    public void ParseChannelAddress_TooShort_Throws()
    {
        Assert.Throws<InvalidDataException>(
            () => S2CReader.ParseChannelAddress(new byte[9]));
    }

    [Fact]
    public void ParseChannelAddress_ArbIpAndPort_Correct()
    {
        byte[] payload = BuildServerIpPayload(
            ip: [192, 168, 1, 10], port: 9999, charId: 0);

        var (ip, port) = S2CReader.ParseChannelAddress(payload);

        Assert.Equal("192.168.1.10", ip);
        Assert.Equal(9999, port);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>建立最小 CharList payload（只需前 12 bytes 供 ParseFirstCharId 測試）。</summary>
    private static byte[] BuildCharListPayload(byte count, int firstCharId)
    {
        var buf = new byte[12];
        // opcode 0x0003 LE
        buf[0] = 0x03; buf[1] = 0x00;
        // byte 0
        buf[2] = 0x00;
        // int 1000000 LE = 0x000F4240
        buf[3] = 0x40; buf[4] = 0x42; buf[5] = 0x0F; buf[6] = 0x00;
        // count
        buf[7] = count;
        // first charId LE
        buf[8]  = (byte)(firstCharId & 0xFF);
        buf[9]  = (byte)((firstCharId >> 8)  & 0xFF);
        buf[10] = (byte)((firstCharId >> 16) & 0xFF);
        buf[11] = (byte)((firstCharId >> 24) & 0xFF);
        return buf;
    }

    /// <summary>建立 ServerIp payload（符合 V113LoginPackets.ServerIp 格式，共 19 bytes）。</summary>
    private static byte[] BuildServerIpPayload(byte[] ip, int port, int charId)
    {
        var buf = new byte[19];
        // opcode 0x0004 LE
        buf[0] = 0x04; buf[1] = 0x00;
        // short 0
        buf[2] = 0x00; buf[3] = 0x00;
        // ip
        buf[4] = ip[0]; buf[5] = ip[1]; buf[6] = ip[2]; buf[7] = ip[3];
        // port LE
        buf[8] = (byte)(port & 0xFF);
        buf[9] = (byte)((port >> 8) & 0xFF);
        // charId LE
        buf[10] = (byte)(charId & 0xFF);
        buf[11] = (byte)((charId >> 8)  & 0xFF);
        buf[12] = (byte)((charId >> 16) & 0xFF);
        buf[13] = (byte)((charId >> 24) & 0xFF);
        // 5 zero bytes
        return buf;
    }
}

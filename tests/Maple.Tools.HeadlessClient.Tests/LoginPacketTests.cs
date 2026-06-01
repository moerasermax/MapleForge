using Maple.Core.IO;

namespace Maple.Tools.HeadlessClient.Tests;

/// <summary>
/// 登入封包位元組驗證（離線，無需 live server）。
///
/// Ground truth（windower 擷取 + 解碼）：
///   c2s 0x01 payload = 0100 | 0800 "testuser" | 0800 "test1234" | machineCodeTail (22 bytes)
///
/// 測試對象是 PacketWriter（WriteMapleString / WriteShort / WriteBytes），
/// 也就是 C2S.Login() 底層呼叫的完整 API 鏈。
/// </summary>
public class LoginPacketTests
{
    // 22-byte 機器碼尾段（直接 replay，私服不驗硬體）
    private static readonly byte[] MachineTail =
        Convert.FromHexString("D8BBC18E37BEEE4A365E00000000AD7A000000000200");

    // Ground truth 全包（44 bytes）
    private static readonly byte[] GroundTruth =
        Convert.FromHexString(
            "0100"                                   + // opcode 0x0001 LE
            "0800" + "7465737475736572"              + // WriteMapleString("testuser"): len16 LE + bytes
            "0800" + "7465737431323334"              + // WriteMapleString("test1234"): len16 LE + bytes
            "D8BBC18E37BEEE4A365E00000000AD7A000000000200"); // machine tail

    // ── 封包結構 ─────────────────────────────────────────────────────────────

    [Fact]
    public void LoginPacket_BuiltWithPacketWriter_MatchesGroundTruth()
    {
        byte[] packet = new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString("testuser")
            .WriteMapleString("test1234")
            .WriteBytes(MachineTail)
            .ToArray();

        Assert.Equal(GroundTruth, packet);
    }

    [Fact]
    public void LoginPacket_TotalLength_Is44Bytes()
    {
        // 2 (opcode) + (2+8) account + (2+8) password + 22 machine tail = 44
        byte[] packet = new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString("testuser")
            .WriteMapleString("test1234")
            .WriteBytes(MachineTail)
            .ToArray();

        Assert.Equal(44, packet.Length);
    }

    [Fact]
    public void LoginPacket_Opcode_Is0x0001_LittleEndian()
    {
        byte[] packet = new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString("testuser")
            .WriteMapleString("test1234")
            .WriteBytes(MachineTail)
            .ToArray();

        Assert.Equal(0x01, packet[0]);
        Assert.Equal(0x00, packet[1]);
    }

    // ── WriteMapleString 位元組佈局 ──────────────────────────────────────────

    [Fact]
    public void WriteMapleString_AccountName_HasCorrectLengthPrefix()
    {
        // [2..3] = length 8 LE
        byte[] packet = new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString("testuser")
            .WriteMapleString("test1234")
            .WriteBytes(MachineTail)
            .ToArray();

        Assert.Equal(0x08, packet[2]); // len low byte
        Assert.Equal(0x00, packet[3]); // len high byte
    }

    [Fact]
    public void WriteMapleString_AccountName_BytesMatchAscii()
    {
        // [4..11] = "testuser" ASCII
        byte[] packet = new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString("testuser")
            .WriteMapleString("test1234")
            .WriteBytes(MachineTail)
            .ToArray();

        byte[] expectedBytes = "testuser"u8.ToArray();
        Assert.Equal(expectedBytes, packet[4..12]);
    }

    // ── MachineTail 佈局 ─────────────────────────────────────────────────────

    [Fact]
    public void LoginPacket_MachineTail_IsAt_Offset22()
    {
        // offset: 2 (opcode) + 2+8 (account) + 2+8 (password) = 22
        byte[] packet = new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString("testuser")
            .WriteMapleString("test1234")
            .WriteBytes(MachineTail)
            .ToArray();

        Assert.Equal(MachineTail, packet[22..]);
    }

    // ── Pong opcode ──────────────────────────────────────────────────────────

    [Fact]
    public void PongPacket_Is_0x0E_LittleEndian()
    {
        // V113RecvOp.Pong = 0x0E（登入伺服器 switch case 確認）
        // 注意：channel server Pong = 0x17（與 windower 擷取記錄不同 server）
        byte[] pong = new PacketWriter(2).WriteShort(0x000E).ToArray();
        Assert.Equal([0x0E, 0x00], pong);
    }
}

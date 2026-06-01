using Maple.Core.IO;

namespace Maple.Tools.HeadlessClient;

/// <summary>c2s 封包建構器（登入伺服器流程）。</summary>
public static class C2S
{
    // V113RecvOp.Pong = 0x0E（對照 V113LoginConnectionHandler switch case）
    // 注意：windower 擷取到的 0x17 為頻道伺服器 Pong，登入伺服器 Pong 是 0x0E。
    private const ushort PongOpcode = 0x000E;

    /// <summary>
    /// 登入請求（RecvOp 0x01）：
    ///   [short 0x0001][WriteMapleString(account)][WriteMapleString(password)][machineTail]
    /// </summary>
    public static byte[] Login(string account, string password, byte[] machineTail)
        => new PacketWriter()
            .WriteShort(0x0001)
            .WriteMapleString(account)
            .WriteMapleString(password)
            .WriteBytes(machineTail)
            .ToArray();

    /// <summary>Pong（RecvOp 0x0E）：回應 server Ping（SendOp 0x09）。</summary>
    public static byte[] Pong()
        => new PacketWriter(2).WriteShort(PongOpcode).ToArray();

    /// <summary>世界列表請求（RecvOp 0x03）。</summary>
    public static byte[] ServerlistRequest()
        => new PacketWriter(2).WriteShort(0x0003).ToArray();

    /// <summary>
    /// 角色列表請求（RecvOp 0x04）：
    ///   [short 0x0004][byte unknown=0][byte worldId][byte channelIndex]
    /// </summary>
    public static byte[] CharlistRequest(byte worldId = 0, byte channelIndex = 0)
        => new PacketWriter(5)
            .WriteShort(0x0004)
            .WriteByte(0)
            .WriteByte(worldId)
            .WriteByte(channelIndex)
            .ToArray();

    /// <summary>角色選擇（RecvOp 0x06）：[short 0x0006][int charId]。</summary>
    public static byte[] CharSelect(int charId)
        => new PacketWriter(6).WriteShort(0x0006).WriteInt(charId).ToArray();

    /// <summary>伺服器狀態查詢（RecvOp 0x18）：補取 s2c ServerStatus（0x16）。</summary>
    public static byte[] ServerStatusRequest()
        => new PacketWriter(2).WriteShort(0x0018).ToArray();

    // ── Channel Server (port 8585) c2s ────────────────────────────────────────

    /// <summary>
    /// PlayerLoggedIn（Channel RecvOp 0x07）：[short 0x0007][int charId]。
    /// 進頻道握手後第一個封包，server 讀 charId → 載入角色 → 送 SetField。
    /// </summary>
    public static byte[] PlayerLoggedIn(int charId)
        => new PacketWriter(6).WriteShort(0x0007).WriteInt(charId).ToArray();
}

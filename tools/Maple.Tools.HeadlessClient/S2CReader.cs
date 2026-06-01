namespace Maple.Tools.HeadlessClient;

/// <summary>
/// s2c 封包解析輔助（純函式，不需 cipher 狀態）。
/// 所有 layout 均對照 V113LoginPackets / V113ChannelPackets 建構碼推導。
/// </summary>
public static class S2CReader
{
    // ── CharList (s2c 0x03) ──────────────────────────────────────────────────
    //
    //  [0..1]  short opcode 0x0003 LE
    //  [2]     byte 0
    //  [3..6]  int 1000000 LE         (固定 magic，來源：V113LoginPackets.CharList)
    //  [7]     byte count             (角色數量)
    //  [8..11] int charId LE          (第一個角色 id，WriteCharStats 首欄)
    //  ...     後續 CharStats / CharLook 略

    /// <summary>
    /// 從 CharList payload 解出第一個角色的 charId。
    /// 無角色（count == 0）或格式不足時回傳 0。
    /// </summary>
    public static int ParseFirstCharId(byte[] payload)
    {
        if (payload.Length < 12) return 0;
        int count = payload[7];
        if (count == 0) return 0;
        return payload[8] | (payload[9] << 8) | (payload[10] << 16) | (payload[11] << 24);
    }

    // ── ServerIp (s2c 0x04) ──────────────────────────────────────────────────
    //
    //  [0..1]  short opcode 0x0004 LE
    //  [2..3]  short 0 LE
    //  [4..7]  4 bytes ip
    //  [8..9]  short port LE
    //  [10..13] int charId LE
    //  [14..18] 5 zero bytes
    //
    //  來源：V113LoginPackets.ServerIp() 建構碼

    /// <summary>
    /// 從 ServerIp payload 解出頻道伺服器位址。
    /// </summary>
    public static (string Ip, int Port) ParseChannelAddress(byte[] payload)
    {
        if (payload.Length < 10)
            throw new InvalidDataException(
                $"ServerIp 封包過短：{payload.Length} bytes（需 ≥10）");

        string ip   = $"{payload[4]}.{payload[5]}.{payload[6]}.{payload[7]}";
        int    port = payload[8] | (payload[9] << 8);
        return (ip, port);
    }
}

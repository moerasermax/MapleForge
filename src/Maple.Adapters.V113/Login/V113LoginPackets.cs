using Maple.Adapters.V113.Crypto;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Login;

/// <summary>v113 登入相關封包建構（對照舊 LoginPacket / MaplePacketCreator）。</summary>
internal static class V113LoginPackets
{
    /// <summary>
    /// getHello（未加密握手）：<c>[short payloadLen][short version][maple"1"][recvIv 4][sendIv 4][byte locale=6]</c>。
    /// 開頭 short 是「之後位元組數」的長度前綴（舊碼硬寫 14 即 patch="1" 時的 payload 長度）。
    /// </summary>
    public static byte[] Hello(ReadOnlySpan<byte> recvIv, ReadOnlySpan<byte> sendIv)
    {
        var payload = new PacketWriter(16)
            .WriteShort(V113CryptoConstants.MapleVersion) // 113
            .WriteMapleString("1")                        // patch
            .WriteBytes(recvIv)
            .WriteBytes(sendIv)
            .WriteByte(6)                                 // locale
            .ToArray();

        return new PacketWriter(payload.Length + 2)
            .WriteShort(payload.Length)
            .WriteBytes(payload)
            .ToArray();
    }

    /// <summary>getLoginFailed：<c>[short LOGIN_STATUS=0][byte reason][short 0]</c>。reason 4=密碼錯誤, 5=未註冊, 3=封鎖。</summary>
    public static byte[] LoginFailed(int reason)
        => new PacketWriter(8)
            .WriteShort(V113SendOp.LoginStatus)
            .WriteByte(reason)
            .WriteShort(0)
            .ToArray();

    /// <summary>
    /// getAuthSuccess（登入成功）：對照舊 <c>LoginPacket.getAuthSuccessRequest</c>。
    /// 送出後客戶端離開登入畫面、進入選伺服器流程。
    /// </summary>
    public static byte[] AuthSuccess(int accountId, string accountName, bool isGm = false)
        => new PacketWriter(32)
            .WriteShort(V113SendOp.LoginStatus)
            .WriteByte(0)                  // type（0=成功）
            .WriteInt(accountId)
            .WriteByte(0)                  // gender（0=男；尚未設定可後續處理）
            .WriteByte(isGm ? 1 : 0)       // admin byte
            .WriteByte(0)
            .WriteInt(0)
            .WriteMapleString(accountName)
            .WriteInt(0)
            .WriteByte(0)
            .WriteByte(0)
            .WriteByte(0)                  // !canTalk（0=可說話）
            .WriteLong(0)                  // 禁言期限
            .WriteByte(0)
            .WriteLong(0)
            .ToArray();

    /// <summary>
    /// getServerList：世界 + 頻道清單（對照舊 LoginPacket.getServerList）。
    /// worldId 0=Aquilla, 1=Bootes, 2=Cass, 3=Delphinus；私服通常只有一個世界。
    /// </summary>
    public static byte[] ServerList(string worldName, int channelCount, int worldId = 0)
    {
        var w = new PacketWriter(64)
            .WriteShort(V113SendOp.Serverlist)
            .WriteByte(worldId)
            .WriteMapleString(worldName)
            .WriteByte(0)           // flag：0=normal, 1=hot, 2=new
            .WriteMapleString("")   // event message
            .WriteShort(100)        // max load x2（原碼兩個 100）
            .WriteShort(100)
            .WriteByte(channelCount);

        for (int i = 1; i <= channelCount; i++)
        {
            w.WriteMapleString($"{worldName}-{i}")
             .WriteInt(0)           // load（玩家數）
             .WriteByte(worldId)
             .WriteShort(i - 1);   // channel index（0-based）
        }

        w.WriteShort(0); // balloon count（廣告板，私服通常 0）
        return w.ToArray();
    }

    /// <summary>getEndOfServerList：0xFF 結束標記（對照舊 LoginPacket.getEndOfServerList）。</summary>
    public static byte[] EndOfServerList()
        => new PacketWriter(3)
            .WriteShort(V113SendOp.Serverlist)
            .WriteByte(0xFF)
            .ToArray();

    /// <summary>getServerStatus：0=正常, 1=高度填滿, 2=爆滿（對照舊 LoginPacket.getServerStatus）。</summary>
    public static byte[] ServerStatus(int status = 0)
        => new PacketWriter(4)
            .WriteShort(V113SendOp.Serverstatus)
            .WriteShort(status)
            .ToArray();

    /// <summary>
    /// getCharList（M2-4 stub）：0 個角色，讓客戶端進角色選單。
    /// 真實角色列表在 M2-5 實作（需 CharStats + CharLook 序列化）。
    /// </summary>
    public static byte[] CharList(int charSlots = 3)
        => new PacketWriter(10)
            .WriteShort(V113SendOp.Charlist)
            .WriteByte(0)
            .WriteInt(1000000)  // 對照 Java: 40 42 0F 00（固定值）
            .WriteByte(0)       // 角色數（0）
            .WriteShort(3)      // second password request（3=不需要）
            .WriteInt(charSlots)
            .ToArray();
}

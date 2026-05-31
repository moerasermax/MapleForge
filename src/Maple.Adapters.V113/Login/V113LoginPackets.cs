using Maple.Adapters.V113.Crypto;
using Maple.Core.IO;

namespace Maple.Adapters.V113.Login;

/// <summary>v113 登入相關封包建構（對照舊 <c>LoginPacket</c>）。</summary>
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
}

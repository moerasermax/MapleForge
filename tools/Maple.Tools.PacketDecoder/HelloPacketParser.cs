namespace Maple.Tools.PacketDecoder;

/// <summary>
/// 解析 v113 getHello（未加密握手封包），抽取 recvIv / sendIv。
///
/// 布局反推依據：<c>Maple.Adapters.V113.Login.V113LoginPackets.Hello()</c> 建構碼：
///   outer:  [short payloadLen LE]          — 之後所有位元組的長度
///   inner:  [short version=113 LE]
///           [short patchStrLen LE][patchStr bytes]
///           [recvIv  4 bytes]
///           [sendIv  4 bytes]
///           [byte   locale=6]
///
/// patch="1" 時 payloadLen=14，整個封包共 16 bytes。
/// </summary>
public static class HelloPacketParser
{
    /// <summary>
    /// 從未加密的 getHello 封包抽出 (recvIv, sendIv)。
    /// </summary>
    /// <param name="raw">完整的 getHello bytes（包含 2-byte payloadLen 前綴）。</param>
    public static (byte[] RecvIv, byte[] SendIv) Parse(ReadOnlySpan<byte> raw)
    {
        if (raw.Length < 14)
            throw new InvalidDataException(
                $"getHello 過短（{raw.Length} bytes，需 ≥14）");

        // [0..1]: payloadLen（小端）
        int payloadLen = raw[0] | (raw[1] << 8);
        if (raw.Length < payloadLen + 2)
            throw new InvalidDataException(
                $"getHello 不完整：payloadLen={payloadLen}，實際有 {raw.Length - 2} bytes");

        // [4..5]: patchStrLen（小端）
        int patchStrLen = raw[4] | (raw[5] << 8);
        int ivOffset = 6 + patchStrLen;

        if (ivOffset + 8 > raw.Length)
            throw new InvalidDataException(
                $"getHello patchStrLen={patchStrLen} 超出封包邊界（raw.Length={raw.Length}）");

        return (raw.Slice(ivOffset, 4).ToArray(), raw.Slice(ivOffset + 4, 4).ToArray());
    }
}

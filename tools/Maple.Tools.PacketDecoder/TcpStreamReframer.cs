using Maple.Adapters.V113.Crypto;
using Maple.Versioning;

namespace Maple.Tools.PacketDecoder;

/// <summary>
/// 把連續的 TCP 位元組流 reframe 成 MapleStory framed 封包邊界：
/// 每個封包 = [4-byte header][body]，與 <see cref="PacketStreamDecoder.DecodeClientToServer"/> 格式一致。
///
/// 不完整的尾段（buffer 尚未湊滿一個封包）回傳為 Remainder，不丟棄，
/// 讓呼叫端累積後重試（應對 TCP 分段）。
/// </summary>
public static class TcpStreamReframer
{
    // ReadLength 是 IV 無關的純運算（IPacketCipher 文件已確認）；任意 IV 的 cipher 均可使用。
    private static readonly IPacketCipher _lengthReader =
        new V113CipherFactory().CreateSessionPair(new byte[4], new byte[4]).Recv;

    private const int MaxBodySize = 0xFFFF;

    /// <summary>
    /// 從連續 TCP 流切出完整 framed 封包，尾端不完整封包放進 Remainder。
    /// </summary>
    /// <param name="stream">累積的原始 TCP bytes（不含 getHello 的握手部分）。</param>
    public static (IReadOnlyList<byte[]> Complete, byte[] Remainder) Reframe(ReadOnlySpan<byte> stream)
    {
        var result = new List<byte[]>();
        int pos = 0;

        while (pos + 4 <= stream.Length)
        {
            int len = _lengthReader.ReadLength(stream.Slice(pos, 4));

            if (len < 0 || len > MaxBodySize)
                throw new InvalidDataException(
                    $"reframe：無效封包長度 {len}（offset {pos}），可能是 IV 失步或 TCP 流錯位");

            if (pos + 4 + len > stream.Length)
                break; // 尾段不完整，交給 Remainder

            result.Add(stream.Slice(pos, 4 + len).ToArray());
            pos += 4 + len;
        }

        return (result, stream.Slice(pos).ToArray());
    }
}

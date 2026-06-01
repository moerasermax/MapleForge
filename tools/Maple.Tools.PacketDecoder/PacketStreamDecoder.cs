using Maple.Adapters.V113.Crypto;
using Maple.Versioning;

namespace Maple.Tools.PacketDecoder;

/// <summary>一個解碼後的封包：序號、方向、opcode（body 前 2 byte，小端）、明文 payload。</summary>
public sealed record DecodedPacket(int Seq, string Dir, ushort Opcode, byte[] Payload);

/// <summary>
/// 離線封包流解碼器（封包擷取模式核心）。
/// 輸入＝擷取到的「加密原始流（每包 = [4-byte header][加密 body]）+ 握手 IV」；
/// 用已 byte 級驗證的 V113 cipher 依序解密，重建明文。對應 <c>MapleSession.RunAsync</c> 的接收邏輯。
///
/// 設計守則（docs/封包擷取模式-設計.md）：
/// - 重用 <see cref="V113CipherFactory"/>（不複製 cipher、不把解碼搬進 server）。
/// - cipher 是 stream 順序、IV 每包遞進 → 必須從握手 IV 起、依序解；漏/亂序會 header 驗證失敗（早期偵測）。
/// - 純函式、無 I/O、可單測（CipherState 由 factory 每次 new，不共用可變狀態）。
/// </summary>
public sealed class PacketStreamDecoder
{
    private readonly IVersionCipherFactory _factory;

    public PacketStreamDecoder(IVersionCipherFactory? factory = null)
        => _factory = factory ?? new V113CipherFactory();

    /// <summary>
    /// 解碼 client→server 方向（server 用 recv cipher 解密）。
    /// </summary>
    /// <param name="recvIv">握手取得的 recv 方向 IV（server 接收 client 的方向）。</param>
    /// <param name="sendIv">握手取得的 send 方向 IV（建立 cipher pair 需要，本方向不直接用）。</param>
    /// <param name="framedPackets">依序的封包，每個 = [4-byte header][加密 body]，與 winsock 擷取一致。</param>
    public IReadOnlyList<DecodedPacket> DecodeClientToServer(
        ReadOnlySpan<byte> recvIv, ReadOnlySpan<byte> sendIv, IReadOnlyList<byte[]> framedPackets)
    {
        var (recv, _) = _factory.CreateSessionPair(recvIv, sendIv);
        var result = new List<DecodedPacket>(framedPackets.Count);

        for (int i = 0; i < framedPackets.Count; i++)
        {
            var framed = framedPackets[i];
            if (framed.Length < 4)
                throw new InvalidDataException($"封包 #{i} 過短（{framed.Length} byte，需 ≥4 byte header）");

            var header = framed.AsSpan(0, 4);
            if (!recv.Check(header))
                throw new InvalidDataException($"封包 #{i} header 驗證失敗：IV 失步/漏包/版本不符（cipher 是 stream 順序，前面少一個 byte 後面全錯）");

            int len = recv.ReadLength(header);
            int actual = framed.Length - 4;
            if (len != actual)
                throw new InvalidDataException($"封包 #{i} 長度不符：header 宣告 {len}，實際 body {actual}");

            var body = framed[4..];   // 複製一份再解，不動原始擷取
            recv.Crypt(body);

            ushort opcode = body.Length >= 2 ? (ushort)(body[0] | (body[1] << 8)) : (ushort)0;
            result.Add(new DecodedPacket(i, "c2s", opcode, body));
        }

        return result;
    }
}

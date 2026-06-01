using Maple.Tools.PacketDecoder;

namespace Maple.Tools.PacketDecoder.Tests;

/// <summary>
/// 封包擷取模式・第二刀的終極驗證：用「真客戶端」擷取到的封包做位元級雙軌對照。
///
/// 資料來源：2026-06-01 用 server 端擷取（MAPLEFORGE_CAPTURE=1）錄到的真 v113 客戶端 Pong(0x17)。
/// fixture：fixtures/real_client_pong.ndjson。
///
/// 證明：把真客戶端送來的「加密原始 bytes + 握手 IV」餵進離線解碼器，
/// 重現的明文 == server 內建 cipher 當場解出的明文（dec 欄位，ground truth）。
/// 這就是「擷取 → 離線解密 → 位元級斷言」閉環在真實資料上的成立。
/// </summary>
public class RealClientCaptureTests
{
    // ── 真客戶端擷取（capture_20260601_054012, 127.0.0.1:51638）──
    private static readonly byte[] RecvIv = Convert.FromHexString("46727ac4");
    private static readonly byte[] SendIv = Convert.FromHexString("52307891");
    private static readonly byte[] FramedPong = Convert.FromHexString("0bc409c46a74"); // [4-byte header][加密 body]
    private static readonly byte[] ServerDecrypted = Convert.FromHexString("1700");     // server 當場解出（ground truth）

    [Fact]
    public void Offline_decode_matches_server_decrypt_bitlevel()
    {
        var decoded = new PacketStreamDecoder()
            .DecodeClientToServer(RecvIv, SendIv, new[] { FramedPong });

        Assert.Single(decoded);
        // 位元級：離線解密 == server 內建解密
        Assert.Equal(ServerDecrypted, decoded[0].Payload);
        // opcode 0x0017 = Pong（真客戶端對 server PING 的回應）
        Assert.Equal((ushort)0x0017, decoded[0].Opcode);
    }
}

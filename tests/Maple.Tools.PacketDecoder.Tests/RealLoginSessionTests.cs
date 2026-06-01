using System.Text;
using Maple.Tools.PacketDecoder;

namespace Maple.Tools.PacketDecoder.Tests;

/// <summary>
/// [3] 真實登入 session 端到端解碼（2026-06-01，windower 擷取真客戶端 + 真人鍵盤登入成功）。
/// fixture：fixtures/real_login_session_20260601.ndjson。
///
/// c2s 方向有 ground truth：server log 確認「✓ 登入成功 account='testuser'」，且解出的登入封包
/// 內含明文 testuser/test1234，與認證一致 → 位元級驗證整條鏈（擷取→reframe→DecodeClientToServer）
/// 在真實多位元組加密封包上正確。
/// s2c 方向標 unverified（無獨立 ground truth，僅斷言機制與 opcode，不斷言欄位語意）。
/// </summary>
public class RealLoginSessionTests
{
    private const string Cap =
        "{\"type\":\"session\",\"pid\":9688,\"socket\":2284,\"ts_start\":42150796}\n" +
        "{\"type\":\"chunk\",\"seq\":1,\"dir\":\"s2c\",\"ts\":1,\"raw_hex\":\"0e00\",\"api\":\"recv\",\"ret\":2}\n" +
        "{\"type\":\"chunk\",\"seq\":2,\"dir\":\"s2c\",\"ts\":1,\"raw_hex\":\"710001003146727a70523078f106\",\"api\":\"recv\",\"ret\":14}\n" +
        "{\"type\":\"chunk\",\"seq\":3,\"dir\":\"c2s\",\"ts\":1,\"raw_hex\":\"0b700970db2b\",\"api\":\"send\",\"ret\":6}\n" +
        "{\"type\":\"chunk\",\"seq\":4,\"dir\":\"c2s\",\"ts\":1,\"raw_hex\":\"310a1d0a3e4f67ec905b9e0c7efa05ad7ceb82e873f8ae6bdf3b92fb97ff459d7c249f7107ee49c75a127bb5ace05e22\",\"api\":\"send\",\"ret\":48}\n" +
        "{\"type\":\"chunk\",\"seq\":5,\"dir\":\"s2c\",\"ts\":1,\"raw_hex\":\"f60ec60e79f01f3a21cf03f58f20af5962ac765ae16b4de64467a5d6b7ef79902e27e09f430ef442b9e4375c6df5100667ad2056\",\"api\":\"recv\",\"ret\":52}\n";

    [Fact]
    public void Decodes_real_login_session_c2s_ground_truth()
    {
        var r = new WindowerNdjsonDecoder().DecodeRawStream(Cap);

        Assert.Equal("46727A70", Convert.ToHexString(r.RecvIv));
        Assert.Equal("523078F1", Convert.ToHexString(r.SendIv));

        // c2s：seq0 Pong(0x17)，seq1 登入請求(0x01)
        Assert.Equal(2, r.C2sPackets.Count);
        Assert.Equal(0x17, r.C2sPackets[0].Opcode);

        var login = r.C2sPackets[1];
        Assert.Equal(0x01, login.Opcode);
        // ground truth：登入封包內含明文帳號/密碼（與 server「✓ 登入成功 account='testuser'」一致）
        var ascii = Encoding.ASCII.GetString(login.Payload);
        Assert.Contains("testuser", ascii);
        Assert.Contains("test1234", ascii);
    }

    [Fact]
    public void Decodes_real_login_session_s2c_mechanism_unverified()
    {
        var r = new WindowerNdjsonDecoder().DecodeRawStream(Cap);

        // s2c 登入回應解出 1 包（unverified：僅斷言機制與 opcode，不斷言欄位語意）
        Assert.Single(r.S2cPackets);
        Assert.Equal(0x00, r.S2cPackets[0].Opcode);
        // 解密正確的強佐證：回應內嵌帳號字串（與 c2s 同 session cipher，c2s 已位元級驗證）
        Assert.Contains("testuser", Encoding.ASCII.GetString(r.S2cPackets[0].Payload));
    }
}

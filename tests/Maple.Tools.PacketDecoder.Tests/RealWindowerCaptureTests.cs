using Maple.Tools.PacketDecoder;

namespace Maple.Tools.PacketDecoder.Tests;

/// <summary>
/// 封包擷取模式的終極驗證：用「真實 windower 客戶端側擷取」做端到端解碼。
///
/// 資料來源：2026-06-01，windower hook ws2_32 注入真 v113 客戶端登入握手錄到（recv I/O 風暴修復後首次成功）。
/// fixture：fixtures/real_client_windower_login.ndjson。內嵌於此以自包含、不依賴檔案複製。
///
/// 證明整條鏈在「客戶端側真實擷取」上跑通：
///   windower NDJSON → 重組 s2c 流 → 解析 getHello 抽 recv/send IV → reframe c2s → 用真 cipher 解密。
/// </summary>
public class RealWindowerCaptureTests
{
    // 真實擷取（PID 25260, socket 1252）：
    //   s2c recv 0e00            = getHello payloadLen=14（未加密長度前綴）
    //   s2c recv 710001003146727a5b5230780306 = getHello payload
    //            7100=版本113, 010031=patch"1", 46727a5b=recvIv, 52307803=sendIv, 06=locale
    //   c2s send 0b5b095be46c    = 客戶端第一個加密封包（4-byte header + 2-byte body）
    private const string RealCapture =
        "{\"type\":\"session\",\"pid\":25260,\"socket\":1252,\"ts_start\":33278187}\n" +
        "{\"type\":\"chunk\",\"seq\":1,\"dir\":\"s2c\",\"ts\":33278187,\"raw_hex\":\"0e00\",\"api\":\"recv\",\"ret\":2}\n" +
        "{\"type\":\"chunk\",\"seq\":2,\"dir\":\"s2c\",\"ts\":33278187,\"raw_hex\":\"710001003146727a5b5230780306\",\"api\":\"recv\",\"ret\":14}\n" +
        "{\"type\":\"chunk\",\"seq\":3,\"dir\":\"c2s\",\"ts\":33292656,\"raw_hex\":\"0b5b095be46c\",\"api\":\"send\",\"ret\":6}\n";

    [Fact]
    public void Decodes_real_windower_client_capture_end_to_end()
    {
        var result = new WindowerNdjsonDecoder().DecodeRawStream(RealCapture);

        // getHello IV 抽取（位元級 ground truth：bytes 直接在未加密握手裡）
        Assert.Equal("46727a5b", Convert.ToHexString(result.RecvIv).ToLowerInvariant());
        Assert.Equal("52307803", Convert.ToHexString(result.SendIv).ToLowerInvariant());

        // c2s 解密：客戶端第一個送出封包，2-byte body → opcode 0x17（Pong，對照 server log「opcode=0x17 len=2」）
        Assert.Single(result.C2sPackets);
        Assert.Equal(0x17, result.C2sPackets[0].Opcode);
    }
}

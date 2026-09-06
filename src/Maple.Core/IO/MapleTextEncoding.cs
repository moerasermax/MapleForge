using System.Text;

namespace Maple.Core.IO;

/// <summary>
/// 封包字串編碼：對照舊 Java <c>ServerConstants.MapleType.台灣</c>（<c>"BIG5-HKSCS"</c>，繁體中文私服）。
/// .NET 無原生 BIG5-HKSCS，改用最接近且與 ASCII 相容的 code page 950（Big5）：
/// 0x00–0x7F 與 ASCII 逐 byte 相同，故既有純 ASCII 封包 fixture 不受影響；繁體中文則正確編解碼
/// （取代舊版逐字元截斷成 byte 的作法，該作法對任何非 Latin-1 字元皆會產生亂碼）。
/// </summary>
public static class MapleTextEncoding
{
    public static readonly Encoding Value = CreateBig5();

    private static Encoding CreateBig5()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(950);
    }
}

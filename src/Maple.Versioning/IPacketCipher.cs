namespace Maple.Versioning;

/// <summary>
/// 有狀態的「單方向」封包加密（v113 = AES-OFB）。
/// 每個連線、每個方向各持有一個實例（IV 會隨每次 Crypt 演化）。
/// 見 v113-protocol-spec.md 第 1 節。
/// </summary>
public interface IPacketCipher
{
    /// <summary>OFB 轉換：加密與解密相同（對稱）。就地處理 <paramref name="data"/> 並推進 IV。</summary>
    void Crypt(Span<byte> data);

    /// <summary>送出側：用「當前 IV」產生 4-byte 封包頭（不推進 IV；推進由隨後的 Crypt 完成）。</summary>
    void WriteHeader(Span<byte> destFour, int length);

    /// <summary>接收側：從 4-byte 頭取出 payload 長度（與 IV 無關的折疊運算）。</summary>
    int ReadLength(ReadOnlySpan<byte> header);

    /// <summary>接收側：驗證封包頭是否符合當前 IV/版本（防止解密錯位）。</summary>
    bool Check(ReadOnlySpan<byte> header);
}

/// <summary>
/// 依 review must-fix #3：用工廠強制「每連線每方向各一個 cipher 實例」，
/// 消滅「方向共用 cipher」的實作空間。
/// </summary>
public interface IVersionCipherFactory
{
    /// <summary>由伺服器產生的兩個 4-byte IV 建立一對 (接收, 送出) cipher。</summary>
    (IPacketCipher Recv, IPacketCipher Send) CreateSessionPair(ReadOnlySpan<byte> recvIv, ReadOnlySpan<byte> sendIv);
}

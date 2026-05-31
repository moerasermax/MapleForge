namespace Maple.Core.Data;

/// <summary>
/// 遊戲資料讀取抽象（WZ / XML / 其他格式無關）。
/// 核心層只透過此介面取資料，不知道底層格式。
/// 快取所有權：process 級唯讀單例（WZ 檔案不在運行時變動）；
/// 每 WzFileName 第一次呼叫時開啟並快取，後續直接回傳快取根節點。
/// </summary>
public interface IDataProvider
{
    /// <summary>
    /// 取得指定 WZ 檔案的根目錄（如 "Mob"、"Map"、"String"）。
    /// fileName 不含 .wz 副檔名。
    /// </summary>
    IDataNode GetRoot(string fileName);

    /// <summary>
    /// 依路徑取得節點（如 "0100100.img/info/maxHP"）。
    /// 路徑分隔符號為 '/'，找不到時回傳 null。
    /// </summary>
    IDataNode? GetAt(string fileName, string path);
}

/// <summary>
/// 遊戲資料樹狀節點（目錄、Image、Property 統一介面）。
/// 核心層只用此介面，不依賴 WzObject 具體類型。
/// </summary>
public interface IDataNode
{
    string Name { get; }

    /// <summary>子節點（可能為空）。</summary>
    IReadOnlyDictionary<string, IDataNode> Children { get; }

    /// <summary>
    /// 節點的葉值（int、float、string、WzVector…）；
    /// 目錄與 Image 節點回傳 null。
    /// </summary>
    object? Value { get; }

    /// <summary>依名稱取得子節點；找不到時回傳 null。</summary>
    IDataNode? this[string name] { get; }
}

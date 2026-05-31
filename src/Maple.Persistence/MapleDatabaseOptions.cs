namespace Maple.Persistence;

/// <summary>
/// 每個伺服器實例的 LiteDB 連線設定。
/// 對應 §4.4「每實例一個 .db 檔」原則：隔離、備份 = 複製檔案。
/// </summary>
public sealed class MapleDatabaseOptions
{
    /// <summary>資料目錄（絕對或相對路徑）。預設為 "data"。</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>實例名稱，用於產生 .db 檔名。預設為 "default"。</summary>
    public string InstanceName { get; set; } = "default";

    /// <summary>
    /// 計算後的資料庫完整路徑（<see cref="DataDirectory"/>/<see cref="InstanceName"/>.db）。
    /// </summary>
    public string DatabasePath => Path.Combine(DataDirectory, $"{InstanceName}.db");
}

namespace Maple.Persistence;

/// <summary>
/// 每個伺服器實例的持久層連線設定。
/// </summary>
public sealed class MapleDatabaseOptions
{
    /// <summary>持久層 provider。預設為 MongoDB。</summary>
    public MapleDatabaseProvider Provider { get; set; } = MapleDatabaseProvider.MongoDb;

    /// <summary>MongoDB connection string。正式環境需提供可連線的 mongod。</summary>
    public string MongoConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>MongoDB database 名稱；空值時以 InstanceName 產生 mapleforge_{InstanceName}。</summary>
    public string MongoDatabaseName { get; set; } = string.Empty;

    /// <summary>資料目錄（絕對或相對路徑）。預設為 "data"。</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>實例名稱，用於產生 .db 檔名。預設為 "default"。</summary>
    public string InstanceName { get; set; } = "default";

    /// <summary>
    /// 計算後的資料庫完整路徑（<see cref="DataDirectory"/>/<see cref="InstanceName"/>.db）。
    /// </summary>
    public string DatabasePath => Path.Combine(DataDirectory, $"{InstanceName}.db");

    /// <summary>MongoDB 實際 database 名稱。</summary>
    public string EffectiveMongoDatabaseName =>
        string.IsNullOrWhiteSpace(MongoDatabaseName) ? $"mapleforge_{InstanceName}" : MongoDatabaseName;
}

namespace Maple.Persistence.Tests;

/// <summary>
/// 需要 EphemeralMongo 下載/啟動 mongod 的測試（P105）。設定環境變數 <c>MAPLEFORGE_SKIP_MONGO=1</c>
/// 時 Skip——雲端 session 的網路政策擋 <c>downloads.mongodb.org</c>/<c>fastdl.mongodb.org</c>。
/// </summary>
public sealed class MongoFactAttribute : FactAttribute
{
    public const string SkipEnvironmentVariable = "MAPLEFORGE_SKIP_MONGO";

    public MongoFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(SkipEnvironmentVariable) == "1")
        {
            Skip = $"{SkipEnvironmentVariable}=1：略過需下載 mongod 的測試";
        }
    }
}

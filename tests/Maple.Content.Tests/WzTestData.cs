namespace Maple.Content.Tests;

/// <summary>
/// 真實 v113 client WZ 目錄（P105）：預設為原開發機路徑，可用環境變數 <c>MAPLEFORGE_WZ_DIR</c> 覆寫；
/// 找不到 <c>String.wz</c> 時，標記 <see cref="WzFactAttribute"/> 的測試自動 Skip（雲端/CI 無 WZ 檔）。
/// </summary>
internal static class WzTestData
{
    public const string EnvironmentVariable = "MAPLEFORGE_WZ_DIR";
    private const string DefaultDirectory = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client";

    public static string Directory { get; } =
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } dir ? dir : DefaultDirectory;

    public static bool Available => File.Exists(Path.Combine(Directory, "String.wz"));
}

/// <summary>需要真實 WZ 檔的測試；WZ 不存在時自動 Skip（見 <see cref="WzTestData"/>）。</summary>
public sealed class WzFactAttribute : FactAttribute
{
    public WzFactAttribute()
    {
        if (!WzTestData.Available)
        {
            Skip = $"找不到 WZ 目錄（設定 {WzTestData.EnvironmentVariable}）：{WzTestData.Directory}";
        }
    }
}

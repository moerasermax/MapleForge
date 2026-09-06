namespace Maple.Host.Shared.Configuration;

/// <summary>
/// 一個伺服器實例的設定（取代舊 settings.ini）。
/// M0 為單一實例；多實例（instances[]）在 M5-6 正式啟用。
/// </summary>
public sealed class ServerInstanceOptions
{
    public const string SectionName = "Instance";

    /// <summary>實例顯示名稱，例如「爸爸的楓之谷」。也用於 log 標記。</summary>
    public string Name { get; set; } = "MapleForge";

    /// <summary>楓之谷版本號（v113）。版本抽象的依據，見設計書 §2。</summary>
    public int Version { get; set; } = 113;

    public string ListenIp { get; set; } = "0.0.0.0";

    public int LoginPort { get; set; } = 8484;

    /// <summary>頻道伺服器 IP（告知客戶端連哪裡；localhost 私服用 127.0.0.1）。</summary>
    public string ChannelIp { get; set; } = "127.0.0.1";

    /// <summary>頻道伺服器監聽 port（對照舊 8585）。</summary>
    public int ChannelPort { get; set; } = 8585;

    /// <summary>LiteDB 資料目錄；每實例一個 `<Name>.db` 檔（見設計書 §4.4 文件模型 + LiteDB）。</summary>
    public string DataDirectory { get; set; } = "data";

    /// <summary>
    /// WZ 遊戲資料目錄（含 Map.wz/Mob.wz/String.wz 等，對照 v113_Client 目錄）。
    /// 2026-08-23 隨工作區重組搬到 E:（見 <c>MapleStory_docs/_已搬移.md</c>），未受版控追蹤。
    /// </summary>
    public string WzDirectory { get; set; } = @"E:\WorkSpace_離線資料\02_遊戲素材_game-assets\MapleStory\v113_Client";

    /// <summary>
    /// NPC/任務腳本根目錄（其下 npc/{npcId}.js）。指向舊 Java server（<c>MapleStory_Server</c>，
    /// 工作區重組前叫 <c>TestMapleStoryV113_Server</c>）的 scripts；之後可搬入 repo。
    /// </summary>
    public string ScriptsDirectory { get; set; } = @"D:\WorkSpace\03_研究_research\Private Game Server\MapleStory\MapleStory_Server\scripts";

    /// <summary>帳號不存在時自動建帳（對照舊 settings.ini autoRegister；私服常開）。</summary>
    public bool AutoRegister { get; set; } = true;

    public ServerRates Rates { get; set; } = new();
}

public sealed class ServerRates
{
    public int Exp { get; set; } = 1;
    public int Drop { get; set; } = 1;
    public int Meso { get; set; } = 1;
}

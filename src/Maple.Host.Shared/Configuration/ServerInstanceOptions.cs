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

    public string Database { get; set; } = "maple_v113";

    public ServerRates Rates { get; set; } = new();
}

public sealed class ServerRates
{
    public int Exp { get; set; } = 1;
    public int Drop { get; set; } = 1;
    public int Meso { get; set; } = 1;
}

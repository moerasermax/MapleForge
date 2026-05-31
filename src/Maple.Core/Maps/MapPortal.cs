namespace Maple.Core.Maps;

/// <summary>
/// 地圖傳送點（對照舊 MaplePortal）。
/// pt=0: 出生點(spawn), pt=1: 隱形入口, pt=2: 可見入口, pt=6: 傳送機, pt=7: 腳本入口, pt=10: 隱藏傳送。
/// </summary>
public sealed class MapPortal
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Type { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int TargetMapId { get; init; }
    public string TargetPortalName { get; init; } = string.Empty;
    public string Script { get; init; } = string.Empty;

    /// <summary>是否為出生點（type=0, name="sp"）。</summary>
    public bool IsSpawnPoint => Type == 0 && Name == "sp";
}

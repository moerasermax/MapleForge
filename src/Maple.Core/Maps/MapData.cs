namespace Maple.Core.Maps;

/// <summary>
/// 地圖完整資料（一次載入、不可變）。
/// 對照舊 MapleMap（去掉運行時狀態，只留靜態地圖定義）。
/// </summary>
public sealed class MapData
{
    public int MapId { get; init; }
    public string Name { get; init; } = string.Empty;
    public int ReturnMapId { get; init; }
    public bool Town { get; init; }

    public IReadOnlyList<MapPortal> Portals { get; init; } = Array.Empty<MapPortal>();
    public IReadOnlyList<MapFoothold> Footholds { get; init; } = Array.Empty<MapFoothold>();

    /// <summary>地圖靜態 NPC（從 WZ life 節點載入）。</summary>
    public IReadOnlyList<MapNpc> Npcs { get; init; } = Array.Empty<MapNpc>();

    /// <summary>依序號取得出生點；找不到時回最近的出生點或 null。</summary>
    public MapPortal? GetSpawnPoint(byte spawnPoint)
    {
        var spawns = Portals.Where(p => p.IsSpawnPoint).ToList();
        if (spawns.Count == 0) return null;
        return spawnPoint < spawns.Count ? spawns[spawnPoint] : spawns[0];
    }
}

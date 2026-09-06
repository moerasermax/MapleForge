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

    /// <summary>對照 Java <c>MapleMap.fieldLimit</c>（WZ <c>info/fieldLimit</c>）：位元旗標限制此圖能否用
    /// 跳躍/移動技能/召喚袋/秘密門/傳送石等，見 <see cref="FieldLimitType"/>。預設 0（無限制）。</summary>
    public long FieldLimit { get; init; }

    public IReadOnlyList<MapPortal> Portals { get; init; } = Array.Empty<MapPortal>();
    public IReadOnlyList<MapFoothold> Footholds { get; init; } = Array.Empty<MapFoothold>();

    /// <summary>地圖靜態 NPC（從 WZ life 節點載入）。</summary>
    public IReadOnlyList<MapNpc> Npcs { get; init; } = Array.Empty<MapNpc>();

    /// <summary>地圖靜態怪物出生點（從 WZ life 節點載入）。</summary>
    public IReadOnlyList<MapMonster> Monsters { get; init; } = Array.Empty<MapMonster>();

    /// <summary>依序號取得出生點；找不到時回最近的出生點或 null。</summary>
    public MapPortal? GetSpawnPoint(byte spawnPoint)
    {
        var spawns = Portals.Where(p => p.IsSpawnPoint).ToList();
        if (spawns.Count == 0) return null;
        return spawnPoint < spawns.Count ? spawns[spawnPoint] : spawns[0];
    }

    /// <summary>
    /// 對照 Java <c>MapleMap.findClosestSpawnpoint(Point)</c>：取離指定座標最近的出生點
    /// （歐氏距離平方比較，避免開根號）；沒有出生點時回 null。
    /// </summary>
    public MapPortal? GetClosestSpawnPoint(int x, int y)
    {
        MapPortal? closest = null;
        long closestDistanceSquared = long.MaxValue;
        foreach (var portal in Portals)
        {
            if (!portal.IsSpawnPoint) continue;

            long dx = portal.X - x;
            long dy = portal.Y - y;
            var distanceSquared = (dx * dx) + (dy * dy);
            if (distanceSquared < closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closest = portal;
            }
        }

        return closest;
    }
}

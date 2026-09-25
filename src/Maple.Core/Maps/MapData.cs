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

    /// <summary>P100：對照 Java <c>MapleMap.everlast</c>（WZ <c>info/everlast</c> &gt; 0）：玩家丟的掉落物不過期、不轉 FFA，
    /// 且只有主人能撿。</summary>
    public bool Everlast { get; init; }

    /// <summary>對照 Java <c>MapleMap.decHP</c>（WZ <c>info/decHP</c>，預設 0）：每個扣血週期扣多少 HP（寒冷/高溫/水中地圖）。</summary>
    public int DecHp { get; init; }

    /// <summary>對照 Java <c>MapleMap.decHPInterval</c>（WZ <c>info/decHPInterval</c>，預設 10000 毫秒）。</summary>
    public int DecHpInterval { get; init; } = 10_000;

    /// <summary>對照 Java <c>MapleMap.protectItem</c>（WZ <c>info/protectItem</c>，預設 0）：穿著此裝備可免扣血。</summary>
    public int ProtectItem { get; init; }

    public IReadOnlyList<MapPortal> Portals { get; init; } = Array.Empty<MapPortal>();
    public IReadOnlyList<MapFoothold> Footholds { get; init; } = Array.Empty<MapFoothold>();

    /// <summary>地圖靜態 NPC（從 WZ life 節點載入）。</summary>
    public IReadOnlyList<MapNpc> Npcs { get; init; } = Array.Empty<MapNpc>();

    /// <summary>地圖靜態怪物出生點（從 WZ life 節點載入）。</summary>
    public IReadOnlyList<MapMonster> Monsters { get; init; } = Array.Empty<MapMonster>();

    /// <summary>
    /// P084：對照 Java <c>MapleMap.getPortal(id)</c> + 找不到時 <c>getPortal(0)</c>（換圖 <c>changeMap(to, pto)</c>
    /// 與登入 <c>loadCharFromDB</c> 的 <c>initialSpawnPoint</c> 都這樣取落地點）；地圖沒有任何 portal 時回 null。
    /// </summary>
    public MapPortal? GetPortalOrFirst(int portalId)
        => Portals.FirstOrDefault(p => p.Id == portalId) ?? Portals.FirstOrDefault(p => p.Id == 0) ?? Portals.FirstOrDefault();

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

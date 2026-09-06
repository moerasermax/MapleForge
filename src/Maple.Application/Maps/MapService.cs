using Maple.Core.Data;
using Maple.Core.Maps;
using Maple.Core.World;

namespace Maple.Application.Maps;

/// <summary>
/// 地圖資料服務：從 IDataProvider (WZ) 載入地圖定義，並以 ConcurrentDictionary 快取（運行時不需重載）。
/// 對照舊 MapleMapFactory（去掉 DB 互動，只從 WZ 讀靜態資料）。
/// </summary>
public sealed class MapService
{
    private readonly IDataProvider _data;

    public MapService(IDataProvider data)
    {
        _data = data;
    }

    /// <summary>
    /// 依 mapId 載入地圖資料（portals + footholds）。
    /// 失敗時回傳基本空地圖（不拋例外，讓呼叫端繼續）。
    /// </summary>
    public MapData LoadMap(int mapId)
    {
        var imgPath = GetMapImagePath(mapId);
        var mapImg = _data.GetAt("Map", imgPath);
        if (mapImg is null)
        {
            return new MapData { MapId = mapId };
        }

        var info = mapImg["info"];
        var returnMapId = GetInt(info, "returnMap", mapId);
        var town = GetInt(info, "town", 0) != 0;
        var fieldLimit = GetLong(info, "fieldLimit", 0);

        var portals = LoadPortals(mapImg["portal"]);
        var footholds = LoadFootholds(mapImg["foothold"]);
        var npcs = LoadNpcs(mapImg["life"]);
        var monsters = LoadMonsters(mapImg["life"]);

        return new MapData
        {
            MapId = mapId,
            ReturnMapId = returnMapId,
            Town = town,
            FieldLimit = fieldLimit,
            Portals = portals,
            Footholds = footholds,
            Npcs = npcs,
            Monsters = monsters,
        };
    }

    /// <summary>Returns whether static map data exists for the map id.</summary>
    public bool MapExists(int mapId) => _data.GetAt("Map", GetMapImagePath(mapId)) is not null;

    /// <summary>從 Mob.wz 載入怪物模板數值；找不到時回傳 null。</summary>
    public MobStats? LoadMobStats(int monsterId)
    {
        var mobImg = _data.GetAt("Mob", $"{monsterId:D7}.img");
        var info = mobImg?["info"];
        if (info is null)
        {
            return null;
        }

        var maxHp = Math.Max(1, GetLong(info, "maxHP", 1));
        var maxMp = Math.Max(0, GetInt(info, "maxMP", 0));
        var level = (short)Math.Clamp(GetInt(info, "level", 1), short.MinValue, short.MaxValue);
        var exp = Math.Max(0, GetInt(info, "exp", 0));
        var boss = GetInt(info, "boss", 0) > 0 || monsterId is 8810018 or 9410066 || (monsterId >= 8810118 && monsterId <= 8810122);
        var friendly = GetInt(info, "damagedByMob", 0) > 0;
        var fly = mobImg?["fly"] is not null;
        var mobile = mobImg?["move"] is not null || fly;
        var selfDestructAnimation = (sbyte)Math.Clamp(
            GetInt(info["selfDestruction"], "action", -1),
            sbyte.MinValue,
            sbyte.MaxValue);

        return new MobStats(
            MonsterId: monsterId,
            MaxHp: maxHp,
            MaxMp: maxMp,
            Level: level,
            Exp: exp,
            Boss: boss,
            Mobile: mobile,
            Friendly: friendly,
            HpDisplayType: GetHpDisplayType(monsterId, boss, friendly),
            SelfDestructAnimation: selfDestructAnimation,
            Fly: fly);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetMapImagePath(int mapId)
    {
        var folder = $"Map{mapId / 100_000_000}";
        var file = $"{mapId:D9}.img";
        return $"Map/{folder}/{file}";
    }

    private static int GetInt(IDataNode? node, string key, int defaultValue)
    {
        var child = node?[key];
        return child?.Value switch
        {
            int v => v,
            short v => v,
            long v when v <= int.MaxValue && v >= int.MinValue => (int)v,
            byte v => v,
            sbyte v => v,
            string s when int.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static long GetLong(IDataNode? node, string key, long defaultValue)
    {
        var child = node?[key];
        return child?.Value switch
        {
            long v => v,
            int v => v,
            short v => v,
            byte v => v,
            sbyte v => v,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    private static string GetString(IDataNode? node, string key, string defaultValue = "")
    {
        var child = node?[key];
        return child?.Value is string s ? s : defaultValue;
    }

    private static IReadOnlyList<MapPortal> LoadPortals(IDataNode? portalNode)
    {
        if (portalNode is null) return Array.Empty<MapPortal>();

        var portals = new List<MapPortal>();
        var i = 0;

        while (true)
        {
            var entry = portalNode[$"{i}"];
            if (entry is null) break;

            portals.Add(new MapPortal
            {
                Id = i,
                Name = GetString(entry, "pn"),
                Type = GetInt(entry, "pt", 0),
                X = GetInt(entry, "x", 0),
                Y = GetInt(entry, "y", 0),
                TargetMapId = GetInt(entry, "tm", 999999999),
                TargetPortalName = GetString(entry, "tn"),
                Script = GetString(entry, "script"),
            });
            i++;
        }

        return portals;
    }

    /// <summary>
    /// 從 WZ <c>life</c> 節點載入 NPC（type 首字 &quot;n&quot;）。怪物(type &quot;m&quot;)留待戰鬥階段。
    /// 對照 Java MapleMapFactory.loadLife：life 子節點(numbered) 有 type/id/cy/f/fh/rx0/rx1/x/hide。
    /// </summary>
    private static IReadOnlyList<MapNpc> LoadNpcs(IDataNode? lifeNode)
    {
        if (lifeNode is null) return Array.Empty<MapNpc>();

        var npcs = new List<MapNpc>();
        var i = 0;

        while (true)
        {
            var entry = lifeNode[$"{i}"];
            if (entry is null) break;
            i++;

            var type = GetString(entry, "type");
            if (type.Length == 0 || char.ToLowerInvariant(type[0]) != 'n') continue;   // 只收 NPC

            if (!int.TryParse(GetString(entry, "id"), out var npcId)) continue;

            npcs.Add(new MapNpc
            {
                NpcId = npcId,
                X = GetInt(entry, "x", 0),
                Cy = GetInt(entry, "cy", 0),
                F = GetInt(entry, "f", 0),
                Fh = GetInt(entry, "fh", 0),
                Rx0 = GetInt(entry, "rx0", 0),
                Rx1 = GetInt(entry, "rx1", 0),
                Hide = GetInt(entry, "hide", 0) == 1,
            });
        }

        return npcs;
    }

    /// <summary>
    /// 從 WZ <c>life</c> 節點載入怪物出生點（type 首字 &quot;m&quot;）。
    /// 對照 Java MapleMapFactory.loadLife：怪物位置用 x/y；cy 保留但 spawn 封包不用 cy。
    /// </summary>
    private static IReadOnlyList<MapMonster> LoadMonsters(IDataNode? lifeNode)
    {
        if (lifeNode is null) return Array.Empty<MapMonster>();

        var monsters = new List<MapMonster>();
        var i = 0;

        while (true)
        {
            var entry = lifeNode[$"{i}"];
            if (entry is null) break;
            i++;

            var type = GetString(entry, "type");
            if (type.Length == 0 || char.ToLowerInvariant(type[0]) != 'm') continue;

            if (!int.TryParse(GetString(entry, "id"), out var monsterId)) continue;

            monsters.Add(new MapMonster
            {
                MonsterId = monsterId,
                X = GetInt(entry, "x", 0),
                Y = GetInt(entry, "y", 0),
                Cy = GetInt(entry, "cy", 0),
                F = GetInt(entry, "f", 0),
                Fh = GetInt(entry, "fh", 0),
                Rx0 = GetInt(entry, "rx0", 0),
                Rx1 = GetInt(entry, "rx1", 0),
                MobTime = GetInt(entry, "mobTime", 0),
                Team = (sbyte)Math.Clamp(GetInt(entry, "team", -1), sbyte.MinValue, sbyte.MaxValue),
                Hide = GetInt(entry, "hide", 0) == 1,
            });
        }

        return monsters;
    }

    private static IReadOnlyList<MapFoothold> LoadFootholds(IDataNode? fhNode)
    {
        if (fhNode is null) return Array.Empty<MapFoothold>();

        var footholds = new List<MapFoothold>();

        foreach (var layer in fhNode.Children.Values)
        {
            foreach (var group in layer.Children.Values)
            {
                foreach (var (idStr, fh) in group.Children)
                {
                    if (!int.TryParse(idStr, out var fhId)) continue;
                    footholds.Add(new MapFoothold
                    {
                        Id = fhId,
                        X1 = GetInt(fh, "x1", 0),
                        Y1 = GetInt(fh, "y1", 0),
                        X2 = GetInt(fh, "x2", 0),
                        Y2 = GetInt(fh, "y2", 0),
                        Next = GetInt(fh, "next", 0),
                        Prev = GetInt(fh, "prev", 0),
                    });
                }
            }
        }

        return footholds;
    }

    private static byte GetHpDisplayType(int monsterId, bool boss, bool friendly)
    {
        if (friendly) return 1;
        if (monsterId >= 9300184 && monsterId <= 9300215) return 2;
        return !boss || monsterId == 9410066 ? (byte)3 : (byte)0;
    }
}

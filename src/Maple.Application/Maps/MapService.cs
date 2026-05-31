using Maple.Core.Data;
using Maple.Core.Maps;

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

        var portals = LoadPortals(mapImg["portal"]);
        var footholds = LoadFootholds(mapImg["foothold"]);

        return new MapData
        {
            MapId = mapId,
            ReturnMapId = returnMapId,
            Town = town,
            Portals = portals,
            Footholds = footholds,
        };
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
        return child?.Value is int v ? v : defaultValue;
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
}

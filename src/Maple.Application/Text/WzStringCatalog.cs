using System.Collections.Concurrent;
using System.Globalization;
using Maple.Core.Data;

namespace Maple.Application.Text;

/// <summary>
/// P193：String.wz 名稱（Java <c>MapleItemInformationProvider.getName</c>、<c>MapleMap.getStreetName／getMapName</c>）。
/// 道具依類別查 Consume／Etc（Etc 子目錄）／Ins／Cash／Pet／Eqp（Eqp 各子類別）；地圖查 Map.img 各區域。結果快取。
/// </summary>
public sealed class WzStringCatalog
{
    private readonly IDataProvider _data;
    private readonly ConcurrentDictionary<int, string?> _itemNames = new();
    private readonly Lazy<IReadOnlyDictionary<int, (string Street, string Map)>> _maps;
    private readonly Lazy<IReadOnlyDictionary<int, string>> _equips;

    public WzStringCatalog(IDataProvider data)
    {
        _data = data;
        _maps = new Lazy<IReadOnlyDictionary<int, (string, string)>>(LoadMaps);
        _equips = new Lazy<IReadOnlyDictionary<int, string>>(LoadEquips);
    }

    public string? GetItemName(int itemId) => _itemNames.GetOrAdd(itemId, LoadItemName);

    public (string StreetName, string MapName)? GetMapName(int mapId)
        => _maps.Value.TryGetValue(mapId, out var names) ? names : null;

    private string? LoadItemName(int itemId)
    {
        var id = itemId.ToString(CultureInfo.InvariantCulture);
        var path = (itemId / 1_000_000) switch
        {
            1 => null,
            2 => $"Consume.img/{id}/name",
            3 => $"Ins.img/{id}/name",
            4 => $"Etc.img/Etc/{id}/name",
            5 => itemId / 10000 == 500 ? $"Pet.img/{id}/name" : $"Cash.img/{id}/name",
            _ => null,
        };
        if (path is null)
        {
            return _equips.Value.GetValueOrDefault(itemId);
        }

        return SafeGetAt(path)?.Value as string;
    }

    private IReadOnlyDictionary<int, (string, string)> LoadMaps()
    {
        var maps = new Dictionary<int, (string, string)>();
        if (SafeGetAt("Map.img") is not { } root)
        {
            return maps;
        }

        foreach (var region in root.Children.Values)
        {
            foreach (var (key, node) in region.Children)
            {
                if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mapId))
                {
                    maps[mapId] = (node["streetName"]?.Value as string ?? string.Empty, node["mapName"]?.Value as string ?? string.Empty);
                }
            }
        }

        return maps;
    }

    private IReadOnlyDictionary<int, string> LoadEquips()
    {
        var equips = new Dictionary<int, string>();
        if (SafeGetAt("Eqp.img/Eqp") is not { } root)
        {
            return equips;
        }

        foreach (var category in root.Children.Values)
        {
            foreach (var (key, node) in category.Children)
            {
                if (int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && node["name"]?.Value is string name)
                {
                    equips[id] = name;
                }
            }
        }

        return equips;
    }

    private IDataNode? SafeGetAt(string path)
    {
        try
        {
            return _data.GetAt("String", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }
}
